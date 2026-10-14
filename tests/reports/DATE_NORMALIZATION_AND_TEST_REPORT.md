# GameDB-PS2 date normalization and test report

Updated: 2026-10-11

## Actual source data inspected

- File: `PS2.data.json` (local source file available in the working environment).
- Root shape: JSON object with 13,052 keyed records.
- Non-empty `release_date` values: 8514; empty/null values: 4538.
- Observed formats and counts:
  - `YYYY-MM-DD`: 8,066
  - `empty/null`: 4,538
  - `YYYY`: 399
  - `YYYY-MM`: 49
- All observed non-empty values match one of `YYYY`, `YYYY-MM`, or `YYYY-MM-DD`; validation found no malformed date/month/day in the inspected source.
- The prior bug was field selection: the normalizer looked for `year`/`release_year`/`date_year` but the actual source uses `release_date`. A year-only source value such as `2000` therefore became `None`.

## Code change

- `importer/gamedb_importer.py` now reads `release_date` and validates year-only, year-month, ISO date, ISO datetime and common explicit numeric date formats. Invalid dates and years outside 1970–2100 generate warnings; empty values remain missing evidence without fabricated values.
- `tests/test_real_gamedb_shape.py` now covers source field use and date parsing edge cases.
- `GameDbJsonImportService.cs` now reads the `release_date` field using a date parser and warns on invalid/out-of-range values. This C# change has not been compiled in this environment.
- `App.xaml` was added to provide the WPF startup window and resources referenced by `MainWindow.xaml`; this addresses a missing application entry/resource file found during source review. It has not been validated by a Windows build.
- The catalogue layout now has a game-details panel and a regional-release list query that displays release title, region, year, edition, serials and languages for a selected concept. This is source-level implementation only; no WPF runtime test has run.

## Tests run

- `python3 -m unittest discover -s tests -v`: 11 tests passed.
- `python3 -m py_compile importer/gamedb_importer.py tests/test_gamedb_importer.py tests/test_real_gamedb_shape.py`: passed.
- `python3 tests/validate_static.py`: passed (schema, required tables, XAML event-handler names and schema content configuration).
- Actual WPF build and GUI tests: NOT RUN. `dotnet` is not installed in the current environment.

## Remaining limitations

- The C# import service has not been compiled; date parser and all importer integration still require a Windows/.NET 8 build and functional testing.
- Catalogue game-concept linking currently uses normalized title matching as an initial heuristic. It must be reviewed and improved; same-title sequels/editions and different-title regional variants can require human review.
- The regional-release detail panel exists in source but is not yet runtime-verified; manual editing and duplicate-resolution controls remain unimplemented.
- This report does not certify the application as a working Windows release.