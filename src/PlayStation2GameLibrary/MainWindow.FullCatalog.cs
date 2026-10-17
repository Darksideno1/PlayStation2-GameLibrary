using System.Collections.Specialized;
using System.Globalization;
using System.IO;
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
    private bool _fullCatalogRefreshing;
    private bool _catalogFiltersPopulated;
    private bool _populatingCatalogFilters;

    internal sealed record CatalogAggregate(
        IReadOnlyList<string> Regions,
        IReadOnlyList<string> Languages,
        IReadOnlyList<string> ReleaseTypes,
        int ReleaseCount);

    private readonly Dictionary<string, CatalogAggregate> _catalogAggregateByGameId = new(StringComparer.OrdinalIgnoreCase);

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
        _games.CollectionChanged += FullCatalogGamesChanged;
        ScheduleFullCatalogRefresh();
    }

    private void FullCatalogCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScheduleFullCatalogRefresh();

    private void FullCatalogGamesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_fullCatalogRefreshing) ScheduleFullCatalogRefresh();
    }

    private void ScheduleFullCatalogRefresh()
    {
        if (!_fullCatalogRefreshEnabled || _fullCatalogRefreshScheduled || _fullCatalogRefreshing) return;
        _fullCatalogRefreshScheduled = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async () =>
        {
            _fullCatalogRefreshScheduled = false;
            await RefreshFullCatalogAsync();
        }));
    }

    private async void FullCatalogFilterChanged(object sender, EventArgs e)
    {
        if (IsLoaded && !_populatingCatalogFilters) await RefreshFullCatalogAsync();
    }

    private async void FullCatalogNavClicked(object sender, RoutedEventArgs e)
    {
        ShowPage(CatalogPage, "Catalog", "Browse conceptual PlayStation 2 games and all linked releases.", CatalogNavButton);
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

    private async Task PopulateCatalogFiltersAsync()
    {
        if (_catalogFiltersPopulated || _populatingCatalogFilters || string.IsNullOrWhiteSpace(_databasePath) || !File.Exists(_databasePath)) return;
        _populatingCatalogFilters = true;
        try
        {
            await using var db = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly");
            await db.OpenAsync();

            var languages = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
            await using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = "SELECT DISTINCT LanguageCode FROM ReleaseLanguage WHERE TRIM(LanguageCode)<>'' ORDER BY LanguageCode COLLATE NOCASE";
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) languages.Add(CanonicalLanguageName(reader.GetString(0)));
            }

            var releaseTypes = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
            await using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = "SELECT DISTINCT ReleaseType FROM Release WHERE TRIM(ReleaseType)<>'' ORDER BY ReleaseType COLLATE NOCASE";
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) releaseTypes.Add(reader.GetString(0).Trim());
            }

            var regions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = "SELECT DISTINCT RegionCode FROM Release WHERE TRIM(RegionCode)<>''";
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) regions.Add(CatalogRegionLabel(reader.GetString(0)));
            }

            RegionFilter.Items.Clear();
            RegionFilter.Items.Add(new ComboBoxItem { Content = "All regions", IsSelected = true });
            foreach (var region in OrderRegions(regions)) RegionFilter.Items.Add(new ComboBoxItem { Content = region });

            LanguageFilter.Items.Clear();
            LanguageFilter.Items.Add(new ComboBoxItem { Content = "All languages", Tag = string.Empty, IsSelected = true });
            foreach (var language in languages) LanguageFilter.Items.Add(new ComboBoxItem { Content = language, Tag = language });

            ReleaseTypeFilter.Items.Clear();
            ReleaseTypeFilter.Items.Add(new ComboBoxItem { Content = "All release types", IsSelected = true });
            foreach (var releaseType in releaseTypes) ReleaseTypeFilter.Items.Add(new ComboBoxItem { Content = releaseType });

            _catalogFiltersPopulated = true;
        }
        finally
        {
            _populatingCatalogFilters = false;
        }
    }

    private async Task RefreshFullCatalogAsync()
    {
        if (_fullCatalogRefreshing) return;
        if (string.IsNullOrWhiteSpace(_databasePath) || !File.Exists(_databasePath)) return;

        _fullCatalogRefreshing = true;
        try
        {
            await PopulateCatalogFiltersAsync();

            var search = SearchBox.Text.Trim();
            var selectedRegion = (RegionFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All regions";
            var selectedLanguage = (LanguageFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
            var selectedReleaseType = (ReleaseTypeFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All release types";
            var results = new List<GameListItem>();
            var aggregates = new Dictionary<string, CatalogAggregate>(StringComparer.OrdinalIgnoreCase);

            await using var connection = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT g.GameId, g.CanonicalTitle, g.FirstReleaseYear, g.Developer,
       COALESCE((SELECT d.SerialRaw FROM Release pr LEFT JOIN Disc d ON d.ReleaseId=pr.ReleaseId WHERE pr.GameId=g.GameId
                 ORDER BY pr.IsPrimary DESC,CASE WHEN pr.RegionCode IN ('NorthAmerica','USA','NTSC-U','NTSC-U/C') THEN 0 WHEN pr.RegionCode IN ('Europe','PAL','Europe/PAL','Australia') THEN 1 ELSE 2 END,d.DiscNumber LIMIT 1), '') AS PreferredCoverSerial,
       COALESCE((SELECT pr.RegionCode FROM Release pr WHERE pr.GameId=g.GameId
                 ORDER BY pr.IsPrimary DESC,CASE WHEN pr.RegionCode IN ('NorthAmerica','USA','NTSC-U','NTSC-U/C') THEN 0 WHEN pr.RegionCode IN ('Europe','PAL','Europe/PAL','Australia') THEN 1 ELSE 2 END LIMIT 1), 'Unknown') AS PreferredRegion,
       COALESCE((SELECT pr.ReleaseType FROM Release pr WHERE pr.GameId=g.GameId ORDER BY pr.IsPrimary DESC LIMIT 1), 'Retail') AS PreferredReleaseType,
       COALESCE((SELECT group_concat(DISTINCT rl.LanguageCode) FROM Release r JOIN ReleaseLanguage rl ON rl.ReleaseId=r.ReleaseId WHERE r.GameId=g.GameId), '') AS Languages,
       COALESCE((SELECT group_concat(DISTINCT r.RegionCode) FROM Release r WHERE r.GameId=g.GameId), '') AS Regions,
       COALESCE((SELECT group_concat(DISTINCT r.ReleaseType) FROM Release r WHERE r.GameId=g.GameId), '') AS ReleaseTypes,
       (SELECT COUNT(*) FROM Release r WHERE r.GameId=g.GameId) AS ReleaseCount,
       (SELECT COUNT(*) FROM CollectionCopy c WHERE c.CopyType IN ('Physical','Digital') AND (c.GameId=g.GameId OR c.ReleaseId IN (SELECT rr.ReleaseId FROM Release rr WHERE rr.GameId=g.GameId))) AS OwnedCount
FROM Game g
WHERE ($search='' OR g.CanonicalTitle LIKE $pattern OR COALESCE(g.Developer,'') LIKE $pattern
   OR EXISTS (SELECT 1 FROM Release r WHERE r.GameId=g.GameId AND (COALESCE(r.Publisher,'') LIKE $pattern OR r.ReleaseTitle LIKE $pattern OR EXISTS (SELECT 1 FROM Disc d WHERE d.ReleaseId=r.ReleaseId AND COALESCE(d.SerialRaw,'') LIKE $pattern))))
ORDER BY g.SortTitle COLLATE NOCASE;";
            command.Parameters.AddWithValue("$search", search);
            command.Parameters.AddWithValue("$pattern", $"%{search}%");

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var gameId = reader.GetString(0);
                var rawLanguages = SplitValues(reader.IsDBNull(7) ? string.Empty : reader.GetString(7));
                var rawRegions = SplitValues(reader.IsDBNull(8) ? string.Empty : reader.GetString(8));
                var rawTypes = SplitValues(reader.IsDBNull(9) ? string.Empty : reader.GetString(9));

                var regions = rawRegions.Select(CatalogRegionLabel).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var languages = rawLanguages.Select(CanonicalLanguageName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToArray();
                var releaseTypes = rawTypes.Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToArray();
                var aggregate = new CatalogAggregate(regions, languages, releaseTypes, reader.GetInt32(10));
                aggregates[gameId] = aggregate;

                if (selectedRegion != "All regions" && !regions.Contains(selectedRegion, StringComparer.OrdinalIgnoreCase)) continue;
                if (selectedLanguage.Length > 0 && !languages.Contains(selectedLanguage, StringComparer.OrdinalIgnoreCase)) continue;
                if (selectedReleaseType != "All release types" && !releaseTypes.Contains(selectedReleaseType, StringComparer.OrdinalIgnoreCase)) continue;

                var supportedFlags = rawLanguages
                    .Select(FlagIconFactory.ForLanguage)
                    .Where(x => x is not null)
                    .Cast<ImageSource>()
                    .Distinct()
                    .ToArray();

                results.Add(new GameListItem
                {
                    GameId = gameId,
                    Title = reader.GetString(1),
                    Year = reader.IsDBNull(2) ? "—" : reader.GetInt32(2).ToString(CultureInfo.InvariantCulture),
                    Developer = reader.IsDBNull(3) ? "—" : reader.GetString(3),
                    // PrimarySerial/Region/ReleaseType remain internal compatibility fields.
                    // The catalogue grid no longer presents them as the identity of a conceptual game.
                    PrimarySerial = reader.GetString(4),
                    Region = CatalogRegionLabel(reader.GetString(5)),
                    RegionFlag = FlagIconFactory.ForRegion(reader.GetString(5)),
                    ReleaseType = reader.GetString(6),
                    LanguageFlags = supportedFlags,
                    OwnedCount = reader.GetInt32(11)
                });
            }

            _catalogAggregateByGameId.Clear();
            foreach (var pair in aggregates) _catalogAggregateByGameId[pair.Key] = pair.Value;

            _games.Clear();
            foreach (var item in results) _games.Add(item);
            GamesGrid.Items.Refresh();
            ResultCountText.Text = $"{results.Count:N0} games";
            DatabaseCountText.Text = $"Catalogue: {results.Count:N0} shown";
            FooterCatalogText.Text = $"{results.Count:N0} shown";
            if (results.Count > 0 && GamesGrid.SelectedItem is null) GamesGrid.SelectedIndex = 0;
            if (results.Count == 0) ClearSelectedGame();
        }
        finally
        {
            _fullCatalogRefreshing = false;
        }
    }

    private static string[] SplitValues(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    internal static string CanonicalLanguageName(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        var key = text.ToLowerInvariant();
        return key switch
        {
            "en" or "eng" or "english" => "English",
            "fr" or "fra" or "fre" or "french" => "French",
            "de" or "deu" or "ger" or "german" => "German",
            "it" or "ita" or "italian" => "Italian",
            "es" or "spa" or "spanish" => "Spanish",
            "ja" or "jpn" or "japanese" => "Japanese",
            "ko" or "kor" or "korean" => "Korean",
            "nl" or "nld" or "dut" or "dutch" => "Dutch",
            "pt" or "por" or "portuguese" => "Portuguese",
            "sv" or "swe" or "swedish" => "Swedish",
            "fi" or "fin" or "finnish" => "Finnish",
            "da" or "dan" or "danish" or "danks" => "Danish",
            "no" or "nor" or "norwegian" or "norweigan" => "Norwegian",
            "pl" or "pol" or "polish" => "Polish",
            "ru" or "rus" or "russian" => "Russian",
            "el" or "ell" or "gre" or "greek" => "Greek",
            "zh" or "zho" or "chi" or "chinese" => "Chinese",
            "cs" or "ces" or "cze" or "czech" => "Czech",
            "hu" or "hun" or "hungarian" => "Hungarian",
            "tr" or "tur" or "turkish" => "Turkish",
            "ar" or "ara" or "arabic" => "Arabic",
            "hi" or "hin" or "hindi" => "Hindi",
            "ga" or "gle" or "gaelic" => "Gaelic",
            "sl" or "slv" or "slovenian" => "Slovenian",
            "ca" or "cat" or "catalan" => "Catalan",
            _ => text.Length == 0 ? "Unknown" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text.ToLowerInvariant())
        };
    }

    internal static string CatalogRegionLabel(string? region)
    {
        var value = (region ?? string.Empty).Trim().ToLowerInvariant();
        if (value.Contains("korea") || value is "kr" or "k") return "Korea";
        if (value.Contains("north") || value.Contains("usa") || value.Contains("ntsc-u") || value is "us" or "u") return "NTSC-U/C";
        if (value.Contains("japan") || value.Contains("ntsc-j") || value is "jp" or "j") return "NTSC-J";
        if (value.Contains("europe") || value.Contains("pal") || value.Contains("australia") || value is "eu" or "e" or "au") return "PAL";
        return string.IsNullOrWhiteSpace(region) ? "Unknown" : region.Trim();
    }

    private static IEnumerable<string> OrderRegions(IEnumerable<string> regions)
    {
        var order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["PAL"] = 0, ["NTSC-U/C"] = 1, ["NTSC-J"] = 2, ["Korea"] = 3, ["Unknown"] = 99
        };
        return regions.OrderBy(x => order.TryGetValue(x, out var rank) ? rank : 50).ThenBy(x => x, StringComparer.OrdinalIgnoreCase);
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
