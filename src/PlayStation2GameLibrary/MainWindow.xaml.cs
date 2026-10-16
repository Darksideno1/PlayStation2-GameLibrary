using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
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
    private readonly ObservableCollection<CollectionListItem> _collectionRows = new();
    private readonly ObservableCollection<MissingCoverItem> _missingCoverRows = new();
    private string _databasePath = string.Empty;
    private string _collectionTypeFilter = "Physical";
    private AppSettings _settings = AppSettingsService.Load();
    private MediaLibraryService _mediaLibrary;

    public MainWindow()
    {
        InitializeComponent();
        _mediaLibrary = new MediaLibraryService(_settings);
        GamesGrid.ItemsSource = _games;
        CollectionGrid.ItemsSource = _collectionRows;
        DashboardMissingGrid.ItemsSource = _missingCoverRows;
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
            AppPaths.EnsurePortableFolders();
            var baseDir = AppContext.BaseDirectory;
            _databasePath = AppPaths.DatabasePath;

            await using var connection = new SqliteConnection($"Data Source={_databasePath}");
            await connection.OpenAsync();
            var schemaPath = Path.Combine(baseDir, "database", "schema.sql");
            if (File.Exists(schemaPath))
            {
                await using var command = connection.CreateCommand();
                command.CommandText = await File.ReadAllTextAsync(schemaPath);
                await command.ExecuteNonQueryAsync();
            }
            else
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE IF NOT EXISTS Game (GameId TEXT PRIMARY KEY, CanonicalTitle TEXT NOT NULL, SortTitle TEXT NOT NULL, FirstReleaseYear INTEGER NULL, Developer TEXT NULL);";
                await command.ExecuteNonQueryAsync();
            }

            await StarterCatalogue.EnsureSeededAsync(_databasePath, Path.Combine(baseDir, "data", "starter-catalog.json"));
            PortablePathText.Text = $"Data: {AppPaths.DataDirectory}";
            ImportExportDataPathText.Text = _databasePath;
            SettingsDataPathText.Text = $"Database: {_databasePath}";
            SettingsConfigPathText.Text = $"Settings: {Path.Combine(AppPaths.ConfigDirectory, "settings.json")}";
            LoadSettingsIntoPage();
            ShowHelpTopic("start");
            await RefreshGamesAsync();
            await RefreshFooterAsync();
            StatusText.Text = $"Catalogue ready. Portable database: {_databasePath}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Database initialization failed. See startup diagnostics.";
            MessageBox.Show(this, ex.Message, "Database initialization error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowPage(FrameworkElement page, string title, string subtitle, Button selectedButton)
    {
        foreach (var candidate in new FrameworkElement[] { CatalogPage, CollectionPage, DashboardPage, ImportExportPage, SettingsPage, HelpPage })
            candidate.Visibility = ReferenceEquals(candidate, page) ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in new[] { CatalogNavButton, CollectionNavButton, DashboardNavButton, ImportExportNavButton, SettingsNavButton, HelpNavButton })
        {
            button.Background = Brushes.Transparent;
            button.Foreground = Brush("#29466D");
        }
        selectedButton.Background = Brush("#DCEEFF");
        selectedButton.Foreground = Brush("#1264BD");
        PageTitle.Text = title;
        PageSubtitle.Text = subtitle;
    }

    private async Task RefreshGamesAsync()
    {
        if (string.IsNullOrWhiteSpace(_databasePath) || !File.Exists(_databasePath)) return;
        var search = SearchBox.Text.Trim();
        var region = (RegionFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All regions";
        var language = (LanguageFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
        var releaseType = (ReleaseTypeFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All release types";
        var results = new List<GameListItem>();

        await using var connection = new SqliteConnection($"Data Source={_databasePath};Mode=ReadWriteCreate");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT g.GameId, g.CanonicalTitle, g.FirstReleaseYear, g.Developer,
       COALESCE((SELECT d.SerialRaw FROM Release pr LEFT JOIN Disc d ON d.ReleaseId=pr.ReleaseId WHERE pr.GameId=g.GameId
                 ORDER BY pr.IsPrimary DESC,CASE WHEN pr.RegionCode IN ('NorthAmerica','USA','NTSC-U','NTSC-U/C') THEN 0 WHEN pr.RegionCode IN ('Europe','PAL','Europe/PAL','Australia') THEN 1 ELSE 2 END,d.DiscNumber LIMIT 1), '') AS PrimarySerial,
       COALESCE((SELECT pr.RegionCode FROM Release pr WHERE pr.GameId=g.GameId
                 ORDER BY pr.IsPrimary DESC,CASE WHEN pr.RegionCode IN ('NorthAmerica','USA','NTSC-U','NTSC-U/C') THEN 0 WHEN pr.RegionCode IN ('Europe','PAL','Europe/PAL','Australia') THEN 1 ELSE 2 END LIMIT 1), 'Unknown') AS PrimaryRegion,
       COALESCE((SELECT pr.ReleaseType FROM Release pr WHERE pr.GameId=g.GameId ORDER BY pr.IsPrimary DESC LIMIT 1), 'Retail') AS ReleaseType,
       COALESCE((SELECT group_concat(DISTINCT rl.LanguageCode) FROM Release r JOIN ReleaseLanguage rl ON rl.ReleaseId=r.ReleaseId WHERE r.GameId=g.GameId), '') AS Languages,
       (SELECT COUNT(*) FROM CollectionCopy c WHERE c.CopyType IN ('Physical','Digital') AND (c.GameId=g.GameId OR c.ReleaseId IN (SELECT rr.ReleaseId FROM Release rr WHERE rr.GameId=g.GameId))) AS OwnedCount
FROM Game g
WHERE ($search='' OR g.CanonicalTitle LIKE $pattern OR COALESCE(g.Developer,'') LIKE $pattern
   OR EXISTS (SELECT 1 FROM Release r WHERE r.GameId=g.GameId AND (COALESCE(r.Publisher,'') LIKE $pattern OR EXISTS (SELECT 1 FROM Disc d WHERE d.ReleaseId=r.ReleaseId AND COALESCE(d.SerialRaw,'') LIKE $pattern))))
AND ($region='All regions' OR EXISTS (SELECT 1 FROM Release r WHERE r.GameId=g.GameId AND (($region='PAL' AND r.RegionCode IN ('Europe','PAL','Europe/PAL','Australia')) OR ($region='NTSC-U/C' AND r.RegionCode IN ('NorthAmerica','USA','NTSC-U','NTSC-U/C')) OR ($region='NTSC-J' AND r.RegionCode IN ('Japan','NTSC-J')))))
AND ($language='' OR EXISTS (SELECT 1 FROM Release r JOIN ReleaseLanguage rl ON rl.ReleaseId=r.ReleaseId WHERE r.GameId=g.GameId AND (LOWER(rl.LanguageCode)=LOWER($language) OR LOWER(rl.LanguageCode)=LOWER($languageName))))
AND ($releaseType='All release types' OR EXISTS (SELECT 1 FROM Release r WHERE r.GameId=g.GameId AND r.ReleaseType=$releaseType))
ORDER BY g.SortTitle COLLATE NOCASE LIMIT 2000;";
        command.Parameters.AddWithValue("$search", search);
        command.Parameters.AddWithValue("$pattern", $"%{search}%");
        command.Parameters.AddWithValue("$region", region);
        command.Parameters.AddWithValue("$language", language);
        command.Parameters.AddWithValue("$languageName", LanguageName(language));
        command.Parameters.AddWithValue("$releaseType", releaseType);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var languageCodes = reader.IsDBNull(7) ? Array.Empty<string>() : reader.GetString(7).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            results.Add(new GameListItem
            {
                GameId = reader.GetString(0), Title = reader.GetString(1), Year = reader.IsDBNull(2) ? "—" : reader.GetInt32(2).ToString(),
                Developer = reader.IsDBNull(3) ? "—" : reader.GetString(3), PrimarySerial = reader.GetString(4), Region = FriendlyRegion(reader.GetString(5)),
                RegionFlag = FlagIconFactory.ForRegion(reader.GetString(5)), ReleaseType = reader.GetString(6),
                LanguageFlags = languageCodes.Select(FlagIconFactory.ForLanguage).Where(x => x is not null).Cast<ImageSource>().ToArray(), OwnedCount = reader.GetInt32(8)
            });
        }

        _games.Clear();
        foreach (var item in results) _games.Add(item);
        ResultCountText.Text = $"{results.Count:N0} games";
        DatabaseCountText.Text = $"Catalogue: {results.Count:N0} shown";
        FooterCatalogText.Text = $"{results.Count:N0} shown";
        if (results.Count > 0 && GamesGrid.SelectedItem is null) GamesGrid.SelectedIndex = 0;
        if (results.Count == 0) ClearSelectedGame();
    }

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => await RefreshGamesAsync();
    private async void RegionFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded) await RefreshGamesAsync(); }
    private async void LanguageFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded) await RefreshGamesAsync(); }
    private async void ReleaseTypeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded) await RefreshGamesAsync(); }

    private async void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear(); RegionFilter.SelectedIndex = 0; LanguageFilter.SelectedIndex = 0; ReleaseTypeFilter.SelectedIndex = 0;
        await RefreshGamesAsync();
    }

    private void GamesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GamesGrid.SelectedItem is GameListItem selected) UpdateSelectedGame(selected);
    }

    private void GamesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => GameDetails_Click(sender, e);

    private void GameDetails_Click(object sender, RoutedEventArgs e)
    {
        if (GamesGrid.SelectedItem is not GameListItem selected) return;
        new GameDetailsWindow(_databasePath, selected.GameId) { Owner = this }.ShowDialog();
    }

    private void AddGame_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_databasePath)) return;
        var selectedId = (GamesGrid.SelectedItem as GameListItem)?.GameId;
        new CollectionWindow(_databasePath, selectedId) { Owner = this }.ShowDialog();
        _ = RefreshFooterAsync(); _ = RefreshGamesAsync(); _ = RefreshCollectionPageAsync();
    }

    private void Catalogue_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(CatalogPage, "Catalog", "Browse and explore PlayStation 2 games.", CatalogNavButton);
        _ = RefreshGamesAsync();
    }

    private async void Collection_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(CollectionPage, "My Collection", "Your Physical and Digital PlayStation 2 collection.", CollectionNavButton);
        await RefreshCollectionPageAsync();
    }

    private async void Dashboard_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(DashboardPage, "Dashboard", "Overview of your library, digital storage and front cover coverage.", DashboardNavButton);
        await RefreshDashboardAsync();
    }

    private void ImportExport_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(ImportExportPage, "Import / Export", "Back up, restore and move your personal collection data.", ImportExportNavButton);
        ImportExportDataPathText.Text = _databasePath;
    }

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(HelpPage, "Help", "Quick guidance for using PLAYSTATION 2 GAME LIBRARY.", HelpNavButton);
        ShowHelpTopic("start");
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        LoadSettingsIntoPage();
        ShowPage(SettingsPage, "Settings", "Configure local media and portable storage.", SettingsNavButton);
    }

    private async Task RefreshCollectionPageAsync()
    {
        if (string.IsNullOrWhiteSpace(_databasePath)) return;
        var search = CollectionSearchBox.Text.Trim();
        var rows = new List<CollectionListItem>();
        await using var db = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly");
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = @"SELECT c.CopyId,c.GameId,g.CanonicalTitle,c.CopyType,c.DigitalSizeBytes,COALESCE(c.Notes,''),
COALESCE((SELECT d.SerialRaw FROM Release r LEFT JOIN Disc d ON d.ReleaseId=r.ReleaseId WHERE r.GameId=g.GameId ORDER BY r.IsPrimary DESC,d.DiscNumber LIMIT 1),''),
COALESCE((SELECT r.RegionCode FROM Release r WHERE r.GameId=g.GameId ORDER BY r.IsPrimary DESC LIMIT 1),'Unknown')
FROM CollectionCopy c JOIN Game g ON g.GameId=c.GameId
WHERE c.CopyType=$type AND ($search='' OR g.CanonicalTitle LIKE $pattern OR COALESCE(c.Notes,'') LIKE $pattern)
ORDER BY g.SortTitle COLLATE NOCASE";
        cmd.Parameters.AddWithValue("$type", _collectionTypeFilter);
        cmd.Parameters.AddWithValue("$search", search);
        cmd.Parameters.AddWithValue("$pattern", $"%{search}%");
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) rows.Add(new CollectionListItem(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetInt64(4), reader.GetString(5), reader.GetString(6), FriendlyRegion(reader.GetString(7))));
        _collectionRows.Clear(); foreach (var row in rows) _collectionRows.Add(row);
        CollectionSummaryText.Text = $"{rows.Count:N0} {_collectionTypeFilter.ToLowerInvariant()} entries";
        if (rows.Count > 0) CollectionGrid.SelectedIndex = 0; else ClearCollectionSelection();
        await RefreshFooterAsync();
    }

    private async void CollectionSearch_TextChanged(object sender, TextChangedEventArgs e) { if (IsLoaded) await RefreshCollectionPageAsync(); }

    private async void PhysicalCollection_Click(object sender, RoutedEventArgs e)
    {
        _collectionTypeFilter = "Physical"; PhysicalTabButton.Background = Brush("#2F82E5"); PhysicalTabButton.Foreground = Brushes.White; DigitalTabButton.Background = Brushes.White; DigitalTabButton.Foreground = Brush("#17314D"); await RefreshCollectionPageAsync();
    }

    private async void DigitalCollection_Click(object sender, RoutedEventArgs e)
    {
        _collectionTypeFilter = "Digital"; DigitalTabButton.Background = Brush("#2F82E5"); DigitalTabButton.Foreground = Brushes.White; PhysicalTabButton.Background = Brushes.White; PhysicalTabButton.Foreground = Brush("#17314D"); await RefreshCollectionPageAsync();
    }

    private void CollectionAdd_Click(object sender, RoutedEventArgs e)
    {
        new CollectionWindow(_databasePath) { Owner = this }.ShowDialog();
        _ = RefreshCollectionPageAsync(); _ = RefreshGamesAsync();
    }

    private void CollectionEdit_Click(object sender, RoutedEventArgs e)
    {
        new CollectionWindow(_databasePath, (CollectionGrid.SelectedItem as CollectionListItem)?.GameId) { Owner = this }.ShowDialog();
        _ = RefreshCollectionPageAsync(); _ = RefreshGamesAsync();
    }

    private async void CollectionGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CollectionGrid.SelectedItem is not CollectionListItem row) { ClearCollectionSelection(); return; }
        CollectionSelectedTitle.Text = row.Title;
        CollectionSelectedBadge.Text = $"{row.CopyType} copy  •  {row.Region}  •  {(string.IsNullOrWhiteSpace(row.PrimarySerial) ? "No serial" : row.PrimarySerial)}";
        CollectionSelectedDetails.Text = row.CopyType == "Digital" ? $"Digital size: {row.SizeDisplay}\nNotes: {(string.IsNullOrWhiteSpace(row.Notes) ? "—" : row.Notes)}" : $"Physical copy\nNotes: {(string.IsNullOrWhiteSpace(row.Notes) ? "—" : row.Notes)}";
        var coverPath = _mediaLibrary.FindCoverForSerial(row.PrimarySerial);
        var cover = _mediaLibrary.LoadImage(coverPath);
        CollectionCoverImage.Source = cover; CollectionCoverImage.Visibility = cover is null ? Visibility.Collapsed : Visibility.Visible; CollectionNoCover.Visibility = cover is null ? Visibility.Visible : Visibility.Collapsed;
        CollectionInventoryDetails.Text = await ReadCollectionDetailsAsync(row.CopyId, row.CopyType);
    }

    private void ClearCollectionSelection()
    {
        CollectionSelectedTitle.Text = "No copies found"; CollectionSelectedBadge.Text = "—"; CollectionSelectedDetails.Text = "No collection entries match this view."; CollectionInventoryDetails.Text = "—"; CollectionCoverImage.Source = null; CollectionCoverImage.Visibility = Visibility.Collapsed; CollectionNoCover.Visibility = Visibility.Visible;
    }

    private async Task<string> ReadCollectionDetailsAsync(string copyId, string copyType)
    {
        await using var db = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly"); await db.OpenAsync();
        await using var cmd = db.CreateCommand(); cmd.CommandText = @"SELECT Edition,CoverPresent,CasePresent,DiscPresent,ManualPresent,DigitalFormat,PurchaseDate,PurchasePrice,Seller FROM CollectionCopyDetails WHERE CopyId=$id"; cmd.Parameters.AddWithValue("$id", copyId);
        await using var reader = await cmd.ExecuteReaderAsync(); if (!await reader.ReadAsync()) return copyType == "Physical" ? "Completeness not recorded yet." : "Digital details not recorded yet.";
        string S(int i) => reader.IsDBNull(i) ? "" : reader.GetString(i);
        if (copyType == "Physical") return $"Original case: {S(2)}\nCover: {S(1)}\nDisc: {S(3)}\nManual: {S(4)}\nEdition: {(string.IsNullOrWhiteSpace(S(0)) ? "—" : S(0))}";
        return $"File format: {(string.IsNullOrWhiteSpace(S(5)) ? "—" : S(5))}\nPurchased: {(string.IsNullOrWhiteSpace(S(6)) ? "—" : S(6))}\nPrice: {(string.IsNullOrWhiteSpace(S(7)) ? "—" : S(7))}\nSource: {(string.IsNullOrWhiteSpace(S(8)) ? "—" : S(8))}";
    }

    private async Task RefreshDashboardAsync()
    {
        if (string.IsNullOrWhiteSpace(_databasePath)) return;
        var games = new List<MissingCoverItem>();
        await using var db = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly"); await db.OpenAsync();
        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT g.CanonicalTitle,COALESCE((SELECT d.SerialRaw FROM Release r LEFT JOIN Disc d ON d.ReleaseId=r.ReleaseId WHERE r.GameId=g.GameId ORDER BY r.IsPrimary DESC,d.DiscNumber LIMIT 1),''),COALESCE((SELECT r.RegionCode FROM Release r WHERE r.GameId=g.GameId ORDER BY r.IsPrimary DESC LIMIT 1),'Unknown') FROM Game g ORDER BY g.SortTitle COLLATE NOCASE";
            await using var reader = await cmd.ExecuteReaderAsync(); while (await reader.ReadAsync()) games.Add(new MissingCoverItem(reader.GetString(1), reader.GetString(0), FriendlyRegion(reader.GetString(2))));
        }
        var covered = games.Count(g => !string.IsNullOrWhiteSpace(g.Serial) && _mediaLibrary.FindCoverForSerial(g.Serial) is not null);
        var missing = games.Count - covered; var percent = games.Count == 0 ? 0 : covered * 100d / games.Count;
        DashboardWithCover.Text = covered.ToString("N0"); DashboardMissingCover.Text = missing.ToString("N0"); DashboardCoverage.Text = $"{percent:0.0}%"; DashboardCoverageBar.Value = percent; DashboardRingPercent.Text = $"{percent:0.0}%"; DashboardRingCaption.Text = $"{covered:N0} of {games.Count:N0} games";
        _missingCoverRows.Clear(); foreach (var row in games.Where(g => string.IsNullOrWhiteSpace(g.Serial) || _mediaLibrary.FindCoverForSerial(g.Serial) is null).Take(12)) _missingCoverRows.Add(row);
        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*),COALESCE(SUM(DigitalSizeBytes),0) FROM CollectionCopy WHERE CopyType='Digital'"; await using var reader = await cmd.ExecuteReaderAsync(); await reader.ReadAsync(); var count = reader.GetInt64(0); var bytes = reader.GetInt64(1); var size = FormatBytes(bytes);
            DashboardDigitalSize.Text = size; DashboardDigitalCount.Text = $"{count:N0} digital games"; DashboardDigitalPanelSize.Text = size; DashboardDigitalPanelCount.Text = $"{count:N0} digital games";
        }
    }

    private async void CreateBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "Save PS2 Library backup", Filter = "SQLite database (*.db)|*.db", FileName = $"PS2-Library-Backup-{DateTime.Now:yyyyMMdd-HHmmss}.db" }; if (dialog.ShowDialog(this) != true) return;
        try { await BackupDatabaseAsync(_databasePath, dialog.FileName); BackupStatusText.Text = $"Backup created: {dialog.FileName}"; BackupStatusText.Foreground = Brush("#168454"); }
        catch (Exception ex) { BackupStatusText.Text = ex.Message; BackupStatusText.Foreground = Brush("#C03943"); }
    }

    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Select PS2 Library backup", Filter = "SQLite database (*.db)|*.db", CheckFileExists = true }; if (dialog.ShowDialog(this) != true) return;
        try
        {
            await ValidateBackupAsync(dialog.FileName); if (MessageBox.Show(this, "Restore will replace the current portable database. A safety backup will be created first. Continue?", "Confirm restore", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            var safety = _databasePath + ".before-restore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".db"; await BackupDatabaseAsync(_databasePath, safety); await RestoreDatabaseAsync(dialog.FileName, _databasePath);
            RestoreStatusText.Text = $"Restore completed. Safety backup: {safety}"; RestoreStatusText.Foreground = Brush("#168454"); await RefreshGamesAsync(); await RefreshCollectionPageAsync(); await RefreshFooterAsync();
        }
        catch (Exception ex) { RestoreStatusText.Text = ex.Message; RestoreStatusText.Foreground = Brush("#C03943"); }
    }

    private async void ExportJson_Click(object sender, RoutedEventArgs e) => await ExportCollectionAsync(true);
    private async void ExportCsv_Click(object sender, RoutedEventArgs e) => await ExportCollectionAsync(false);

    private async Task ExportCollectionAsync(bool json)
    {
        try
        {
            var rows = await ReadCollectionExportAsync(); var dialog = new SaveFileDialog { Title = json ? "Export collection as JSON" : "Export collection as CSV", Filter = json ? "JSON file (*.json)|*.json" : "CSV file (*.csv)|*.csv", FileName = json ? "My_PS2_Collection.json" : "My_PS2_Collection.csv" }; if (dialog.ShowDialog(this) != true) return;
            if (json) await File.WriteAllTextAsync(dialog.FileName, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
            else { var b = new StringBuilder(); b.AppendLine("GameId,Title,CopyType,DigitalSizeBytes,Notes"); foreach (var r in rows) b.AppendLine(string.Join(',', Csv(r.GameId), Csv(r.Title), Csv(r.CopyType), r.DigitalSizeBytes?.ToString(CultureInfo.InvariantCulture) ?? "", Csv(r.Notes))); await File.WriteAllTextAsync(dialog.FileName, b.ToString(), new UTF8Encoding(true)); }
            ExportStatusText.Text = $"Exported {rows.Count:N0} entries to {dialog.FileName}"; ExportStatusText.Foreground = Brush("#168454");
        }
        catch (Exception ex) { ExportStatusText.Text = ex.Message; ExportStatusText.Foreground = Brush("#C03943"); }
    }

    private async void ImportCollection_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Select collection export", Filter = "Collection files (*.json;*.csv)|*.json;*.csv|JSON (*.json)|*.json|CSV (*.csv)|*.csv", CheckFileExists = true }; if (dialog.ShowDialog(this) != true) return;
        try
        {
            var rows = Path.GetExtension(dialog.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase) ? await ReadCsvAsync(dialog.FileName) : await ReadJsonAsync(dialog.FileName); var added = await ImportRowsAsync(rows); ImportStatusText.Text = $"Import completed: {added:N0} new entries added. Existing exact duplicates were skipped."; ImportStatusText.Foreground = Brush("#168454"); await RefreshCollectionPageAsync(); await RefreshGamesAsync(); await RefreshFooterAsync();
        }
        catch (Exception ex) { ImportStatusText.Text = ex.Message; ImportStatusText.Foreground = Brush("#C03943"); }
    }

    private void LoadSettingsIntoPage()
    {
        _settings = AppSettingsService.Load(); SettingsMediaPathBox.Text = _settings.MediaLibraryLocation; SettingsThumbnailCacheCheck.IsChecked = _settings.ThumbnailCacheEnabled; SettingsDataPathText.Text = $"Database: {AppPaths.DatabasePath}"; SettingsConfigPathText.Text = $"Settings: {Path.Combine(AppPaths.ConfigDirectory, "settings.json")}";
    }

    private void SettingsBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select Media Library root folder" }; var current = ResolveSettingsMediaRoot(); if (Directory.Exists(current)) dialog.InitialDirectory = current; if (dialog.ShowDialog(this) == true) SettingsMediaPathBox.Text = dialog.FolderName;
    }

    private string ResolveSettingsMediaRoot() => AppSettingsService.ResolveMediaRoot(new AppSettings { MediaLibraryLocation = SettingsMediaPathBox.Text.Trim(), ThumbnailCacheEnabled = SettingsThumbnailCacheCheck.IsChecked == true });

    private void SettingsTest_Click(object sender, RoutedEventArgs e)
    {
        try { var covers = Path.Combine(ResolveSettingsMediaRoot(), "Covers"); SettingsStatusText.Foreground = Directory.Exists(covers) ? Brush("#168454") : Brush("#B66D13"); SettingsStatusText.Text = Directory.Exists(covers) ? $"Covers folder is available: {covers}" : $"Covers folder does not exist yet: {covers}"; }
        catch (Exception ex) { SettingsStatusText.Foreground = Brush("#C03943"); SettingsStatusText.Text = ex.Message; }
    }

    private void SettingsOpenCovers_Click(object sender, RoutedEventArgs e)
    {
        try { var covers = Path.Combine(ResolveSettingsMediaRoot(), "Covers"); Directory.CreateDirectory(covers); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = covers, UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Open Covers folder failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void SettingsSave_Click(object sender, RoutedEventArgs e)
    {
        try { _settings = new AppSettings { MediaLibraryLocation = string.IsNullOrWhiteSpace(SettingsMediaPathBox.Text) ? @".\Media" : SettingsMediaPathBox.Text.Trim(), ThumbnailCacheEnabled = SettingsThumbnailCacheCheck.IsChecked == true }; AppSettingsService.Save(_settings); _mediaLibrary = new MediaLibraryService(_settings); SettingsStatusText.Foreground = Brush("#168454"); SettingsStatusText.Text = $"Settings saved. Media Library: {_mediaLibrary.MediaRoot}"; if (GamesGrid.SelectedItem is GameListItem selected) UpdateSelectedGame(selected); }
        catch (Exception ex) { SettingsStatusText.Foreground = Brush("#C03943"); SettingsStatusText.Text = ex.Message; }
    }

    private void HelpTopic_Click(object sender, RoutedEventArgs e) { if (sender is Button button && button.Tag is string topic) ShowHelpTopic(topic); }

    private void ShowHelpTopic(string topic)
    {
        (HelpTopicTitle.Text, HelpTopicBody.Text) = topic switch
        {
            "catalog" => ("Catalog", "The Catalog is the global PlayStation 2 game list. Use Region, Language and Release Type filters. Selecting a game shows its front cover, primary serial and collection status in the right panel. Cover availability is intentionally not a main catalog filter."),
            "collection" => ("My Collection", "My Collection is part of the main window. Physical and Digital entries are separated by tabs. Multiple physical copies are supported. Physical details can record Original case, Cover, Disc and Manual; Digital entries can store size and file information."),
            "covers" => ("Front Covers", "ONE GAME = ONE FRONT COVER. The program reads JPG, JPEG, PNG or WebP images from the configured Media\\Covers folder. The primary serial is used as the filename key. Missing images show a neutral placeholder and never prevent the catalog from working."),
            "backup" => ("Backup & Restore", "Import / Export is also part of the main window. Create Backup copies the active SQLite database. Restore validates the selected database and creates an automatic safety backup before replacing the active portable database. JSON and CSV are available for personal collection transfer."),
            "troubleshooting" => ("Troubleshooting", "If old test entries appeared in earlier builds, they came from %LOCALAPPDATA%\\PlayStation2GameLibrary. This portable build no longer uses that location by default. Its database is stored in the package Data folder and settings in Config. If covers are missing, check Settings and confirm the Covers folder is reachable."),
            "about" => ("About", "PLAYSTATION 2 GAME LIBRARY v0.1 BETA\nWindows / WPF / .NET 8\n\nUSER and ADMIN are separate executables but share the same portable Data, Config and Media folders when kept inside the same extracted package."),
            _ => ("Quick Start", "1. Open Settings and confirm the Media Library location.\n\n2. Browse the Catalog and select a game.\n\n3. Use + Add to Collection for a Physical or Digital copy.\n\n4. Open My Collection from the left menu; the main window remains in place.\n\n5. Use Import / Export to create backups before major changes.\n\nAll main USER sections now stay inside one application window. Add/Edit dialogs and file pickers remain modal where that is useful.")
        };
    }

    private async void ImportCatalogue_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Select the official GameDB-PS2 JSON file", Filter = "GameDB-PS2 JSON (*.json)|*.json|All files (*.*)|*.*", CheckFileExists = true, Multiselect = false }; if (dialog.ShowDialog(this) != true) return;
        try
        {
            StatusText.Text = "Reading source and preparing import preview…"; var service = new GameDbJsonImportService(); var preview = await service.PreviewAsync(dialog.FileName, _databasePath);
            var message = $"GameDB-PS2 import preview\n\nRecords: {preview.Rows.Count:N0}\nAdd: {preview.AddCount:N0}\nUpdate: {preview.UpdateCount:N0}\nSkip: {preview.SkipCount:N0}\nReject: {preview.RejectCount:N0}\nRecords with warnings: {preview.WarningCount:N0}\n\nApply this import?";
            if (MessageBox.Show(this, message, "Import preview — v0.1 BETA", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) { StatusText.Text = "Import cancelled; catalogue unchanged."; return; }
            await service.ApplyAsync(preview, _databasePath); await RefreshGamesAsync(); StatusText.Text = $"Import completed. Added {preview.AddCount:N0}, updated {preview.UpdateCount:N0}, skipped {preview.SkipCount:N0}, rejected {preview.RejectCount:N0}.";
        }
        catch (Exception ex) { StatusText.Text = "Import failed; transaction rolled back where applicable."; MessageBox.Show(this, ex.Message, "Catalogue import failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void UpdateSelectedGame(GameListItem selected)
    {
        SelectedGameTitleText.Text = selected.Title; SelectedGamePrimarySerialText.Text = string.IsNullOrWhiteSpace(selected.PrimarySerial) ? "—" : selected.PrimarySerial; SelectedGameRegionText.Text = selected.Region; SelectedRegionFlag.Source = selected.RegionFlag; SelectedGameReleaseTypeText.Text = selected.ReleaseType; SelectedGameOwnedText.Text = selected.Owned; SelectedGameInfoText.Text = $"Year: {selected.Year}\nDeveloper: {selected.Developer}";
        SelectedLanguageFlagsPanel.Children.Clear(); foreach (var source in selected.LanguageFlags) SelectedLanguageFlagsPanel.Children.Add(new Image { Source = source, Width = 26, Height = 17, Stretch = Stretch.Fill, Margin = new Thickness(0, 0, 6, 0) }); if (selected.LanguageFlags.Count == 0) SelectedLanguageFlagsPanel.Children.Add(new TextBlock { Text = "—", Foreground = Brushes.SlateGray });
        var coverPath = _mediaLibrary.FindCoverForSerial(selected.PrimarySerial); var cover = _mediaLibrary.LoadImage(coverPath); SelectedGameCoverImage.Source = cover; SelectedGameCoverImage.Visibility = cover is null ? Visibility.Collapsed : Visibility.Visible; NoCoverPlaceholder.Visibility = cover is null ? Visibility.Visible : Visibility.Collapsed; StatusText.Text = cover is null ? $"Selected: {selected.Title}. No local front cover found for {selected.PrimarySerial}." : $"Selected: {selected.Title}. Cover: {Path.GetFileName(coverPath)}";
    }

    private void ClearSelectedGame()
    {
        SelectedGameTitleText.Text = "No games found"; SelectedGamePrimarySerialText.Text = "—"; SelectedGameRegionText.Text = "—"; SelectedGameReleaseTypeText.Text = "—"; SelectedGameOwnedText.Text = "—"; SelectedGameInfoText.Text = "No games match the current search and filters."; SelectedRegionFlag.Source = null; SelectedLanguageFlagsPanel.Children.Clear(); SelectedGameCoverImage.Source = null; SelectedGameCoverImage.Visibility = Visibility.Collapsed; NoCoverPlaceholder.Visibility = Visibility.Visible;
    }

    private async Task RefreshFooterAsync()
    {
        if (string.IsNullOrWhiteSpace(_databasePath)) return;
        await using var db = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly"); await db.OpenAsync(); await using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT CopyType,COUNT(*),COALESCE(SUM(DigitalSizeBytes),0) FROM CollectionCopy WHERE CopyType IN ('Physical','Digital') GROUP BY CopyType"; long physical = 0, digital = 0, bytes = 0;
        await using (var reader = await cmd.ExecuteReaderAsync()) { while (await reader.ReadAsync()) { var type = reader.GetString(0); var count = reader.GetInt64(1); bytes += reader.GetInt64(2); if (type == "Physical") physical = count; else if (type == "Digital") digital = count; } }
        FooterPhysicalText.Text = physical.ToString("N0"); FooterDigitalText.Text = digital.ToString("N0"); FooterSizeText.Text = FormatBytes(bytes); cmd.CommandText = "SELECT COUNT(*) FROM Game"; var total = Convert.ToInt64(await cmd.ExecuteScalarAsync()); FooterProgressText.Text = total > 0 ? $"{100.0 * (physical + digital) / total:0.0}%" : "0%";
    }

    private async Task<List<CollectionExportEntry>> ReadCollectionExportAsync()
    {
        var rows = new List<CollectionExportEntry>(); await using var db = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly"); await db.OpenAsync(); await using var cmd = db.CreateCommand(); cmd.CommandText = @"SELECT c.GameId,g.CanonicalTitle,c.CopyType,c.DigitalSizeBytes,COALESCE(c.Notes,'') FROM CollectionCopy c JOIN Game g ON g.GameId=c.GameId WHERE c.CopyType IN ('Physical','Digital') ORDER BY g.SortTitle,c.CopyType"; await using var reader = await cmd.ExecuteReaderAsync(); while (await reader.ReadAsync()) rows.Add(new CollectionExportEntry(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetInt64(3), reader.GetString(4))); return rows;
    }

    private static async Task<List<CollectionExportEntry>> ReadJsonAsync(string path)
    {
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path)); var root = doc.RootElement; if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Entries", out var entries)) root = entries; if (root.ValueKind != JsonValueKind.Array) throw new InvalidDataException("JSON must contain an array of collection entries."); var rows = new List<CollectionExportEntry>();
        foreach (var item in root.EnumerateArray()) { string S(string name) => item.TryGetProperty(name, out var v) ? v.GetString() ?? "" : ""; long? size = item.TryGetProperty("DigitalSizeBytes", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt64(out var n) ? n : null; rows.Add(new CollectionExportEntry(S("GameId"), S("Title"), S("CopyType"), size, S("Notes"))); } return rows;
    }

    private static async Task<List<CollectionExportEntry>> ReadCsvAsync(string path)
    {
        var lines = await File.ReadAllLinesAsync(path); if (lines.Length < 2) return new(); var rows = new List<CollectionExportEntry>(); foreach (var line in lines.Skip(1).Where(x => !string.IsNullOrWhiteSpace(x))) { var v = ParseCsv(line); if (v.Count < 5) continue; long? size = long.TryParse(v[3], out var n) ? n : null; rows.Add(new CollectionExportEntry(v[0], v[1], v[2], size, v[4])); } return rows;
    }

    private async Task<int> ImportRowsAsync(IEnumerable<CollectionExportEntry> rows)
    {
        var added = 0; await using var db = new SqliteConnection($"Data Source={_databasePath}"); await db.OpenAsync(); await using var tx = await db.BeginTransactionAsync();
        foreach (var source in rows)
        {
            var type = source.CopyType.Equals("Physical", StringComparison.OrdinalIgnoreCase) ? "Physical" : source.CopyType.Equals("Digital", StringComparison.OrdinalIgnoreCase) ? "Digital" : string.Empty; if (type.Length == 0) continue; var gameId = source.GameId;
            await using (var resolve = db.CreateCommand()) { resolve.Transaction = (SqliteTransaction)tx; resolve.CommandText = string.IsNullOrWhiteSpace(gameId) ? "SELECT GameId FROM Game WHERE CanonicalTitle=$title COLLATE NOCASE LIMIT 1" : "SELECT GameId FROM Game WHERE GameId=$id LIMIT 1"; if (string.IsNullOrWhiteSpace(gameId)) resolve.Parameters.AddWithValue("$title", source.Title); else resolve.Parameters.AddWithValue("$id", gameId); gameId = (await resolve.ExecuteScalarAsync())?.ToString() ?? string.Empty; }
            if (gameId.Length == 0) continue; await using var dup = db.CreateCommand(); dup.Transaction = (SqliteTransaction)tx; dup.CommandText = "SELECT COUNT(*) FROM CollectionCopy WHERE GameId=$g AND CopyType=$t AND COALESCE(DigitalSizeBytes,-1)=COALESCE($s,-1) AND COALESCE(Notes,'')=$n"; dup.Parameters.AddWithValue("$g", gameId); dup.Parameters.AddWithValue("$t", type); dup.Parameters.AddWithValue("$s", (object?)source.DigitalSizeBytes ?? DBNull.Value); dup.Parameters.AddWithValue("$n", source.Notes ?? string.Empty); if (Convert.ToInt64(await dup.ExecuteScalarAsync()) > 0) continue;
            await using var insert = db.CreateCommand(); insert.Transaction = (SqliteTransaction)tx; insert.CommandText = @"INSERT INTO CollectionCopy(CopyId,GameId,CopyType,DigitalSizeBytes,Notes,CreatedUtc,UpdatedUtc) VALUES($id,$g,$t,$s,$n,$now,$now)"; insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D")); insert.Parameters.AddWithValue("$g", gameId); insert.Parameters.AddWithValue("$t", type); insert.Parameters.AddWithValue("$s", (object?)source.DigitalSizeBytes ?? DBNull.Value); insert.Parameters.AddWithValue("$n", source.Notes ?? string.Empty); insert.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O")); added += await insert.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync(); return added;
    }

    private static async Task ValidateBackupAsync(string path)
    {
        await using var db = new SqliteConnection($"Data Source={path};Mode=ReadOnly"); await db.OpenAsync(); await using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('Game','Release','CollectionCopy')"; if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) != 3) throw new InvalidDataException("This is not a compatible PS2 Library backup."); cmd.CommandText = "PRAGMA quick_check"; if (!string.Equals((await cmd.ExecuteScalarAsync())?.ToString(), "ok", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("SQLite integrity check failed.");
    }

    private static async Task BackupDatabaseAsync(string sourcePath, string destinationPath)
    {
        if (Path.GetFullPath(sourcePath).Equals(Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Choose a destination different from the active database."); if (File.Exists(destinationPath)) File.Delete(destinationPath); await using var source = new SqliteConnection($"Data Source={sourcePath};Mode=ReadOnly"); await using var destination = new SqliteConnection($"Data Source={destinationPath}"); await source.OpenAsync(); await destination.OpenAsync(); source.BackupDatabase(destination);
    }

    private static async Task RestoreDatabaseAsync(string sourcePath, string livePath)
    {
        await using var source = new SqliteConnection($"Data Source={sourcePath};Mode=ReadOnly"); await using var live = new SqliteConnection($"Data Source={livePath}"); await source.OpenAsync(); await live.OpenAsync(); source.BackupDatabase(live);
    }

    private static string Csv(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
    private static IReadOnlyList<string> ParseCsv(string line) { var result = new List<string>(); var b = new StringBuilder(); var quoted = false; for (var i = 0; i < line.Length; i++) { var ch = line[i]; if (ch == '"') { if (quoted && i + 1 < line.Length && line[i + 1] == '"') { b.Append('"'); i++; } else quoted = !quoted; } else if (ch == ',' && !quoted) { result.Add(b.ToString()); b.Clear(); } else b.Append(ch); } result.Add(b.ToString()); return result; }

    private static string LanguageName(string code) => code.ToLowerInvariant() switch { "en" => "English", "fr" => "French", "de" => "German", "it" => "Italian", "es" => "Spanish", "ja" => "Japanese", _ => code };
    private static string FriendlyRegion(string region) { var value = region.ToLowerInvariant(); if (value.Contains("north") || value.Contains("usa") || value.Contains("ntsc-u")) return "NTSC-U/C"; if (value.Contains("japan") || value.Contains("ntsc-j")) return "NTSC-J"; if (value.Contains("europe") || value.Contains("pal") || value.Contains("australia")) return "PAL"; return region; }
    private static string FormatBytes(long bytes) => bytes >= 1_000_000_000_000L ? $"{bytes / 1_000_000_000_000.0:0.##} TB" : $"{bytes / 1_000_000_000.0:0.##} GB";
    private static Brush Brush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;

    private sealed class GameListItem
    {
        public string GameId { get; init; } = string.Empty; public string Title { get; init; } = string.Empty; public string Year { get; init; } = string.Empty; public string Developer { get; init; } = string.Empty; public string PrimarySerial { get; init; } = string.Empty; public string Region { get; init; } = string.Empty; public ImageSource? RegionFlag { get; init; } public IReadOnlyList<ImageSource> LanguageFlags { get; init; } = Array.Empty<ImageSource>(); public string ReleaseType { get; init; } = string.Empty; public int OwnedCount { get; init; } public string Owned => OwnedCount > 0 ? $"{OwnedCount} owned" : "—";
    }

    private sealed record CollectionListItem(string CopyId, string GameId, string Title, string CopyType, long? SizeBytes, string Notes, string PrimarySerial, string Region)
    {
        public string SizeDisplay => SizeBytes is long bytes ? FormatBytes(bytes) : "—";
    }
    private sealed record MissingCoverItem(string Serial, string Title, string Region);
    private sealed record CollectionExportEntry(string GameId, string Title, string CopyType, long? DigitalSizeBytes, string Notes);
}
