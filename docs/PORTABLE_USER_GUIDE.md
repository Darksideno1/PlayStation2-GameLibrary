# PLAYSTATION 2 GAME LIBRARY v0.1 BETA — Portable guide

## Ordinary use
1. Extract the complete ZIP to a folder you can write to.
2. Keep the `USER` and `ADMIN` folders together; do not move just the EXE out of its folder.
3. Open `USER` and double-click `PlayStation2GameLibrary.exe`.
4. The first launch creates the local SQLite database at `%LOCALAPPDATA%\PlayStation2GameLibrary\Data\catalogue.db`.
5. Close the application normally. The database remains on the computer and is reused on later launches.

## Administrator
Use `ADMIN\PlayStation2GameLibrary.exe` only for administrator tasks. Keep the ADMIN and USER folders in the same package. The current beta's administrator separation is a user-interface/build distinction, not Windows account security: do not treat it as access control against someone who can inspect or modify the files.

## Backup
Before testing imports or replacing the package, close the application and copy `%LOCALAPPDATA%\PlayStation2GameLibrary\Data\catalogue.db` to a safe location. The portable executable is self-contained for .NET, but the application data is intentionally stored separately so it survives package updates and folder moves.

## If Windows shows a warning or the app will not open
- Confirm you extracted the ZIP fully rather than running the EXE from inside the ZIP preview.
- Keep the `database` folder next to the EXE; `database\schema.sql` is currently a required companion file.
- Check Windows Security/SmartScreen messages before allowing execution; only proceed if you trust the package source.
- If a message box reports an error, copy the full text. Also provide Windows version and whether the error happened on first launch, search, details or import.
- Do not delete the database to troubleshoot unless you have a backup; it contains the local catalogue.

## Known beta limitations
The UI, database initialization, search, region filter, details and import workflow must still pass practical Windows tests before this is called a verified release. Collection management, editing, artwork, and full backup/restore are not complete. Import only data you have reviewed and are entitled to use.
