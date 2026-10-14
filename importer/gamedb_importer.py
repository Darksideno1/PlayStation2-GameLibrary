#!/usr/bin/env python3
"""Auditable GameDB-PS2 preview/import pipeline (stdlib-only reference implementation).

Input contract: UTF-8 TSV or JSON converted to canonical records with fields:
external_id, title, serial, region, languages (list or ';'-separated), publisher,
year, edition, release_type, disc_number, source_url. Unknown source columns are
retained in raw_payload. The adapter intentionally does not merge by title.
"""
from __future__ import annotations
import argparse, csv, hashlib, json, re, sqlite3, sys, unicodedata, uuid
from dataclasses import dataclass, asdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable

SOURCE_NAME = 'GameDB-PS2'
SOURCE_URL = 'https://github.com/niemasd/GameDB-PS2'
SCHEMA = Path(__file__).resolve().parents[1] / 'database' / 'schema.sql'

def now(): return datetime.now(timezone.utc).isoformat(timespec='seconds')
def sha(value: str) -> str: return hashlib.sha256(value.encode('utf-8')).hexdigest()
def norm_serial(value: Any) -> str | None:
    if value is None: return None
    s = re.sub(r'[^A-Z0-9]', '', str(value).upper())
    # Common PS2 serial forms: SCES-xxxxx, SLUS_123.45, etc.
    return s or None
def norm_title(value: str) -> str:
    s = unicodedata.normalize('NFKC', value).casefold()
    s = ''.join(ch for ch in s if ch.isalnum() or ch.isspace())
    return ' '.join(s.split())
def parse_languages(value: Any) -> list[str]:
    if value is None: return []
    if isinstance(value, list): parts = value
    else: parts = re.split(r'[;,|/]', str(value))
    out=[]
    for p in parts:
        p=str(p).strip()
        if p and p.casefold() not in {'unknown','n/a','none','-'} and p not in out: out.append(p)
    return out

def pick(row: dict, *keys):
    lower={str(k).strip().casefold().replace(' ','_').replace('-','_'):v for k,v in row.items()}
    for key in keys:
        k=key.casefold().replace(' ','_').replace('-','_')
        if k in lower and lower[k] not in (None, ''): return lower[k]
    return None

@dataclass
class Normalized:
    row_number: int
    external_id: str | None
    title: str | None
    serial_raw: str | None
    serial_normalized: str | None
    region: str | None
    languages: list[str]
    publisher: str | None
    year: int | None
    edition: str | None
    release_type: str
    disc_number: int
    source_url: str | None
    raw_payload: dict
    payload_sha256: str
    action: str = 'reject'
    reason: str = ''
    warnings: list[str] | None = None
    candidate_release_ids: list[str] | None = None
    game_id: str | None = None
    release_id: str | None = None


def normalize_row(row: dict, row_number: int) -> Normalized:
    title=pick(row,'title','name','game_title','game')
    ext=pick(row,'external_id','id','record_id','game_id','source_id')
    serial=pick(row,'serial','serial_number','product_code','serials','disc_serial')
    region=pick(row,'region','region_code','territory')
    langs=parse_languages(pick(row,'languages','language','supported_languages'))
    publisher=pick(row,'publisher','publisher_name')
    year_raw=pick(row,'year','release_year','date_year')
    edition=pick(row,'edition','version','release')
    rtype=str(pick(row,'release_type','type','category') or 'Retail').strip()
    disc_raw=pick(row,'disc_number','disc','disc_no')
    warnings=[]; reason=[]
    title=str(title).strip() if title is not None else None
    serial_raw=str(serial).strip() if serial is not None else None
    serial_norm=norm_serial(serial_raw)
    region=str(region).strip() if region is not None else None
    if region and region.casefold() in {'unknown','n/a','none','-'}: region=None
    try: year=int(str(year_raw)[:4]) if year_raw is not None else None
    except (ValueError, TypeError): year=None; warnings.append(f'Invalid year value: {year_raw!r}')
    if year is not None and not 1970 <= year <= 2100:
        warnings.append(f'Year outside accepted range: {year}'); year=None
    try: disc=int(disc_raw) if disc_raw is not None else 1
    except (ValueError, TypeError): disc=1; warnings.append(f'Invalid disc number: {disc_raw!r}; defaulted to 1')
    if disc < 1: warnings.append(f'Invalid disc number: {disc}; defaulted to 1'); disc=1
    payload=json.dumps(row, ensure_ascii=False, sort_keys=True, separators=(',',':'))
    if not title: reason.append('Missing required title')
    if title and len(title)>300: reason.append('Title exceeds 300 characters')
    if serial_raw and not serial_norm: warnings.append('Serial supplied but normalization produced an empty value')
    if not serial_norm: warnings.append('Missing serial number; identity matching will be weaker')
    if not region: warnings.append('Missing region; release will be marked Unknown')
    if not langs: warnings.append('No language evidence supplied')
    if not ext: warnings.append('Missing source identifier; stable source-level idempotence unavailable for this row')
    return Normalized(row_number, str(ext).strip() if ext is not None else None, title, serial_raw, serial_norm,
        region, langs, str(publisher).strip() if publisher else None, year,
        str(edition).strip() if edition else None, rtype or 'Retail', disc,
        str(pick(row,'source_url','url','link') or '').strip() or None, row, sha(payload),
        'reject' if reason else 'pending', '; '.join(reason), warnings, [], None, None)


def load_records(path: Path) -> list[dict]:
    text=path.read_text(encoding='utf-8-sig')
    if path.suffix.casefold()=='.json':
        obj=json.loads(text)
        if isinstance(obj,dict):
            # support a top-level array nested under a common data key
            for k in ('games','data','records','results'):
                if isinstance(obj.get(k),list): obj=obj[k]; break
            else: obj=[dict(v, external_id=k) if isinstance(v,dict) else {'external_id':k,'title':v} for k,v in obj.items()]
        if not isinstance(obj,list): raise ValueError('JSON root must be an array or object of records')
        if not all(isinstance(x,dict) for x in obj): raise ValueError('Every JSON record must be an object')
        return obj
    with path.open('r',encoding='utf-8-sig',newline='') as f:
        sample=f.read(4096); f.seek(0)
        dialect=csv.Sniffer().sniff(sample, delimiters='\t,;')
        return list(csv.DictReader(f,dialect=dialect))


def initialize(db: sqlite3.Connection):
    db.execute('PRAGMA foreign_keys=ON')
    db.executescript(SCHEMA.read_text(encoding='utf-8'))
    db.executescript('''
    CREATE TABLE IF NOT EXISTS ImportRun(
      ImportRunId TEXT PRIMARY KEY, SourceId TEXT NOT NULL REFERENCES Source(SourceId),
      StartedUtc TEXT NOT NULL, CompletedUtc TEXT NULL, InputSha256 TEXT NOT NULL,
      InputName TEXT NOT NULL, Status TEXT NOT NULL, SummaryJson TEXT NULL);
    CREATE TABLE IF NOT EXISTS SourceRecordSnapshot(
      SnapshotId TEXT PRIMARY KEY, SourceRecordId TEXT NOT NULL REFERENCES SourceRecord(SourceRecordId),
      PayloadSha256 TEXT NOT NULL, RawPayloadJson TEXT NOT NULL, CapturedUtc TEXT NOT NULL,
      UNIQUE(SourceRecordId,PayloadSha256));
    CREATE TABLE IF NOT EXISTS ImportRow(
      ImportRunId TEXT NOT NULL REFERENCES ImportRun(ImportRunId) ON DELETE CASCADE,
      RowNumber INTEGER NOT NULL, ExternalId TEXT, PayloadSha256 TEXT NOT NULL,
      Action TEXT NOT NULL, Reason TEXT NOT NULL, WarningsJson TEXT NOT NULL,
      RawPayloadJson TEXT NOT NULL, PRIMARY KEY(ImportRunId,RowNumber));
    CREATE TABLE IF NOT EXISTS FieldAssertion(
      AssertionId TEXT PRIMARY KEY, SourceRecordId TEXT NOT NULL REFERENCES SourceRecord(SourceRecordId),
      EntityType TEXT NOT NULL, EntityId TEXT NOT NULL, FieldName TEXT NOT NULL,
      FieldValue TEXT NOT NULL, PayloadSha256 TEXT NOT NULL, ObservedUtc TEXT NOT NULL,
      UNIQUE(SourceRecordId,FieldName,PayloadSha256));
    ''')
    db.execute("INSERT OR IGNORE INTO Source(SourceId,SourceName,SourceUrl,TermsStatus,Notes) VALUES(?,?,?,?,?)",
      ('gamedb-ps2',SOURCE_NAME,SOURCE_URL,'ReviewRequired','Use only after applicable data/licence obligations are approved.'))
    db.commit()


def prepare_preview(rows: list[dict], existing: sqlite3.Connection | None=None) -> list[Normalized]:
    result=[normalize_row(row,i) for i,row in enumerate(rows,1)]
    # Exact duplicate IDs or identical payloads inside the incoming file: first valid row wins.
    seen_ids={}; seen_fingerprints={}
    for rec in result:
        if rec.action=='reject': continue
        if rec.external_id and rec.external_id in seen_ids:
            rec.action='skip'; rec.reason=f'Duplicate source identifier in input; first seen on row {seen_ids[rec.external_id]}'
            continue
        if rec.external_id: seen_ids[rec.external_id]=rec.row_number
        rec.action='add'
        # Semantic duplicate detection ignores the source-specific identifier and URL.
        semantic={k:v for k,v in rec.raw_payload.items() if str(k).strip().casefold().replace('-','_').replace(' ','_') not in {'external_id','id','record_id','game_id','source_id','source_url','url','link'}}
        fingerprint=sha(json.dumps(semantic,ensure_ascii=False,sort_keys=True,separators=(',',':')))
        if fingerprint in seen_fingerprints:
            rec.warnings=(rec.warnings or [])+[f'Semantically identical input record to row {seen_fingerprints[fingerprint]}; retained for provenance, possible duplicate candidate']
        else: seen_fingerprints[fingerprint]=rec.row_number
    if existing:
        for rec in result:
            if rec.action!='add': continue
            # Same source ID is an update if the content changed; no new source record.
            if rec.external_id:
                old=existing.execute('SELECT SourceRecordId,PayloadSha256 FROM SourceRecord WHERE SourceId=? AND ExternalId=? AND EntityType=?',
                                     ('gamedb-ps2',rec.external_id,'Release')).fetchone()
                if old:
                    rec.action='skip' if old[1]==rec.payload_sha256 else 'update'
                    rec.reason='Identical source record already imported' if old[1]==rec.payload_sha256 else 'Existing source identifier has changed payload'
            # candidate release matching: exact serial and region first, then normalized title+region.
            if rec.serial_normalized:
                matches=existing.execute('''SELECT DISTINCT r.ReleaseId FROM Disc d JOIN Release r ON r.ReleaseId=d.ReleaseId
                  WHERE d.SerialNormalized=? AND lower(r.RegionCode)=lower(?)''', (rec.serial_normalized, rec.region or 'Unknown')).fetchall()
                if not matches:
                    matches=existing.execute('''SELECT DISTINCT r.ReleaseId FROM Release r JOIN Game g ON g.GameId=r.GameId
                      WHERE lower(g.CanonicalTitle)=lower(?) AND lower(r.RegionCode)=lower(?)''', (rec.title, rec.region or 'Unknown')).fetchall()
                rec.candidate_release_ids=[m[0] for m in matches]
            else:
                matches=existing.execute('''SELECT r.ReleaseId FROM Release r JOIN Game g ON g.GameId=r.GameId
                  WHERE lower(g.CanonicalTitle)=lower(?) AND lower(r.RegionCode)=lower(?)''',(rec.title,rec.region or 'Unknown')).fetchall()
                rec.candidate_release_ids=[m[0] for m in matches]
            if rec.action=='add' and rec.candidate_release_ids:
                rec.warnings=(rec.warnings or [])+[f'{len(rec.candidate_release_ids)} possible existing release match(es); review required, not auto-merged']
    return result


def write_preview(path: Path, rows: list[Normalized], source_file: Path):
    data={'source':SOURCE_NAME,'source_url':SOURCE_URL,'input_file':source_file.name,
      'input_sha256':sha(source_file.read_text(encoding='utf-8-sig')),'generated_utc':now(),
      'counts':{a:sum(r.action==a for r in rows) for a in ('add','update','skip','reject')},
      'records':[asdict(r) for r in rows]}
    path.write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')


def apply_import(db: sqlite3.Connection, rows: list[Normalized], source_file: Path, approved: bool=False) -> dict:
    if not approved: raise RuntimeError('Import not applied: pass --apply only after reviewing the preview and confirming source-use rights.')
    run_id=str(uuid.uuid4()); started=now(); file_hash=sha(source_file.read_text(encoding='utf-8-sig'))
    summary={'add':0,'update':0,'skip':0,'reject':0,'warnings':0,'review_candidates':0}
    db.execute('BEGIN IMMEDIATE')
    try:
        db.execute('INSERT INTO ImportRun VALUES(?,?,?,?,?,?,?,?)',(run_id,'gamedb-ps2',started,None,file_hash,source_file.name,'Running',None))
        for rec in rows:
            summary[rec.action]=summary.get(rec.action,0)+1
            summary['warnings']+=len(rec.warnings or [])
            summary['review_candidates']+=len(rec.candidate_release_ids or [])
            db.execute('INSERT INTO ImportRow VALUES(?,?,?,?,?,?,?,?)',(run_id,rec.row_number,rec.external_id,rec.payload_sha256,rec.action,rec.reason,json.dumps(rec.warnings or []),json.dumps(rec.raw_payload,ensure_ascii=False,sort_keys=True)))
            if rec.action not in ('add','update') or not rec.title: continue
            source_row=db.execute('SELECT SourceRecordId,PayloadSha256 FROM SourceRecord WHERE SourceId=? AND ExternalId=? AND EntityType=?',('gamedb-ps2',rec.external_id,'Release')).fetchone() if rec.external_id else None
            if source_row and source_row[1]==rec.payload_sha256: continue
            srid=source_row[0] if source_row else str(uuid.uuid4())
            # Resolve Game only by exact normalized title; otherwise create a separate concept.
            game=None
            game_rows=db.execute('SELECT GameId,CanonicalTitle FROM Game').fetchall()
            wanted=norm_title(rec.title)
            for gid,gtitle in game_rows:
                if norm_title(gtitle)==wanted: game=gid; break
            if not game:
                game=str(uuid.uuid4()); stamp=now()
                db.execute('INSERT INTO Game(GameId,CanonicalTitle,SortTitle,FirstReleaseYear,Developer,Description,PrimaryGameImagePath,CreatedUtc,UpdatedUtc) VALUES(?,?,?,?,?,?,?,?,?)',
                           (game,rec.title,rec.title,rec.year,None,None,None,stamp,stamp))
            # Release identity is not title-only. Reuse only exact serial + region + release class + edition.
            release=None
            # On a source-record update, prefer its existing explicit link so changed
            # metadata updates the same entity rather than silently creating a new one.
            if source_row:
                linked=db.execute("SELECT EntityId FROM EntitySourceLink WHERE SourceRecordId=? AND EntityType='Release' ORDER BY Confidence DESC LIMIT 1",(srid,)).fetchone()
                if linked: release=linked[0]
            if release is None and rec.serial_normalized:
                match=db.execute('''SELECT r.ReleaseId FROM Release r JOIN Disc d ON d.ReleaseId=r.ReleaseId
                  WHERE d.SerialNormalized=? AND lower(r.RegionCode)=lower(?) AND lower(r.ReleaseType)=lower(?)
                  AND COALESCE(lower(r.Edition),'')=COALESCE(lower(?),'')''',
                  (rec.serial_normalized,rec.region or 'Unknown',rec.release_type,rec.edition or '')).fetchone()
                if match: release=match[0]
            if release is None:
                release=str(uuid.uuid4()); stamp=now()
                db.execute('INSERT INTO Release(ReleaseId,GameId,ReleaseTitle,RegionCode,Edition,ReleaseType,Publisher,ReleaseYear,Notes,CreatedUtc,UpdatedUtc) VALUES(?,?,?,?,?,?,?,?,?,?,?)',
                           (release,game,rec.title,rec.region or 'Unknown',rec.edition,rec.release_type,rec.publisher,rec.year,'Imported from GameDB-PS2',stamp,stamp))
            # Disc number is unique within release. Preserve serial variants in source record even if not attached.
            # Refresh only fields supported by this source; missing incoming values
            # never erase existing data. Raw assertions and snapshots preserve history.
            db.execute('''UPDATE Release SET ReleaseTitle=?, RegionCode=?, Edition=COALESCE(?,Edition),
              ReleaseType=COALESCE(?,ReleaseType), Publisher=COALESCE(?,Publisher),
              ReleaseYear=COALESCE(?,ReleaseYear), UpdatedUtc=? WHERE ReleaseId=?''',
              (rec.title,rec.region or 'Unknown',rec.edition,rec.release_type,rec.publisher,rec.year,now(),release))
            existing_disc=db.execute('SELECT DiscId,SerialNormalized FROM Disc WHERE ReleaseId=? AND DiscNumber=?',(release,rec.disc_number)).fetchone()
            if not existing_disc:
                db.execute('INSERT INTO Disc(DiscId,ReleaseId,DiscNumber,SerialRaw,SerialNormalized,DiscVersion,DiscLabel) VALUES(?,?,?,?,?,?,?)',
                           (str(uuid.uuid4()),release,rec.disc_number,rec.serial_raw,rec.serial_normalized,rec.edition,None))
            elif rec.serial_normalized and existing_disc[1] and existing_disc[1]!=rec.serial_normalized:
                db.execute('INSERT INTO ReviewQueue VALUES(?,?,?,?,?,?,?,?,?,?)',(str(uuid.uuid4()),'SerialConflict',release,None,
                  f'Disc {rec.disc_number} serial conflict: existing {existing_disc[1]}, incoming {rec.serial_normalized}',0.9,'Pending',now(),None,None))
            for lang in rec.languages:
                db.execute('INSERT OR IGNORE INTO ReleaseLanguage(ReleaseId,LanguageCode,EvidenceSourceRecordId,Confidence) VALUES(?,?,?,?)',(release,lang,srid,0.75))
            if source_row:
                db.execute('INSERT OR IGNORE INTO SourceRecordSnapshot VALUES(?,?,?,?,?)',(str(uuid.uuid4()),srid,source_row[1],db.execute('SELECT RawPayloadJson FROM SourceRecord WHERE SourceRecordId=?',(srid,)).fetchone()[0],now()))
                db.execute('UPDATE SourceRecord SET SourceUrl=?,RawTitle=?,RawSerial=?,RawRegion=?,RawPayloadJson=?,PayloadSha256=?,ImportedUtc=? WHERE SourceRecordId=?',
                           (rec.source_url,rec.title,rec.serial_raw,rec.region,json.dumps(rec.raw_payload,ensure_ascii=False,sort_keys=True),rec.payload_sha256,now(),srid))
            else:
                db.execute('INSERT INTO SourceRecord VALUES(?,?,?,?,?,?,?,?,?,?,?)',(srid,'gamedb-ps2',rec.external_id,rec.source_url,'Release',rec.title,rec.serial_raw,rec.region,json.dumps(rec.raw_payload,ensure_ascii=False,sort_keys=True),rec.payload_sha256,now()))
            db.execute('INSERT OR IGNORE INTO SourceRecordSnapshot VALUES(?,?,?,?,?)',(str(uuid.uuid4()),srid,rec.payload_sha256,json.dumps(rec.raw_payload,ensure_ascii=False,sort_keys=True),now()))
            db.execute('INSERT OR IGNORE INTO EntitySourceLink VALUES(?,?,?,?,?)',(srid,'Game',game,'ExactNormalizedTitle',0.65))
            db.execute('INSERT OR IGNORE INTO EntitySourceLink VALUES(?,?,?,?,?)',(srid,'Release',release,'SerialRegionEdition' if rec.serial_normalized else 'ProvisionalSourceRecord',0.8 if rec.serial_normalized else 0.45))
            for field,value in [('title',rec.title),('serial',rec.serial_raw),('region',rec.region),('publisher',rec.publisher),('year',rec.year),('languages',rec.languages)]:
                if value is not None:
                    db.execute('INSERT OR IGNORE INTO FieldAssertion VALUES(?,?,?,?,?,?,?,?)',(str(uuid.uuid4()),srid,'Release',release,field,json.dumps(value,ensure_ascii=False),rec.payload_sha256,now()))
            for candidate in rec.candidate_release_ids or []:
                db.execute('INSERT INTO ReviewQueue VALUES(?,?,?,?,?,?,?,?,?,?)',(str(uuid.uuid4()),'PossibleDuplicate',candidate,srid,
                  f'Possible match for imported title {rec.title!r}, region {rec.region or "Unknown"}, serial {rec.serial_normalized or "missing"}',0.6,'Pending',now(),None,None))
        db.execute('UPDATE ImportRun SET CompletedUtc=?,Status=?,SummaryJson=? WHERE ImportRunId=?',(now(),'Completed',json.dumps(summary),run_id))
        db.commit()
    except Exception:
        db.rollback(); raise
    return summary


def report(rows):
    print('PREVIEW — no database changes have been made')
    print('row | action | title | serial | region | warnings/reason')
    for r in rows:
        notes='; '.join(([r.reason] if r.reason else []) + (r.warnings or []))
        print(f'{r.row_number} | {r.action} | {r.title or "<missing>"} | {r.serial_raw or "<missing>"} | {r.region or "Unknown"} | {notes}')
    print('Summary:',json.dumps({a:sum(r.action==a for r in rows) for a in ('add','update','skip','reject')},ensure_ascii=False))


def main(argv=None):
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('input',type=Path); p.add_argument('--db',type=Path,default=Path('Data/catalog.db'))
    p.add_argument('--preview',type=Path,default=Path('import-preview.json')); p.add_argument('--apply',action='store_true')
    p.add_argument('--source-rights-approved',action='store_true',help='Explicitly confirm that applicable source terms have been reviewed/approved.')
    args=p.parse_args(argv)
    records=load_records(args.input); args.db.parent.mkdir(parents=True,exist_ok=True)
    con=sqlite3.connect(args.db); initialize(con)
    rows=prepare_preview(records,con); write_preview(args.preview,rows,args.input); report(rows)
    print(f'Preview written: {args.preview}')
    if args.apply:
        if not args.source_rights_approved: raise SystemExit('Refusing import: review applicable data licence/terms and pass --source-rights-approved.')
        print('Applied:',json.dumps(apply_import(con,rows,args.input,True),ensure_ascii=False))
    con.close()
    return 0
if __name__=='__main__':
    try: sys.exit(main())
    except Exception as exc: print(f'ERROR: {exc}',file=sys.stderr); sys.exit(2)