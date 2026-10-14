import json, sys, unittest
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "importer"))
from gamedb_importer import load_records, normalize_row, prepare_preview

class RealGameDbShapeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.path = ROOT / "tests" / "real_data" / "gamedb_real_sample.json"
        cls.raw = json.loads(cls.path.read_text(encoding="utf-8"))
        cls.records = load_records(cls.path)

    def test_actual_top_level_object_shape_and_source_keys(self):
        self.assertIsInstance(self.raw, dict)
        self.assertEqual(len(self.raw), len(self.records))
        self.assertTrue(all(r.get("external_id") for r in self.records))
        self.assertTrue(all(r.get("serial") for r in self.records))

    def test_actual_fields_region_language_and_date(self):
        record = next(r for r in self.records if r.get("release_date"))
        normalized = normalize_row(record, 1)
        self.assertEqual(normalized.external_id, record["external_id"])
        self.assertEqual(normalized.serial_raw, record["serial"])
        self.assertEqual(normalized.region, record["region"])
        self.assertEqual(normalized.year, int(record["release_date"][:4]))
        if record.get("language"):
            self.assertIn(record["language"], normalized.languages)

    def test_real_serial_collision_remains_distinct_source_records(self):
        collision = {k:v for k,v in self.raw.items() if k in {"MH-051013", "MH051013"}}
        if len(collision) == 2:
            rows = prepare_preview([dict(v, external_id=k) for k,v in collision.items()])
            self.assertEqual(len(rows), 2)
            self.assertNotEqual(rows[0].title, rows[1].title)
            self.assertEqual(rows[0].serial_normalized, rows[1].serial_normalized)

    def test_incomplete_source_metadata_is_warning_not_silent_fabrication(self):
        record = next(r for r in self.records if not r.get("language"))
        normalized = normalize_row(record, 1)
        self.assertEqual(normalized.languages, [])
        self.assertTrue(any("language" in w.casefold() for w in normalized.warnings))

if __name__ == "__main__": unittest.main()