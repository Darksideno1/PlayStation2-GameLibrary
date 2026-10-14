using System.IO;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace PlayStation2GameLibrary;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<GameListItem> _games = new();
    private string _databasePath = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        GamesGrid.ItemsSource = _games;
#if ADMIN_BUILD
        ImportCatalogueButton.Visibility = Visibility.Visible;
        Title = "PLAYSTATION 2 GAME LIBRARY — ADMIN — v0.1 BETA";
#else
        ImportCatalogueButton.Visibility = Visibility.Collapsed;
        Title = "PLAYSTATION 2 GAME LIBRARY — v0.1 BETA";
#endif
        Loaded += async (_, _) => await InitializeDatabaseAsync();
    }

    private async Task InitializeDatabaseAsync()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var dataDir = Environment.GetEnvironmentVariable("PS2_LIBRARY_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlayStation2GameLibrary", "Data");
            Directory.CreateDirectory(dataDir);
            _databasePath = Path.Combine(dataDir, "catalogue.db");
            await using var connection = new SqliteConnection($"Data Source={_databasePath}");
            await connection.OpenAsync();
            var schemaPath = Path.Combine(baseDir, "database", "schema.sql");
            if (File.Exists(schemaPath))
            {
                var sql = await File.ReadAllTextAsync(schemaPath);
                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                await command.ExecuteNonQueryAsync();
            }
            else
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE IF NOT EXISTS Game (GameId TEXT PRIMARY KEY, CanonicalTitle TEXT NOT NULL, SortTitle TEXT NOT NULL, FirstReleaseYear INTEGER NULL, Developer TEXT NULL);";
                await command.ExecuteNonQueryAsync();
            }
            // On first launch populate the real SQLite catalogue from bundled GameDB data.
            // Existing user databases are preserved, including personal collection records.
            await StarterCatalogue.EnsureSeededAsync(_databasePath, Path.Combine(baseDir, "data", "starter-catalog.json"));
            await RefreshGamesAsync();
            await RefreshFooterAsync();
            StatusText.Text = $"Catalogue ready: {_databasePath}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Database initialization failed. See startup diagnostics.";
            MessageBox.Show(this, ex.Message, "Database initialization error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RefreshGamesAsync()
    {
        if (string.IsNullOrWhiteSpace(_databasePath) || !File.Exists(_databasePath)) return;
        var search = SearchBox.Text.Trim();
        var region = (RegionFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All regions";
        var results = new List<GameListItem>();
        await using var connection = new SqliteConnection($"Data Source={_databasePath};Mode=ReadWriteCreate");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT g.GameId, g.CanonicalTitle, g.FirstReleaseYear, g.Developer,
       (SELECT COUNT(*) FROM Release r WHERE r.GameId = g.GameId) AS ReleaseCount,
       (SELECT group_concat(DISTINCT rl.LanguageCode) FROM Release r JOIN ReleaseLanguage rl ON rl.ReleaseId = r.ReleaseId WHERE r.GameId = g.GameId) AS Languages
FROM Game g
WHERE ($search = '' OR g.CanonicalTitle LIKE $pattern OR COALESCE(g.Developer,'') LIKE $pattern
   OR EXISTS (SELECT 1 FROM Release r WHERE r.GameId = g.GameId AND (COALESCE(r.Publisher,'') LIKE $pattern OR EXISTS (SELECT 1 FROM Disc d WHERE d.ReleaseId = r.ReleaseId AND COALESCE(d.SerialRaw,'') LIKE $pattern))))
AND ($region = 'All regions' OR EXISTS (SELECT 1 FROM Release r WHERE r.GameId = g.GameId AND
   (($region = 'Europe / PAL' AND r.RegionCode IN ('Europe','PAL','Europe/PAL')) OR
    ($region = 'North America' AND r.RegionCode IN ('NorthAmerica','USA','NTSC-U')) OR
    ($region = 'Japan' AND r.RegionCode IN ('Japan','NTSC-J')) OR
    ($region = 'Asia' AND r.RegionCode = 'Asia') OR
    ($region = 'Korea' AND r.RegionCode = 'Korea') OR
    ($region = 'Australia' AND r.RegionCode = 'Australia') OR
    ($region = 'Other / Unknown' AND r.RegionCode IN ('Other','Unknown')))))
ORDER BY g.SortTitle COLLATE NOCASE LIMIT 2000;";
        command.Parameters.AddWithValue("$search", search);
        command.Parameters.AddWithValue("$pattern", $"%{search}%");
        command.Parameters.AddWithValue("$region", region);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new GameListItem
            {
                GameId = reader.GetString(0),
                Title = reader.GetString(1),
                Year = reader.IsDBNull(2) ? "—" : reader.GetInt32(2).ToString(),
                Developer = reader.IsDBNull(3) ? "—" : reader.GetString(3),
                ReleaseCount = reader.GetInt32(4),
                Languages = reader.IsDBNull(5) ? "—" : reader.GetString(5)
            });
        }
        _games.Clear();
        foreach (var item in results) _games.Add(item);
        ResultCountText.Text = $"{results.Count:N0} results";
        DatabaseCountText.Text = $"Catalogue: {results.Count:N0} shown";
        FooterCatalogText.Text = $"{results.Count:N0} shown";
    }

    private async void ImportCatalogue_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select the official GameDB-PS2 JSON file",
            Filter = "GameDB-PS2 JSON (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            StatusText.Text = "Reading source and preparing import preview…";
            var service = new GameDbJsonImportService();
            var preview = await service.PreviewAsync(dialog.FileName, _databasePath);
            var sampleLines = preview.Rows.Where(r => r.Action == "Reject" || !string.IsNullOrWhiteSpace(r.Warnings) || r.Action is "Add" or "Update")
                .Take(20).Select(r => $"{r.Action}: {r.Title} [{r.Region}; {r.Serial}] — {r.Reason}{(string.IsNullOrWhiteSpace(r.Warnings) ? "" : " | Warning: " + r.Warnings)}");
            var previewStem = Path.Combine(Path.GetDirectoryName(dialog.FileName)!, $"GameDB-PS2-preview-{DateTime.Now:yyyyMMdd-HHmmss}");
            var previewPath = previewStem + ".txt";
            var fullPreviewPath = previewStem + ".json";
            await File.WriteAllTextAsync(fullPreviewPath, System.Text.Json.JsonSerializer.Serialize(preview, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            await File.WriteAllTextAsync(previewPath, $"Source: {dialog.FileName}\nSHA-256: {preview.Sha256}\nRecords: {preview.Rows.Count}\nAdd: {preview.AddCount}\nUpdate: {preview.UpdateCount}\nSkip: {preview.SkipCount}\nReject: {preview.RejectCount}\nRows with warnings: {preview.WarningCount}\nFull row-by-row preview JSON: {fullPreviewPath}\n\nSample rows (first 20 matching add/update/reject/warning):\n" + string.Join("\n", sampleLines));
            var message = $"GameDB-PS2 import preview\n\nRecords: {preview.Rows.Count:N0}\nAdd: {preview.AddCount:N0}\nUpdate: {preview.UpdateCount:N0}\nSkip: {preview.SkipCount:N0}\nReject: {preview.RejectCount:N0}\nRecords with warnings: {preview.WarningCount:N0}\n\nFull row-by-row preview saved to:\n{fullPreviewPath}\n\nNo catalogue changes have been made yet. Review warnings and source rights before continuing. Apply this import to the current local catalogue?";
            if (MessageBox.Show(this, message, "Import preview — v0.1 BETA", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                StatusText.Text = "Import cancelled; catalogue unchanged.";
                return;
            }
            await service.ApplyAsync(preview, _databasePath);
            await RefreshGamesAsync();
            StatusText.Text = $"Import completed. Added {preview.AddCount:N0}, updated {preview.UpdateCount:N0}, skipped {preview.SkipCount:N0}, rejected {preview.RejectCount:N0}.";
            MessageBox.Show(this, StatusText.Text, "Import complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Import failed; transaction rolled back where applicable.";
            MessageBox.Show(this, ex.Message, "Catalogue import failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => await RefreshGamesAsync();
    private async void RegionFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded) await RefreshGamesAsync(); }
    private async void ClearFilters_Click(object sender, RoutedEventArgs e) { SearchBox.Clear(); RegionFilter.SelectedIndex = 0; await RefreshGamesAsync(); }
    private void GameDetails_Click(object sender, RoutedEventArgs e)
    {
        if (GamesGrid.SelectedItem is not GameListItem selected) return;
        var details = new GameDetailsWindow(_databasePath, selected.GameId) { Owner = this };
        details.ShowDialog();
    }

    private void Dashboard_Click(object sender, RoutedEventArgs e) => SetPage("Dashboard", "A quick overview of your catalogue and personal collection.");
    private void Catalogue_Click(object sender, RoutedEventArgs e) => SetPage("Game Catalogue", "Browse the global PlayStation 2 catalogue and its regional releases.");
    private void Collection_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_databasePath)) return;
        new CollectionWindow(_databasePath) { Owner = this }.ShowDialog();
        _ = RefreshFooterAsync();
    }
    private async void ImportExport_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_databasePath)) return;
        var choice = MessageBox.Show(this,
            "Yes: create a complete SQLite backup of your catalogue and collection.\nNo: restore a backup (overwrites current data).\nCancel: do nothing.",
            "Backup / Restore", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (choice == MessageBoxResult.Cancel) return;
        try
        {
            if (choice == MessageBoxResult.Yes)
            {
                var save = new SaveFileDialog { Title = "Save database backup", Filter = "SQLite database (*.db)|*.db", FileName = $"PS2-Library-Backup-{DateTime.Now:yyyyMMdd-HHmmss}.db" };
                if (save.ShowDialog(this) != true) return;
                if (Path.GetFullPath(save.FileName).Equals(Path.GetFullPath(_databasePath), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Choose a different destination from the live database.");
                await using var source = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly");
                await using var destination = new SqliteConnection($"Data Source={save.FileName}");
                await source.OpenAsync();
                await destination.OpenAsync();
                source.BackupDatabase(destination);
                StatusText.Text = $"Backup saved: {save.FileName}";
            }
            else
            {
                var open = new OpenFileDialog { Title = "Select a PS2 Library database backup", Filter = "SQLite database (*.db)|*.db", CheckFileExists = true };
                if (open.ShowDialog(this) != true) return;
                if (Path.GetFullPath(open.FileName).Equals(Path.GetFullPath(_databasePath), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The selected file is already the active database.");
                await using (var check = new SqliteConnection($"Data Source={open.FileName};Mode=ReadOnly"))
                {
                    await check.OpenAsync();
                    await using var command = check.CreateCommand();
                    command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('Game','Release','CollectionCopy')";
                    if (Convert.ToInt32(await command.ExecuteScalarAsync()) != 3)
                        throw new InvalidDataException("Not a compatible PS2 Library database backup.");
                    command.CommandText = "PRAGMA quick_check";
                    if (!string.Equals((await command.ExecuteScalarAsync())?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("SQLite integrity check failed; restore cancelled.");
                }
                if (MessageBox.Show(this, "Restore will overwrite the current catalogue and personal collection. A safety backup of the current database will be created first. Continue?",
                    "Confirm restore", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                var safety = _databasePath + ".before-restore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".db";
                await using (var current = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly"))
                await using (var backup = new SqliteConnection($"Data Source={safety}"))
                {
                    await current.OpenAsync();
                    await backup.OpenAsync();
                    current.BackupDatabase(backup);
                }
                await using (var input = new SqliteConnection($"Data Source={open.FileName};Mode=ReadOnly"))
                await using (var live = new SqliteConnection($"Data Source={_databasePath}"))
                {
                    await input.OpenAsync();
                    await live.OpenAsync();
                    input.BackupDatabase(live);
                }
                await RefreshGamesAsync();
                StatusText.Text = $"Database restored. Previous database saved as {safety}";
            }
            MessageBox.Show(this, StatusText.Text, "Backup / Restore", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Backup / Restore failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private void Help_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "Catalog: browse and select a game.\nAdd to Collection: record a Physical, Digital or OPL copy.\nMy Collection: view and edit saved copies.\nImport / Export: back up or restore your SQLite database.",
            "PlayStation 2 Game Library — Help", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private void Settings_Click(object sender, RoutedEventArgs e) => SetPage("Settings", "Application and database settings.");
    private void AddGame_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_databasePath)) return;
        var selectedId = (GamesGrid.SelectedItem as GameListItem)?.GameId;
        new CollectionWindow(_databasePath, selectedId) { Owner = this }.ShowDialog();
        _ = RefreshFooterAsync();
    }
    private void GamesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GamesGrid.SelectedItem is GameListItem selected)
            { SelectedGameTitleText.Text = selected.Title; SelectedGameInfoText.Text = $"Year: {selected.Year}\nDeveloper: {selected.Developer}\nReleases: {selected.ReleaseCount}\nLanguages: {selected.Languages}"; StatusText.Text = $"Selected: {selected.Title}."; }
    }
    private void SetPage(string title, string subtitle) { PageTitle.Text = title; PageSubtitle.Text = subtitle; }

    private async Task RefreshFooterAsync()
    {
        if (string.IsNullOrWhiteSpace(_databasePath)) return;
        await using var db = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly");
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT CopyType,COUNT(*),COALESCE(SUM(DigitalSizeBytes),0) FROM CollectionCopy GROUP BY CopyType";
        long physical = 0, digital = 0, opl = 0, bytes = 0;
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var type = reader.GetString(0);
                var count = reader.GetInt64(1);
                bytes += reader.GetInt64(2);
                if (type == "Physical") physical = count;
                else if (type == "Digital") digital = count;
                else if (type == "OPL") opl = count;
            }
        }
        FooterPhysicalText.Text = physical.ToString("N0");
        FooterDigitalText.Text = digital.ToString("N0");
        FooterOplText.Text = opl.ToString("N0");
        FooterSizeText.Text = $"{bytes / 1_000_000_000.0:0.##} GB";
        cmd.CommandText = "SELECT COUNT(*) FROM Game";
        var total = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        FooterProgressText.Text = total > 0 ? $"{100.0 * (physical + digital + opl) / total:0.0}%" : "0%";
    }

    private sealed class GameListItem
    {
        public string GameId { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Year { get; init; } = string.Empty;
        public string Developer { get; init; } = string.Empty;
        public int ReleaseCount { get; init; }
        public string Languages { get; init; } = string.Empty;
    }
}