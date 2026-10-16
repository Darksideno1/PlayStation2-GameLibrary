using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace PlayStation2GameLibrary;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<GameListItem> _games = new();
    private string _databasePath = string.Empty;
    private AppSettings _settings = AppSettingsService.Load();
    private MediaLibraryService _mediaLibrary;

    public MainWindow()
    {
        InitializeComponent();
        _mediaLibrary = new MediaLibraryService(_settings);
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
            var dataDir = Environment.GetEnvironmentVariable("PS2_LIBRARY_DATA_DIR")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlayStation2GameLibrary", "Data");
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

            await StarterCatalogue.EnsureSeededAsync(_databasePath, Path.Combine(baseDir, "data", "starter-catalog.json"));
            await RefreshGamesAsync();
            await RefreshFooterAsync();
            StatusText.Text = $"Catalogue ready. Media Library: {_mediaLibrary.MediaRoot}";
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
        var language = (LanguageFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
        var languageName = LanguageName(language);
        var releaseType = (ReleaseTypeFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All release types";
        var results = new List<GameListItem>();

        await using var connection = new SqliteConnection($"Data Source={_databasePath};Mode=ReadWriteCreate");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT g.GameId, g.CanonicalTitle, g.FirstReleaseYear, g.Developer,
       COALESCE((SELECT d.SerialRaw
                 FROM Release pr LEFT JOIN Disc d ON d.ReleaseId=pr.ReleaseId
                 WHERE pr.GameId=g.GameId
                 ORDER BY pr.IsPrimary DESC,
                          CASE WHEN pr.RegionCode IN ('NorthAmerica','USA','NTSC-U','NTSC-U/C') THEN 0
                               WHEN pr.RegionCode IN ('Europe','PAL','Europe/PAL','Australia') THEN 1 ELSE 2 END,
                          d.DiscNumber
                 LIMIT 1), '') AS PrimarySerial,
       COALESCE((SELECT pr.RegionCode FROM Release pr WHERE pr.GameId=g.GameId
                 ORDER BY pr.IsPrimary DESC,
                          CASE WHEN pr.RegionCode IN ('NorthAmerica','USA','NTSC-U','NTSC-U/C') THEN 0
                               WHEN pr.RegionCode IN ('Europe','PAL','Europe/PAL','Australia') THEN 1 ELSE 2 END
                 LIMIT 1), 'Unknown') AS PrimaryRegion,
       COALESCE((SELECT pr.ReleaseType FROM Release pr WHERE pr.GameId=g.GameId
                 ORDER BY pr.IsPrimary DESC LIMIT 1), 'Retail') AS ReleaseType,
       COALESCE((SELECT group_concat(DISTINCT rl.LanguageCode)
                 FROM Release r JOIN ReleaseLanguage rl ON rl.ReleaseId=r.ReleaseId
                 WHERE r.GameId=g.GameId), '') AS Languages,
       (SELECT COUNT(*) FROM CollectionCopy c
         WHERE c.CopyType IN ('Physical','Digital')
           AND (c.GameId=g.GameId OR c.ReleaseId IN (SELECT rr.ReleaseId FROM Release rr WHERE rr.GameId=g.GameId))) AS OwnedCount
FROM Game g
WHERE ($search='' OR g.CanonicalTitle LIKE $pattern OR COALESCE(g.Developer,'') LIKE $pattern
   OR EXISTS (SELECT 1 FROM Release r WHERE r.GameId=g.GameId AND (COALESCE(r.Publisher,'') LIKE $pattern
      OR EXISTS (SELECT 1 FROM Disc d WHERE d.ReleaseId=r.ReleaseId AND COALESCE(d.SerialRaw,'') LIKE $pattern))))
AND ($region='All regions' OR EXISTS (SELECT 1 FROM Release r WHERE r.GameId=g.GameId AND
   (($region='PAL' AND r.RegionCode IN ('Europe','PAL','Europe/PAL','Australia')) OR
    ($region='NTSC-U/C' AND r.RegionCode IN ('NorthAmerica','USA','NTSC-U','NTSC-U/C')) OR
    ($region='NTSC-J' AND r.RegionCode IN ('Japan','NTSC-J')))))
AND ($language='' OR EXISTS (SELECT 1 FROM Release r JOIN ReleaseLanguage rl ON rl.ReleaseId=r.ReleaseId
                             WHERE r.GameId=g.GameId AND (LOWER(rl.LanguageCode)=LOWER($language) OR LOWER(rl.LanguageCode)=LOWER($languageName))))
AND ($releaseType='All release types' OR EXISTS (SELECT 1 FROM Release r WHERE r.GameId=g.GameId AND r.ReleaseType=$releaseType))
ORDER BY g.SortTitle COLLATE NOCASE LIMIT 2000;";
        command.Parameters.AddWithValue("$search", search);
        command.Parameters.AddWithValue("$pattern", $"%{search}%");
        command.Parameters.AddWithValue("$region", region);
        command.Parameters.AddWithValue("$language", language);
        command.Parameters.AddWithValue("$languageName", languageName);
        command.Parameters.AddWithValue("$releaseType", releaseType);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var languageCodes = reader.IsDBNull(7) ? Array.Empty<string>() : reader.GetString(7).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            results.Add(new GameListItem
            {
                GameId = reader.GetString(0),
                Title = reader.GetString(1),
                Year = reader.IsDBNull(2) ? "—" : reader.GetInt32(2).ToString(),
                Developer = reader.IsDBNull(3) ? "—" : reader.GetString(3),
                PrimarySerial = reader.GetString(4),
                Region = FriendlyRegion(reader.GetString(5)),
                RegionFlag = FlagIconFactory.ForRegion(reader.GetString(5)),
                ReleaseType = reader.GetString(6),
                Languages = string.Join(", ", languageCodes),
                LanguageFlags = languageCodes.Select(FlagIconFactory.ForLanguage).Where(x => x is not null).Cast<ImageSource>().ToArray(),
                OwnedCount = reader.GetInt32(8)
            });
        }

        _games.Clear();
        foreach (var item in results) _games.Add(item);
        ResultCountText.Text = $"{results.Count:N0} games";
        DatabaseCountText.Text = $"Catalogue: {results.Count:N0} shown";
        FooterCatalogText.Text = $"{results.Count:N0} shown";
        if (results.Count > 0 && GamesGrid.SelectedItem is null) GamesGrid.SelectedIndex = 0;
    }

    private static string LanguageName(string code) => code.ToLowerInvariant() switch
    {
        "en" => "English", "fr" => "French", "de" => "German", "it" => "Italian",
        "es" => "Spanish", "ja" => "Japanese", _ => code
    };

    private static string FriendlyRegion(string region)
    {
        var value = region.ToLowerInvariant();
        if (value.Contains("north") || value.Contains("usa") || value.Contains("ntsc-u")) return "NTSC-U/C";
        if (value.Contains("japan") || value.Contains("ntsc-j")) return "NTSC-J";
        if (value.Contains("europe") || value.Contains("pal") || value.Contains("australia")) return "PAL";
        return region;
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
            await File.WriteAllTextAsync(previewPath, $"Source: {dialog.FileName}\nSHA-256: {preview.Sha256}\nRecords: {preview.Rows.Count}\nAdd: {preview.AddCount}\nUpdate: {preview.UpdateCount}\nSkip: {preview.SkipCount}\nReject: {preview.RejectCount}\nRows with warnings: {preview.WarningCount}\nFull row-by-row preview JSON: {fullPreviewPath}\n\nSample rows:\n" + string.Join("\n", sampleLines));
            var message = $"GameDB-PS2 import preview\n\nRecords: {preview.Rows.Count:N0}\nAdd: {preview.AddCount:N0}\nUpdate: {preview.UpdateCount:N0}\nSkip: {preview.SkipCount:N0}\nReject: {preview.RejectCount:N0}\nRecords with warnings: {preview.WarningCount:N0}\n\nFull preview:\n{fullPreviewPath}\n\nApply this import?";
            if (MessageBox.Show(this, message, "Import preview — v0.1 BETA", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                StatusText.Text = "Import cancelled; catalogue unchanged.";
                return;
            }
            await service.ApplyAsync(preview, _databasePath);
            await RefreshGamesAsync();
            StatusText.Text = $"Import completed. Added {preview.AddCount:N0}, updated {preview.UpdateCount:N0}, skipped {preview.SkipCount:N0}, rejected {preview.RejectCount:N0}.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Import failed; transaction rolled back where applicable.";
            MessageBox.Show(this, ex.Message, "Catalogue import failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => await RefreshGamesAsync();
    private async void RegionFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded) await RefreshGamesAsync(); }
    private async void LanguageFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded) await RefreshGamesAsync(); }
    private async void ReleaseTypeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded) await RefreshGamesAsync(); }
    private async void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        RegionFilter.SelectedIndex = 0;
        LanguageFilter.SelectedIndex = 0;
        ReleaseTypeFilter.SelectedIndex = 0;
        await RefreshGamesAsync();
    }

    private void GameDetails_Click(object sender, RoutedEventArgs e)
    {
        if (GamesGrid.SelectedItem is not GameListItem selected) return;
        new GameDetailsWindow(_databasePath, selected.GameId) { Owner = this }.ShowDialog();
    }

    private void GamesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => GameDetails_Click(sender, e);
    private void Dashboard_Click(object sender, RoutedEventArgs e) => SetPage("Dashboard", "Dashboard implementation is the next WPF migration step.");
    private void Catalogue_Click(object sender, RoutedEventArgs e) => SetPage("Catalog", "Browse and explore PlayStation 2 games.");

    private void Collection_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_databasePath)) return;
        new CollectionWindow(_databasePath) { Owner = this }.ShowDialog();
        _ = RefreshFooterAsync();
        _ = RefreshGamesAsync();
    }

    private async void ImportExport_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_databasePath)) return;
        var choice = MessageBox.Show(this,
            "Yes: create a complete SQLite backup.\nNo: restore a backup (overwrites current data after confirmation).\nCancel: do nothing.",
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
                    if (Convert.ToInt32(await command.ExecuteScalarAsync()) != 3) throw new InvalidDataException("Not a compatible PS2 Library backup.");
                    command.CommandText = "PRAGMA quick_check";
                    if (!string.Equals((await command.ExecuteScalarAsync())?.ToString(), "ok", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("SQLite integrity check failed; restore cancelled.");
                }
                if (MessageBox.Show(this, "Restore will overwrite the current catalogue and personal collection. A safety backup will be created first. Continue?", "Confirm restore", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
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
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Backup / Restore failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "Catalog: browse games and regional releases.\nAdd to Collection: record a Physical or Digital copy.\nSettings: choose the local Media Library folder used for front covers.\nImport / Export: back up or restore the SQLite database.",
            "PlayStation 2 Game Library — Help", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow { Owner = this };
        if (window.ShowDialog() == true)
        {
            _settings = AppSettingsService.Load();
            _mediaLibrary = new MediaLibraryService(_settings);
            if (GamesGrid.SelectedItem is GameListItem selected) UpdateSelectedGame(selected);
            StatusText.Text = $"Media Library updated: {_mediaLibrary.MediaRoot}";
        }
    }

    private void AddGame_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_databasePath)) return;
        var selectedId = (GamesGrid.SelectedItem as GameListItem)?.GameId;
        new CollectionWindow(_databasePath, selectedId) { Owner = this }.ShowDialog();
        _ = RefreshFooterAsync();
        _ = RefreshGamesAsync();
    }

    private void GamesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GamesGrid.SelectedItem is GameListItem selected) UpdateSelectedGame(selected);
    }

    private void UpdateSelectedGame(GameListItem selected)
    {
        SelectedGameTitleText.Text = selected.Title;
        SelectedGamePrimarySerialText.Text = string.IsNullOrWhiteSpace(selected.PrimarySerial) ? "—" : selected.PrimarySerial;
        SelectedGameRegionText.Text = selected.Region;
        SelectedRegionFlag.Source = selected.RegionFlag;
        SelectedGameReleaseTypeText.Text = selected.ReleaseType;
        SelectedGameOwnedText.Text = selected.Owned;
        SelectedGameInfoText.Text = $"Year: {selected.Year}\nDeveloper: {selected.Developer}";
        SelectedLanguageFlagsPanel.Children.Clear();
        foreach (var source in selected.LanguageFlags)
        {
            SelectedLanguageFlagsPanel.Children.Add(new System.Windows.Controls.Image { Source = source, Width = 26, Height = 17, Stretch = Stretch.Fill, Margin = new Thickness(0, 0, 6, 0) });
        }
        if (selected.LanguageFlags.Count == 0)
            SelectedLanguageFlagsPanel.Children.Add(new TextBlock { Text = "—", Foreground = Brushes.SlateGray });

        var coverPath = _mediaLibrary.FindCoverForSerial(selected.PrimarySerial);
        var cover = _mediaLibrary.LoadImage(coverPath);
        SelectedGameCoverImage.Source = cover;
        SelectedGameCoverImage.Visibility = cover is null ? Visibility.Collapsed : Visibility.Visible;
        NoCoverPlaceholder.Visibility = cover is null ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = cover is null
            ? $"Selected: {selected.Title}. No local front cover found for {selected.PrimarySerial}."
            : $"Selected: {selected.Title}. Cover: {Path.GetFileName(coverPath)}";
    }

    private void SetPage(string title, string subtitle)
    {
        PageTitle.Text = title;
        PageSubtitle.Text = subtitle;
    }

    private async Task RefreshFooterAsync()
    {
        if (string.IsNullOrWhiteSpace(_databasePath)) return;
        await using var db = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly");
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT CopyType,COUNT(*),COALESCE(SUM(DigitalSizeBytes),0) FROM CollectionCopy WHERE CopyType IN ('Physical','Digital') GROUP BY CopyType";
        long physical = 0, digital = 0, bytes = 0;
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var type = reader.GetString(0);
                var count = reader.GetInt64(1);
                bytes += reader.GetInt64(2);
                if (type == "Physical") physical = count;
                else if (type == "Digital") digital = count;
            }
        }
        FooterPhysicalText.Text = physical.ToString("N0");
        FooterDigitalText.Text = digital.ToString("N0");
        FooterSizeText.Text = FormatBytes(bytes);
        cmd.CommandText = "SELECT COUNT(*) FROM Game";
        var total = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        FooterProgressText.Text = total > 0 ? $"{100.0 * (physical + digital) / total:0.0}%" : "0%";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_000_000_000_000L) return $"{bytes / 1_000_000_000_000.0:0.##} TB";
        return $"{bytes / 1_000_000_000.0:0.##} GB";
    }

    private sealed class GameListItem
    {
        public string GameId { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Year { get; init; } = string.Empty;
        public string Developer { get; init; } = string.Empty;
        public string PrimarySerial { get; init; } = string.Empty;
        public string Region { get; init; } = string.Empty;
        public ImageSource? RegionFlag { get; init; }
        public string Languages { get; init; } = string.Empty;
        public IReadOnlyList<ImageSource> LanguageFlags { get; init; } = Array.Empty<ImageSource>();
        public string ReleaseType { get; init; } = string.Empty;
        public int OwnedCount { get; init; }
        public string Owned => OwnedCount > 0 ? $"{OwnedCount} owned" : "—";
    }
}
