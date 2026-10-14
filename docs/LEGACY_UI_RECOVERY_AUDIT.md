# Legacy Windows v0.1 BETA UI recovery — verified source comparison

Source archive reviewed: `PS2_Game_Library_Windows_v0.1_BETA_WORKING_PRIMARY_MANUAL.zip` (37 ZIP entries). This document is an implementation guardrail, not evidence that the old UI has already been restored.

## Non-negotiable preservation

The current `main` code is backed up at branch `backup/pre-legacy-ui-restoration-2026-10-14`. Do not overwrite existing user databases or migrate their primary keys without a verified transaction and rollback path.

## Old frontend (source archive)

- `src/MainWindow.xaml`: one WPF WebView2 host, 1600×900, minimum 1180×680.
- `src/wwwroot/index.html`: original approximately 100 KB HTML/CSS/JS frontend. Primary colors: `#f3f6fb`, `#dbe4ef`, `#1261e8`, `#eaf2ff`. Layout: titlebar, 230px left sidebar, main workspace, dashboard cards, modal dialogs and responsive game views.
- Legacy UI regression function names: `renderDashboard`, `renderCatalog`, `renderCollection`, `renderWishlist`, `renderDetails`, `renderStatistics`, `renderGameForm`, `openReleaseModal`, `releaseViewCard`, `viewReleaseImages`, `renderHistory`, `renderBackup`, `renderSettings`, `renderQuickDetail`, `openCollectionModal`, `init`.
- Backend bridge: `src/MainWindow.xaml.cs` uses WebView2 messages/RPC to `DatabaseService.cs`. The old backend includes catalogue, regional release, media, personal backup, catalog update, merge/split and admin functionality.
- Separate `PS2GameLibrary.User.csproj` and `PS2GameLibrary.Admin.csproj` use `net10.0-windows`, `Microsoft.Web.WebView2`, `Microsoft.Data.Sqlite`, `SharpCompress`. Their personal data is in each portable mode's own `Data` directory.

## Current GitHub application

- Native WPF/XAML (`net8.0-windows`) with `Game`, `Release`, `CollectionCopy` SQLite schema and a starter GameDB catalogue.
- USER and ADMIN publish from a single csproj with a conditional compilation symbol.
- Catalogue population, collection insert and persistence have passed Windows CI smoke tests. Preserve these guarantees and their tests.
- Different SQLite model and database location (`%LOCALAPPDATA%\PlayStation2GameLibrary\Data`); DO NOT point the old DatabaseService directly at the current catalogue.db.

## Restoration acceptance criteria

1. Restore the actual archived `src/wwwroot/index.html` and its static assets, not a visual approximation; use WebView2 as the host.
2. Restore and adapt its message handlers so every visible navigation/action has a working backend or an explicit disabled state.
3. Preserve existing SQLite game IDs and personal CollectionCopy records. Implement a schema adapter or transactional migration with rollback, validated on an existing populated database.
4. Keep the initial catalogue and import path working, including offline first launch and 3,000+ populated games.
5. Test UI navigation and the complete Catalog → Add to Collection → My Collection → restart workflow in a Windows runner.
6. Preserve English UI and separate USER/ADMIN output. Never distribute a ZIP on the basis of compilation alone.
7. Maintain a real regression list covering the 16 archived JS entry points, admin release editing, game primary image, front/full/disc/manual media, catalog-only updates and personal backups.

## Current progress

- Reviewed the source ZIP and mapped architecture, project files, data model and key UI functions.
- Preserved the current GitHub state on a dedicated backup branch.
- **Not yet restored:** exact WebView2 frontend and backend; this document does not mark them implemented.
