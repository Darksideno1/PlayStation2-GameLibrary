# Archive manifest — v0.1 BETA build-pipeline candidate

This project ZIP contains the current recovered first-Windows-test source baseline plus the portable publishing scripts and user/developer documentation added on 2026-10-11.

Included: WPF source and project file, XAML windows/resources, C# GameDB importer, SQLite schema, Python reference importer, representative fixtures, Python tests, static validation, reports, build/check PowerShell scripts, portable guide, Windows build/test checklist, Version.txt, CHANGELOG.txt and FUNKCIONALNOSTI.txt.

Excluded intentionally: generated `bin`/`obj`, Python bytecode/cache, the full GameDB source JSON, and generated full-import SQLite test database. The full-source test evidence is documented in the reports; the raw source file is not redistributable as part of this package without separately confirming its applicable terms.

Release boundary: source checks passed in this Linux environment; no WPF compile, `dotnet publish`, Windows EXE launch or functional UI test has been run. The Windows packaging script is included but has not itself been executed here. This archive is a development/build-pipeline candidate, not a verified ready-to-use portable release.