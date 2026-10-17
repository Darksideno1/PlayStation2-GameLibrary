#!/usr/bin/env python3
from __future__ import annotations

import argparse
import collections
import json
import re
from pathlib import Path

ENGLISH = {"en", "eng", "english"}
US = {"u", "us", "usa", "northamerica", "north america", "ntsc-u", "ntsc-u/c"}
PAL = {"e", "eu", "europe", "pal", "europe/pal", "australia", "au"}
JP = {"j", "jp", "japan", "ntsc-j"}
KR = {"k", "kr", "kor", "korea", "south korea"}

# Diagnostic patterns. The importer can later expose these in a rejected/review list.
NON_GAME_PATTERNS = [
    r"\bdemo\b", r"\bsampler\b", r"\bpreview\b", r"\btrial\b", r"\btaikenban\b",
    r"tentou shiyuu", r"magazine", r"buyers guide", r"players guide", r"yearbook",
    r"\bdvd\s*player\b", r"\bdvd\s*test\s*disc\b", r"\butility\s*disc\b",
    r"\bnetwork\s+(?:adaptor|adapter|access).*disc\b", r"online start-up disc",
    r"\bps2\s+linux\b", r"linux \(for playstation 2\)", r"manufacturing", r"seizou kensa",
    r"\bkiosk\b", r"\bbonus\s+disc\b", r"\bbonus\s+demo\b",
]


def text(value) -> str:
    if value is None:
        return ""
    if isinstance(value, str):
        return value.strip()
    return str(value).strip()


def serial_key(value: str) -> str:
    return re.sub(r"[^A-Z0-9]", "", value.upper())


def title_key(value: str) -> str:
    value = value.casefold().strip()
    value = re.sub(r"[™®©]", "", value)
    value = re.sub(r"\s+", " ", value)
    return value


def languages(record: dict) -> list[str]:
    raw = record.get("language", record.get("languages"))
    if raw is None:
        return []
    values = raw if isinstance(raw, list) else re.split(r"[,;/|]", text(raw))
    return list(dict.fromkeys(text(v) for v in values if text(v)))


def genres(record: dict) -> list[str]:
    raw = record.get("genre")
    if raw is None:
        return []
    values = raw if isinstance(raw, list) else [raw]
    return [text(v).rstrip(".") for v in values if text(v)]


def bucket(record: dict) -> str:
    value = text(record.get("region")).casefold()
    if value in US:
        return "NTSC-U/C"
    if value in PAL:
        return "PAL"
    if value in JP:
        return "NTSC-J"
    if value in KR:
        return "Korea"
    return text(record.get("region")) or "Unknown"


def has_english(record: dict) -> bool:
    return any(x.casefold() in ENGLISH for x in languages(record))


def is_clear_non_game(record: dict) -> bool:
    title = text(record.get("title"))
    return any(re.search(pattern, title, re.I) for pattern in NON_GAME_PATTERNS)


def primary_rank(record: dict) -> tuple[int, int, str]:
    region_rank = {"NTSC-U/C": 0, "PAL": 1, "NTSC-J": 2, "Korea": 3}.get(bucket(record), 4)
    date = text(record.get("release_date"))
    year = int(date[:4]) if len(date) >= 4 and date[:4].isdigit() else 9999
    return region_rank, year, serial_key(text(record.get("serial")))


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--source", type=Path, required=True)
    ap.add_argument("--report", type=Path, required=True)
    args = ap.parse_args()

    data = json.loads(args.source.read_text(encoding="utf-8-sig"))
    if not isinstance(data, dict):
        raise SystemExit("Official PS2.data.json must be a top-level object")

    records = []
    for source_key, value in data.items():
        if isinstance(value, dict):
            row = dict(value)
            row["_source_key"] = source_key
            records.append(row)

    by_title: dict[str, list[dict]] = collections.defaultdict(list)
    for row in records:
        if text(row.get("title")):
            by_title[title_key(text(row.get("title")))].append(row)

    region_counts = collections.Counter(bucket(x) for x in records)
    language_counts = collections.Counter(v for x in records for v in languages(x))
    genre_counts = collections.Counter(v for x in records for v in genres(x))
    prefix_counts = collections.Counter(re.split(r"[-_]", text(x.get("serial")).upper())[0] for x in records if text(x.get("serial")))
    array_language_count = sum(isinstance(x.get("language", x.get("languages")), list) for x in records)
    missing_language_count = sum(not languages(x) for x in records)
    clear_non_game = [x for x in records if is_clear_non_game(x)]
    jp_kr = [x for x in records if bucket(x) in {"NTSC-J", "Korea"}]
    jp_kr_no_en = [x for x in jp_kr if not has_english(x)]

    serial_region = collections.Counter((serial_key(text(x.get("serial"))), bucket(x)) for x in records if text(x.get("serial")))
    duplicate_serial_region = [(key, n) for key, n in serial_region.items() if n > 1]

    candidate = [
        x for x in records
        if text(x.get("title"))
        and text(x.get("serial"))
        and not is_clear_non_game(x)
        and not (bucket(x) in {"NTSC-J", "Korea"} and not has_english(x))
    ]
    candidate_titles: dict[str, list[dict]] = collections.defaultdict(list)
    for x in candidate:
        candidate_titles[title_key(text(x.get("title")))].append(x)
    primaries = [min(group, key=primary_rank) for group in candidate_titles.values()]
    primary_regions = collections.Counter(bucket(x) for x in primaries)

    san_andreas = [
        x for x in records
        if title_key(text(x.get("title"))) == title_key("Grand Theft Auto: San Andreas")
    ]

    lines = [
        "PLAYSTATION 2 GAME LIBRARY - OFFICIAL FULL GAMEDB VALIDATION",
        "============================================================",
        f"Source: {args.source.name}",
        "",
        "RAW OFFICIAL SOURCE",
        f"Release records: {len(records):,}",
        f"Distinct exact normalized titles: {len(by_title):,}",
        f"Titles represented by multiple releases: {sum(len(v) > 1 for v in by_title.values()):,}",
        f"Language field is an array: {array_language_count:,}",
        f"Records without language evidence: {missing_language_count:,}",
        f"Duplicate normalized serial+region pairs: {len(duplicate_serial_region):,}",
        "",
        "REGIONS",
    ]
    lines.extend(f"{k}: {v:,}" for k, v in region_counts.most_common())
    lines += ["", "LANGUAGES (top 25)"]
    lines.extend(f"{k}: {v:,}" for k, v in language_counts.most_common(25))
    lines += ["", "GENRES (top 25)"]
    lines.extend(f"{k}: {v:,}" for k, v in genre_counts.most_common(25))
    lines += ["", "SERIAL PREFIXES (top 30)"]
    lines.extend(f"{k}: {v:,}" for k, v in prefix_counts.most_common(30))

    lines += [
        "",
        "POLICY PREVIEW",
        f"Clear demo/system/utility/etc. records flagged: {len(clear_non_game):,}",
        f"JP/KR release records: {len(jp_kr):,}",
        f"JP/KR release records without English: {len(jp_kr_no_en):,}",
        f"Candidate accepted release records: {len(candidate):,}",
        f"Candidate conceptual games by exact normalized title: {len(candidate_titles):,}",
        f"Candidate conceptual games with multiple releases: {sum(len(v) > 1 for v in candidate_titles.values()):,}",
        "Primary release region preview (NTSC-U/C > PAL > JP/KR):",
    ]
    lines.extend(f"  {k}: {v:,}" for k, v in primary_regions.most_common())

    lines += ["", "SAN ANDREAS MULTI-RELEASE CHECK"]
    if san_andreas:
        for row in sorted(san_andreas, key=primary_rank):
            lines.append(f"- {text(row.get('serial'))} | {bucket(row)} | {', '.join(languages(row)) or 'no language'}")
    else:
        lines.append("ERROR: Grand Theft Auto: San Andreas not found")

    lines += ["", "SAMPLE FLAGGED NON-GAME/DEMO RECORDS"]
    for row in clear_non_game[:50]:
        lines.append(f"- {text(row.get('serial'))} | {text(row.get('title'))} | {bucket(row)}")

    lines += [
        "",
        "NOTES",
        "- This is a validation report only; it never writes catalogue.db.",
        "- Exact-title grouping is a safe first pass, not the final merge strategy for every localized title.",
        "- ADMIN Merge/Split remains the safety mechanism for edge cases.",
        "- The production importer must store every language from a source array as a separate ReleaseLanguage row.",
    ]

    report = "\n".join(lines) + "\n"
    print(report, end="")
    args.report.write_text(report, encoding="utf-8")

    # Hard checks that protect us from accidentally validating the old flattened seed.
    if len(records) < 5000:
        raise SystemExit(f"Official source looks incomplete: only {len(records)} release records")
    san_serials = {text(x.get("serial")).upper() for x in san_andreas}
    expected = {"SLES-52541", "SLES-52927", "SLUS-20946"}
    if not expected.issubset(san_serials):
        raise SystemExit(f"Official source is missing expected San Andreas releases: {sorted(expected - san_serials)}")
    if array_language_count == 0:
        raise SystemExit("Official source unexpectedly contains no array-valued language fields")


if __name__ == "__main__":
    main()
