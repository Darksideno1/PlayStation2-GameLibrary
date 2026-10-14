import json, sqlite3, tempfile, unittest
from pathlib import Path
import sys
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'importer'))
import gamedb_importer as imp

class ImporterTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory(); self.addCleanup(self.tmp.cleanup)
        self.db=sqlite3.connect(Path(self.tmp.name)/'test.db')
        self.addCleanup(self.db.close)
        imp.initialize(self.db)
        self.input=ROOT/'tests/fixtures/import_sample.json'
        self.rows=imp.load_records(self.input)

    def test_preview_handles_regional_variants_bad_incomplete_and_duplicates(self):
        preview=imp.prepare_preview(self.rows,self.db)
        self.assertEqual(len(preview),8)
        self.assertEqual(sum(r.action=='reject' for r in preview),1)
        self.assertEqual(sum(r.action=='skip' for r in preview),0)
        self.assertEqual(sum(r.action=='add' for r in preview),7)
        self.assertTrue(any('Semantically identical input record' in w for w in preview[7].warnings))
        self.assertEqual(preview[0].serial_normalized,'SCES51719')
        self.assertEqual(preview[1].serial_normalized,'SCUS97328')
        self.assertNotEqual(preview[0].region,preview[1].region)
        self.assertTrue(any('Missing serial number' in w for w in preview[5].warnings))
        self.assertTrue(any('Invalid year' in w for w in preview[6].warnings))
        self.assertEqual(preview[4].action,'reject')

    def test_apply_is_transactional_idempotent_and_keeps_regions_separate(self):
        preview=imp.prepare_preview(self.rows,self.db)
        result=imp.apply_import(self.db,preview,self.input,approved=True)
        self.assertEqual(result['reject'],1)
        games=self.db.execute('select count(*) from Game').fetchone()[0]
        releases=self.db.execute('select count(*) from Release').fetchone()[0]
        self.assertEqual(games,3) # Gran Turismo 4 + two incomplete-evidence concepts
        self.assertGreaterEqual(releases,4)
        regions={r[0] for r in self.db.execute('select RegionCode from Release')}
        self.assertIn('PAL',regions); self.assertIn('NTSC-U',regions)
        serials={r[0] for r in self.db.execute('select SerialNormalized from Disc where SerialNormalized is not null')}
        self.assertIn('SCES51719',serials); self.assertIn('SCUS97328',serials)
        lang_count=self.db.execute('select count(*) from ReleaseLanguage').fetchone()[0]
        self.assertGreaterEqual(lang_count,5)
        # Identical second import should be previewed as skips; no new rows.
        again=imp.prepare_preview(self.rows,self.db)
        before=(self.db.execute('select count(*) from Game').fetchone()[0],self.db.execute('select count(*) from Release').fetchone()[0])
        imp.apply_import(self.db,again,self.input,approved=True)
        after=(self.db.execute('select count(*) from Game').fetchone()[0],self.db.execute('select count(*) from Release').fetchone()[0])
        self.assertEqual(before,after)
        self.assertGreater(self.db.execute('select count(*) from SourceRecordSnapshot').fetchone()[0],0)

    def test_changed_source_record_is_updated_and_old_payload_is_preserved(self):
        preview=imp.prepare_preview(self.rows,self.db)
        imp.apply_import(self.db,preview,self.input,approved=True)
        changed=[dict(r) for r in self.rows]
        changed[0]=dict(changed[0]); changed[0]['publisher']='Sony Updated'; changed[0]['year']=2006
        changed_file=Path(self.tmp.name)/'changed.json'
        changed_file.write_text(json.dumps(changed,ensure_ascii=False),encoding='utf-8')
        preview2=imp.prepare_preview(changed,self.db)
        self.assertEqual(preview2[0].action,'update')
        imp.apply_import(self.db,preview2,changed_file,approved=True)
        row=self.db.execute("SELECT r.Publisher,r.ReleaseYear FROM Release r JOIN EntitySourceLink l ON l.EntityId=r.ReleaseId JOIN SourceRecord s ON s.SourceRecordId=l.SourceRecordId WHERE s.ExternalId='gdb-001' AND l.EntityType='Release'").fetchone()
        self.assertEqual(row,('Sony Updated',2006))
        snaps=self.db.execute("SELECT count(*) FROM SourceRecordSnapshot s JOIN SourceRecord r ON r.SourceRecordId=s.SourceRecordId WHERE r.ExternalId='gdb-001'").fetchone()[0]
        self.assertGreaterEqual(snaps,2)

    def test_preview_only_does_not_mutate_catalogue(self):
        before=self.db.execute('select count(*) from Game').fetchone()[0]
        imp.prepare_preview(self.rows,self.db)
        after=self.db.execute('select count(*) from Game').fetchone()[0]
        self.assertEqual(before,after)

    def test_requires_explicit_approval(self):
        with self.assertRaises(RuntimeError): imp.apply_import(self.db,imp.prepare_preview(self.rows,self.db),self.input,approved=False)

if __name__=='__main__': unittest.main(verbosity=2)