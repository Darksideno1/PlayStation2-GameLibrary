# Project repair and verification report — 2026-10-13

## Changes actually made

- `scripts/Build-Portable.ps1`: removed the `SkipTests` switch so the release build always runs tests; checks for `python`, `python3`, or the Windows `py -3` launcher before testing; reports the discovered interpreter/version; gives a specific diagnostic if Python cannot be found or cannot run; captures native process exit codes immediately; reports failing test stage and propagates its original exit code; also captures and checks `dotnet` and publish exit codes.
- `tests/validate_static.py`: added contract checks to guard against reintroducing a test-skipping switch and to verify Python discovery/error messaging and test exit-code propagation are present in the build script.
- `README.md`: documented the build-script behavior and the remaining Windows verification boundary.

## Checks run in this environment

- Python unit tests: **EXECUTED — PASS**, 16 tests passed (`python3 -m unittest discover -s tests -v`).
- Static schema/XAML/build-script validation: **EXECUTED — PASS** (`python3 tests/validate_static.py`).
- Python syntax compilation: **NOT YET RUN** in this report.
- PowerShell parsing/execution: **NOT EXECUTED**; `pwsh`/Windows PowerShell is unavailable in this environment.
- .NET/WPF build and self-contained publish: **NOT EXECUTED**; `dotnet` / MSBuild is unavailable in this environment.
- Windows launch/functional checks and ZIP executable-entry checks for a real publish: **NOT EXECUTED**. No finished portable app package is included because this environment cannot compile/publish the WPF app.

## Remaining concrete requirement

Run `scripts\Build-Portable.ps1` on Windows 10/11 with the .NET 8 SDK and Python 3 installed. It will run the tests first and will stop with the original failing test exit code if a test stage fails. Only after successful USER and ADMIN self-contained publishes can it create and inspect `dist\PLAYSTATION_2_GAME_LIBRARY_v0.1_BETA_PORTABLE.zip`. The end-user package is intended to be self-contained and not require a separately installed Python, .NET SDK, or .NET Runtime.
