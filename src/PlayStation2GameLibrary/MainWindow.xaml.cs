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
        Loaded += async (_, _) => await InitializeDatabaseAsync();
    }

    private async Task InitializeDatabaseAsync()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var dataDir = Path.Combine(baseDir, "Data");
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
            await RefreshGamesAsync();
            StatusText.Text = $"Database ready: {_databasePath}. Source import is the next implementation step.";
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
    private void Dashboard_Click(object sender, RoutedEventArgs e) => SetPage("Dashboard", "A quick overview of your catalogue and personal collection.");
    private void Catalogue_Click(object sender, RoutedEventArgs e) => SetPage("Game Catalogue", "Browse the global PlayStation 2 catalogue and its regional releases.");
    private void Collection_Click(object sender, RoutedEventArgs e) => SetPage("My Collection", "Collection editing will be enabled after the catalogue foundation is verified.");
    private void ImportExport_Click(object sender, RoutedEventArgs e) => SetPage("Import / Export", "Safe source import, backup and restore workflows are planned for the next phase.");
    private void Settings_Click(object sender, RoutedEventArgs e) => SetPage("Settings", "Application and database settings.");
    private void AddGame_Click(object sender, RoutedEventArgs e) => MessageBox.Show(this, "Manual game/release creation is not implemented in this starter build.", "v0.1 BETA", MessageBoxButton.OK, MessageBoxImage.Information);
    private void GamesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GamesGrid.SelectedItem is GameListItem selected)
            StatusText.Text = $"Selected: {selected.Title}. Detailed game and release editor is a Phase 1 follow-up.";
    }
    private void SetPage(string title, string subtitle) { PageTitle.Text = title; PageSubtitle.Text = subtitle; }

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