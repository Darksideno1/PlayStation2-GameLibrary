# Windows first build and practical test — PLAYSTATION 2 GAME LIBRARY v0.1 BETA

**Purpose:** obtain the first real Windows build and report compile/runtime findings. This is a development baseline, not a verified release.

## Requirements
- Windows 10 or Windows 11, 64-bit.
- **.NET 8 SDK, latest available 8.0 servicing release** (8.0.xxx). The project targets `net8.0-windows` and uses WPF.
- Internet connection for the first NuGet restore.
- Python 3.10+ for the required Python-side release tests.

Official SDK download: https://dotnet.microsoft.com/en-us/download/dotnet/8.0

## Exact first build and launch
1. Extract the project ZIP to a writable folder.
2. Open PowerShell in the project root.
3. Run:

```powershell
dotnet --list-sdks
dotnet restore .\src\PlayStation2GameLibrary\PlayStation2GameLibrary.csproj
dotnet build .\src\PlayStation2GameLibrary\PlayStation2GameLibrary.csproj --configuration Debug --no-restore
dotnet run --project .\src\PlayStation2GameLibrary\PlayStation2GameLibrary.csproj --configuration Debug --no-build
```

## First practical test checklist
Record **Pass / Fail / Not tested** for:
1. Startup and database initialization.
2. Empty catalogue state.
3. GameDB import preview; cancellation must not mutate the catalogue.
4. Small controlled import, then search by title, publisher, serial.
5. Region filters and game details.
6. Repeat import without duplication.
7. Close and reopen with data retained.
8. USER and ADMIN differences.
9. Portable ZIP extracted and started on a clean Windows machine without development tools.

## Limitations
- Current baseline does not yet implement full collection editing, artwork, backup/restore UI or complete administrator/user separation.
- Passing Python tests and static checks does not prove WPF runs on Windows.
- Preserve `catalogue.db` before testing imports.
