using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;

namespace PlayStation2GameLibrary;

internal static class CatalogFinishingBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded), true);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window)) return;
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
            new Action(window.EnableCatalogFinishingPass));
    }
}

public partial class MainWindow
{
    private bool _catalogFinishingPassEnabled;
    private bool _detailsFinishingBusy;
    private bool _metadataEnrichmentStarted;

    internal void EnableCatalogFinishingPass()
    {
        if (_catalogFinishingPassEnabled) return;
        _catalogFinishingPassEnabled = true;

        GamesGrid.SelectionChanged += (_, _) =>
        {
            if (GamesGrid.SelectedItem is not GameListItem selected) return;
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,
                new Action(() => _ = RefreshConceptualCoverStatusAsync(selected)));
        };

        LayoutUpdated += (_, _) =>
        {
            if (_inlineGameDetailsPage is null || PageTitle.Text != "Game Details" || _detailsFinishingBusy) return;
            var primaryVisible = FindNamed<TextBlock>(_inlineGameDetailsPage, "InlineCollectionPrimary");
            var expected = _inlineReleases.Count == 1 ? "1 release" : $"{_inlineReleases.Count} releases";
            if (_inlineReleases.Count > 0 && primaryVisible?.Text != expected)
                _ = ApplyConceptualGameDetailsUxAsync();
        };

        if (GamesGrid.SelectedItem is GameListItem current)
            _ = RefreshConceptualCoverStatusAsync(current);

        if (!_metadataEnrichmentStarted)
        {
            _metadataEnrichmentStarted = true;
            _ = ApplyBundledMetadataWhenReadyAsync();
        }
    }

    private async Task ApplyBundledMetadataWhenReadyAsync()
    {
        var metadataPath = Path.Combine(AppContext.BaseDirectory, "data", "OPL.metadata.json");
        if (!File.Exists(metadataPath)) return;

        for (var attempt = 0; attempt < 80; attempt++)
        {
            if (!string.IsNullOrWhiteSpace(_databasePath) && File.Exists(_databasePath))
            {
                try
                {
                    await using var db = new SqliteConnection($"Data Source={_databasePath}");
                    await db.OpenAsync();
                    await using var count = db.CreateCommand();
                    count.CommandText = "SELECT COUNT(*) FROM Game";
                    if (Convert.ToInt64(await count.ExecuteScalarAsync()) > 0)
                    {
                        var changed = await MetadataEnrichmentService.ApplyIfAvailableAsync(db, metadataPath);
                        if (changed > 0)
                        {
                            StatusText.Text = $"Catalogue metadata enriched for {changed:N0} games (descriptions / genres where available).";
                            await RefreshGamesAsync();
                        }
                        return;
                    }
                }
                catch
                {
                    // Database initialization may still be in progress; retry briefly.
                }
            }
            await Task.Delay(150);
        }
    }

    private async Task RefreshConceptualCoverStatusAsync(GameListItem selected)
    {
        if (string.IsNullOrWhiteSpace(_databasePath) || !File.Exists(_databasePath)) return;
        var serials = await ReadLinkedSerialsAsync(selected.GameId);
        var coverPath = _mediaLibrary.FindCoverForSerials(serials);
        var image = _mediaLibrary.LoadImage(coverPath);

        SelectedGameCoverImage.Source = image;
        SelectedGameCoverImage.Visibility = image is null ? Visibility.Collapsed : Visibility.Visible;
        NoCoverPlaceholder.Visibility = image is null ? Visibility.Visible : Visibility.Collapsed;

        if (_catalogVisualCover is not null && _catalogVisualNoCover is not null)
        {
            _catalogVisualCover.Source = image;
            _catalogVisualCover.Visibility = image is null ? Visibility.Collapsed : Visibility.Visible;
            _catalogVisualNoCover.Visibility = image is null ? Visibility.Visible : Visibility.Collapsed;
        }

        StatusText.Text = image is null
            ? $"Selected: {selected.Title}. No local front cover found."
            : $"Selected: {selected.Title}. Front cover: {Path.GetFileName(coverPath)}";
    }

    private async Task<IReadOnlyList<string>> ReadLinkedSerialsAsync(string gameId)
    {
        var result = new List<string>();
        await using var db = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly");
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = @"SELECT COALESCE(d.SerialRaw,'')
FROM Release r JOIN Disc d ON d.ReleaseId=r.ReleaseId
WHERE r.GameId=$id
ORDER BY r.IsPrimary DESC,
CASE WHEN r.RegionCode IN ('NorthAmerica','USA','NTSC-U','NTSC-U/C') THEN 0
     WHEN r.RegionCode IN ('Europe','PAL','Europe/PAL','Australia') THEN 1
     WHEN r.RegionCode IN ('Japan','NTSC-J') THEN 2 ELSE 3 END,
d.DiscNumber";
        cmd.Parameters.AddWithValue("$id", gameId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var serial = reader.GetString(0).Trim();
            if (serial.Length > 0 && !result.Contains(serial, StringComparer.OrdinalIgnoreCase)) result.Add(serial);
        }
        return result;
    }

    private async Task ApplyConceptualGameDetailsUxAsync()
    {
        if (_detailsFinishingBusy || _inlineGameDetailsPage is null) return;
        _detailsFinishingBusy = true;
        try
        {
            var page = _inlineGameDetailsPage;
            var gameId = (GamesGrid.SelectedItem as GameListItem)?.GameId;
            if (string.IsNullOrWhiteSpace(gameId) || _inlineReleases.Count == 0) return;

            var releaseCount = _inlineReleases.Count;
            var releaseText = releaseCount == 1 ? "1 release" : $"{releaseCount} releases";

            var metricValue = FindNamed<TextBlock>(page, "InlineCollectionPrimary");
            if (metricValue is not null)
            {
                metricValue.Text = releaseText;
                if (metricValue.Parent is StackPanel metricStack)
                {
                    var label = metricStack.Children.OfType<TextBlock>().FirstOrDefault(x => !ReferenceEquals(x, metricValue));
                    if (label is not null) label.Text = "Linked releases";
                }
            }

            var overviewValue = FindNamed<TextBlock>(page, "InlinePrimarySerial");
            if (overviewValue is not null)
            {
                overviewValue.Text = releaseText;
                SetContainingRowLabel(overviewValue, "Linked releases");
                if (overviewValue.Parent is StackPanel valuePanel)
                {
                    foreach (var border in valuePanel.Children.OfType<Border>()) border.Visibility = Visibility.Collapsed;
                }
            }

            var quickValue = FindNamed<TextBlock>(page, "InlineQuickSerial");
            if (quickValue is not null)
            {
                quickValue.Text = releaseText;
                SetContainingRowLabel(quickValue, "Linked releases");
            }

            var releaseType = FindNamed<TextBlock>(page, "InlineReleaseType");
            if (releaseType is not null)
            {
                var types = _inlineReleases.Select(x => x.ReleaseType).Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                releaseType.Text = types.Length switch
                {
                    0 => "Release variants",
                    1 => types[0],
                    _ => string.Join(" · ", types.Take(3))
                };
            }

            var languages = _inlineReleases.SelectMany(x => x.Languages)
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToArray();
            var overviewLanguages = FindNamed<WrapPanel>(page, "InlineLanguageFlags");
            if (overviewLanguages is not null) PopulateLanguagePanel(overviewLanguages, languages);

            var publisher = FindNamed<TextBlock>(page, "InlinePublisher");
            if (publisher is not null)
            {
                var publishers = _inlineReleases.Select(x => x.Publisher).Where(x => !string.IsNullOrWhiteSpace(x) && x != "—")
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                publisher.Text = publishers.Length == 0 ? "—" : string.Join(", ", publishers.Take(4));
            }

            await using (var db = new SqliteConnection($"Data Source={_databasePath}"))
            {
                await db.OpenAsync();
                await MetadataEnrichmentService.EnsureSchemaAsync(db);
                await using var cmd = db.CreateCommand();
                cmd.CommandText = "SELECT COALESCE(Description,''),COALESCE(Genres,'') FROM Game WHERE GameId=$id";
                cmd.Parameters.AddWithValue("$id", gameId);
                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var description = FindNamed<TextBlock>(page, "InlineDescription");
                    var genres = FindNamed<TextBlock>(page, "InlineGenres");
                    var descValue = reader.GetString(0);
                    var genreValue = reader.GetString(1);
                    if (description is not null) description.Text = string.IsNullOrWhiteSpace(descValue) ? "No description available from the metadata sources." : descValue;
                    if (genres is not null) genres.Text = string.IsNullOrWhiteSpace(genreValue) ? "—" : genreValue;
                }
            }

            var serials = _inlineReleases.SelectMany(x => x.Serial.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToArray();
            var coverPath = _mediaLibrary.FindCoverForSerials(serials);
            var image = _mediaLibrary.LoadImage(coverPath);
            var cover = FindNamed<Image>(page, "InlineCoverImage");
            var placeholder = FindNamed<StackPanel>(page, "InlineNoCover");
            var status = FindNamed<TextBlock>(page, "InlineDetailsStatus");
            if (cover is not null && placeholder is not null)
            {
                cover.Source = image;
                cover.Visibility = image is null ? Visibility.Collapsed : Visibility.Visible;
                placeholder.Visibility = image is null ? Visibility.Visible : Visibility.Collapsed;
            }
            if (status is not null)
                status.Text = image is null ? $"{releaseText} • no local front cover" : $"{releaseText} • {Path.GetFileName(coverPath)}";
        }
        finally
        {
            _detailsFinishingBusy = false;
        }
    }

    private static void SetContainingRowLabel(FrameworkElement value, string text)
    {
        DependencyObject? current = value;
        while (current is not null)
        {
            if (current is Grid grid)
            {
                var label = grid.Children.OfType<TextBlock>().FirstOrDefault(x => Grid.GetColumn(x) == 0);
                if (label is not null)
                {
                    label.Text = text;
                    return;
                }
            }
            current = LogicalTreeHelper.GetParent(current) ?? TryVisualParent(current);
        }
    }

    private static DependencyObject? TryVisualParent(DependencyObject value)
    {
        try { return VisualTreeHelper.GetParent(value); }
        catch { return null; }
    }
}
