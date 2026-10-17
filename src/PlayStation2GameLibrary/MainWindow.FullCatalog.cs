using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;

namespace PlayStation2GameLibrary;

internal static class FullCatalogBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded), true);
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window)) return;
        window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(window.EnableFullCatalogRefresh));
    }
}

public partial class MainWindow
{
    private bool _fullCatalogRefreshEnabled;
    private bool _fullCatalogRefreshScheduled;

    internal void EnableFullCatalogRefresh()
    {
        if (_fullCatalogRefreshEnabled) return;
        _fullCatalogRefreshEnabled = true;

        SearchBox.TextChanged -= SearchBox_TextChanged;
        RegionFilter.SelectionChanged -= RegionFilter_SelectionChanged;
        LanguageFilter.SelectionChanged -= LanguageFilter_SelectionChanged;
        ReleaseTypeFilter.SelectionChanged -= ReleaseTypeFilter_SelectionChanged;
        CatalogNavButton.Click -= Catalogue_Click;

        SearchBox.TextChanged += FullCatalogFilterChanged;
        RegionFilter.SelectionChanged += FullCatalogFilterChanged;
        LanguageFilter.SelectionChanged += FullCatalogFilterChanged;
        ReleaseTypeFilter.SelectionChanged += FullCatalogFilterChanged;
        CatalogNavButton.Click += FullCatalogNavClicked;

        foreach (var button in FindVisualChildren<Button>(CatalogPage))
        {
            if (string.Equals(button.Content?.ToString(), "Clear", StringComparison.OrdinalIgnoreCase))
            {
                button.Click -= ClearFilters_Click;
                button.Click += FullCatalogClearFiltersClicked;
                break;
            }
        }

        _collectionRows.CollectionChanged += FullCatalogCollectionChanged;
        _ = RefreshFullCatalogAsync();
    }

    private void FullCatalogCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScheduleFullCatalogRefresh();

    private void ScheduleFullCatalogRefresh()
    {
        if (!_fullCatalogRefreshEnabled || _fullCatalogRefreshScheduled) return;
        _fullCatalogRefreshScheduled = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async () =>
        {
            _fullCatalogRefreshScheduled = false;
            await RefreshFullCatalogAsync();
        }));
    }

    private async void FullCatalogFilterChanged(object sender, EventArgs e)
    {
        if (IsLoaded) await RefreshFullCatalogAsync();
    }

    private async void FullCatalogNavClicked(object sender, RoutedEventArgs e)
    {
        ShowPage(CatalogPage, "Catalog", "Browse and explore PlayStation 2 games.", CatalogNavButton);
        await RefreshFullCatalogAsync();
    }

    private async void FullCatalogClearFiltersClicked(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        RegionFilter.SelectedIndex = 0;
        LanguageFilter.SelectedIndex = 0;
        ReleaseTypeFilter.SelectedIndex = 0;
        await RefreshFullCatalogAsync();
    }

    private async Task RefreshFullCatalogAsync()
    {
        if (string.IsNullOrWhiteSpace(_databasePath) || !File.Exists(_databasePath)) return;

        var search = SearchBox.Text.Trim();
        var region = (RegionFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All regions";
        var language = (LanguageFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
        var releaseType = (ReleaseTypeFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All release types";
        var results = new List<GameListItem>();

        await using var connection = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly");
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
ORDER BY g.SortTitle COLLATE NOCASE;";
        command.Parameters.AddWithValue("$search", search);
        command.Parameters.AddWithValue("$pattern", $"%{search}%");
        command.Parameters.AddWithValue("$region", region);
        command.Parameters.AddWithValue("$language", language);
        command.Parameters.AddWithValue("$languageName", LanguageName(language));
        command.Parameters.AddWithValue("$releaseType", releaseType);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var languageCodes = reader.IsDBNull(7)
                ? Array.Empty<string>()
                : reader.GetString(7).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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
        if (results.Count == 0) ClearSelectedGame();
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }
}
