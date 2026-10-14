# Importer test report — 2026-10-10

## Scope
Tested the Python reference importer and SQLite persistence pipeline against `tests/fixtures/import_sample.json`. The fixture is synthetic and intentionally includes a PAL and NTSC-U Gran Turismo 4 release, a repeated serial, a semantically repeated row with a different source ID, a missing title, a record with missing serial/region/language, and invalid year/disc values.

## Results
- `python tests/test_gamedb_importer.py`: **5 tests passed**.
- `python tests/validate_static.py`: **passed** (SQLite schema executes, required tables exist, XAML handlers resolve, schema copy configured).
- `python -m py_compile importer/gamedb_importer.py tests/test_gamedb_importer.py`: **passed**.
- CLI smoke test against a fresh temporary database: **passed**. Preview reported 7 additions and 1 rejection; applying the fixture created 3 conceptual games, 4 release rows, 4 disc rows, 14 language assertions, 7 source records and 7 payload snapshots. PAL and NTSC-U remained distinct.
- Update test changed a source record's publisher/year and verified the linked release was updated while the prior payload snapshot remained available.
- Preview-only test verified no Game rows were created.
- Import without explicit approval is rejected.

## Important limits
- The sample is synthetic, not an actual downloaded GameDB-PS2 sample. Network DNS is unavailable in this execution environment, so the current upstream TSV/JSON could not be downloaded and its exact live field names/shape could not be confirmed here.
- The importer currently accepts a canonical JSON/TSV input shape and common field aliases. A production source adapter must be validated against the actual upstream file before full ingestion.
- These tests run the Python importer and SQLite engine available here. They do **not** prove the WPF application compiles or runs on Windows; no `dotnet`/MSBuild/C# compiler is installed in this environment.
- Data-source terms remain marked `ReviewRequired`. Passing `--source-rights-approved` is an explicit operator attestation, not legal advice or automatic proof that use is permitted.
- The importer is a pipeline prototype and is not yet wired into the WPF Admin UI. Do not present the project as a ready-to-use Windows beta.