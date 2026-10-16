using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;

namespace PlayStation2GameLibrary;

public partial class MainWindow
{
    private bool _visualPolishApplied;
    private bool _ownershipRefreshScheduled;
    private readonly Dictionary<string, (int Physical, int Digital)> _ownershipByGameId = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<CatalogSideReleaseItem> _catalogSideReleases = new();

    private Image? _catalogVisualCover;
    private StackPanel? _catalogVisualNoCover;
    private TextBlock? _catalogVisualTitle;
    private Border? _catalogVisualCollectionBadge;
    private TextBlock? _catalogVisualCollectionBadgeText;
    private TextBlock? _catalogVisualSerial;
    private StackPanel? _catalogVisualLanguages;
    private TextBlock? _catalogVisualReleaseYear;
    private TextBlock? _catalogVisualDeveloper;
    private TextBlock? _catalogVisualPublisher;

    private Image? _collectionVisualCover;
    private StackPanel? _collectionVisualNoCover;
    private TextBlock? _collectionVisualTitle;
    private ContentControl? _collectionVisualRegionHost;
    private ContentControl? _collectionVisualTypeHost;
    private TextBlock? _collectionVisualSerial;
    private TextBlock? _collectionSelectedBadgeText;
    private TextBlock? _collectionConditionText;
    private TextBlock? _collectionPurchaseDateText;
    private TextBlock? _collectionNotesText;
    private StackPanel? _collectionPhysicalPanel;
    private StackPanel? _collectionDigitalPanel;
    private Border? _collectionCaseCard;
    private Border? _collectionCoverCard;
    private Border? _collectionDiscCard;
    private Border? _collectionManualCard;
    private TextBlock? _collectionCaseState;
    private TextBlock? _collectionCoverState;
    private TextBlock? _collectionDiscState;
    private TextBlock? _collectionManualState;
    private TextBlock? _collectionDigitalSizeText;
    private TextBlock? _collectionStorageText;
    private TextBlock? _collectionFormatText;
    private TextBlock? _collectionDigitalPurchaseText;
    private TextBlock? _collectionDigitalSourceText;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_visualPolishApplied) return;
        _visualPolishApplied = true;

        ApplyCatalogVisualPolish();
        ApplyCollectionVisualPolish();

        GamesGrid.SelectionChanged += async (_, _) =>
        {
            if (GamesGrid.SelectedItem is GameListItem selected)
                await RefreshCatalogVisualAsync(selected);
            else
                ClearCatalogVisual();
        };
        CollectionGrid.SelectionChanged += async (_, _) =>
        {
            if (CollectionGrid.SelectedItem is CollectionListItem selected)
                await RefreshCollectionVisualAsync(selected);
            else
                ClearCollectionVisual();
        };
        _games.CollectionChanged += GamesForVisualPolish_CollectionChanged;

        ScheduleOwnershipRefresh();
        if (GamesGrid.SelectedItem is GameListItem game) _ = RefreshCatalogVisualAsync(game);
        if (CollectionGrid.SelectedItem is CollectionListItem copy) _ = RefreshCollectionVisualAsync(copy);
    }

    private void ApplyCatalogVisualPolish()
    {
        CatalogPage.ColumnDefinitions[1].Width = new GridLength(405);
        GamesGrid.RowHeight = 48;
        GamesGrid.ColumnHeaderHeight = 38;
        GamesGrid.Columns.Clear();
        GamesGrid.Columns.Add(new DataGridTextColumn { Header = "ID", Binding = new Binding("PrimarySerial"), Width = 115, MinWidth = 105 });
        GamesGrid.Columns.Add(new DataGridTextColumn { Header = "TITLE", Binding = new Binding("Title"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 230 });
        GamesGrid.Columns.Add(new DataGridTemplateColumn { Header = "REGION", Width = 125, MinWidth = 110, CellTemplate = BuildRegionPillTemplate() });
        GamesGrid.Columns.Add(new DataGridTemplateColumn { Header = "LANGUAGES", Width = 155, MinWidth = 125, CellTemplate = BuildLanguageFlagsTemplate() });
        GamesGrid.Columns.Add(new DataGridTemplateColumn { Header = "RELEASE", Width = 100, MinWidth = 90, CellTemplate = BuildReleasePillTemplate() });
        GamesGrid.Columns.Add(new DataGridTemplateColumn { Header = "OWNED", Width = 185, MinWidth = 145, CellTemplate = BuildOwnedBadgesTemplate() });

        var right = CatalogPage.Children.OfType<Border>().FirstOrDefault(b => Grid.GetColumn(b) == 1);
        if (right is not null) right.Child = BuildCatalogSidePanel();
    }

    private FrameworkElement BuildCatalogSidePanel()
    {
        var dock = new DockPanel();

        var actions = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        DockPanel.SetDock(actions, Dock.Bottom);
        dock.Children.Add(actions);

        var detailsButton = new Button
        {
            Content = "Open Game Details",
            Background = Brush("#2F82E5"), Foreground = Brushes.White, BorderBrush = Brush("#2F82E5"),
            FontWeight = FontWeights.SemiBold, Padding = new Thickness(10, 10, 10, 10), Margin = new Thickness(0, 0, 5, 0)
        };
        detailsButton.Click += InlineGameDetails_Click;
        actions.Children.Add(detailsButton);

        var addButton = new Button
        {
            Content = "+ Add to Collection",
            Background = Brush("#22B573"), Foreground = Brushes.White, BorderBrush = Brush("#22B573"),
            FontWeight = FontWeights.SemiBold, Padding = new Thickness(10, 10, 10, 10), Margin = new Thickness(5, 0, 0, 0)
        };
        addButton.Click += AddGame_Click;
        Grid.SetColumn(addButton, 1);
        actions.Children.Add(addButton);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        dock.Children.Add(scroll);
        var stack = new StackPanel();
        scroll.Content = stack;

        var coverFrame = new Border
        {
            Height = 300, Background = Brush("#F4F7FA"), BorderBrush = Brush("#D3E1EE"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(8)
        };
        stack.Children.Add(coverFrame);
        var coverGrid = new Grid(); coverFrame.Child = coverGrid;
        _catalogVisualCover = new Image { Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };
        coverGrid.Children.Add(_catalogVisualCover);
        _catalogVisualNoCover = BuildNoCoverPlaceholder(42);
        coverGrid.Children.Add(_catalogVisualNoCover);

        _catalogVisualTitle = new TextBlock
        {
            Text = "Select a game", FontSize = 23, FontWeight = FontWeights.Bold, Foreground = Brush("#10243F"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 9)
        };
        stack.Children.Add(_catalogVisualTitle);

        _catalogVisualCollectionBadgeText = new TextBlock { Text = "Not in collection", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brush("#5D7187") };
        _catalogVisualCollectionBadge = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left, Background = Brush("#F2F5F8"), BorderBrush = Brush("#D8E1EA"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 5, 9, 5), Child = _catalogVisualCollectionBadgeText
        };
        stack.Children.Add(_catalogVisualCollectionBadge);

        stack.Children.Add(SectionDivider());

        var meta = new Grid();
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 5; i++) meta.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        stack.Children.Add(meta);

        _catalogVisualSerial = AddMetaTextRow(meta, 0, "Primary serial:");
        _catalogVisualLanguages = AddMetaFlagsRow(meta, 1, "Languages:");
        _catalogVisualReleaseYear = AddMetaTextRow(meta, 2, "Release year:");
        _catalogVisualDeveloper = AddMetaTextRow(meta, 3, "Developer:");
        _catalogVisualPublisher = AddMetaTextRow(meta, 4, "Publisher:");

        stack.Children.Add(SectionDivider());
        stack.Children.Add(new TextBlock { Text = "Regions & Releases", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brush("#10243F"), Margin = new Thickness(0, 0, 0, 4) });
        var releases = new ItemsControl { ItemsSource = _catalogSideReleases, ItemTemplate = BuildSideReleaseTemplate() };
        stack.Children.Add(releases);
        return dock;
    }

    private async Task RefreshCatalogVisualAsync(GameListItem selected)
    {
        if (_catalogVisualTitle is null || string.IsNullOrWhiteSpace(_databasePath)) return;
        _catalogVisualTitle.Text = selected.Title;
        _catalogVisualDeveloper!.Text = selected.Developer;
        _catalogVisualSerial!.Text = string.IsNullOrWhiteSpace(selected.PrimarySerial) ? "—" : selected.PrimarySerial;
        _catalogVisualReleaseYear!.Text = selected.Year;

        var coverPath = _mediaLibrary.FindCoverForSerial(selected.PrimarySerial);
        var cover = _mediaLibrary.LoadImage(coverPath);
        _catalogVisualCover!.Source = cover;
        _catalogVisualCover.Visibility = cover is null ? Visibility.Collapsed : Visibility.Visible;
        _catalogVisualNoCover!.Visibility = cover is null ? Visibility.Visible : Visibility.Collapsed;

        _catalogSideReleases.Clear();
        var primaryPublisher = "—";
        var primaryYear = selected.Year;
        IReadOnlyList<ImageSource> primaryLanguageFlags = selected.LanguageFlags;

        try
        {
            await using var db = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly");
            await db.OpenAsync();
            await using var cmd = db.CreateCommand();
            cmd.CommandText = @"SELECT r.RegionCode,r.ReleaseYear,COALESCE(r.Publisher,'—'),r.ReleaseType,r.IsPrimary,
COALESCE((SELECT group_concat(d.SerialRaw, ', ') FROM Disc d WHERE d.ReleaseId=r.ReleaseId),'—'),
COALESCE((SELECT group_concat(DISTINCT rl.LanguageCode) FROM ReleaseLanguage rl WHERE rl.ReleaseId=r.ReleaseId),'')
FROM Release r WHERE r.GameId=$id
ORDER BY r.IsPrimary DESC,
CASE WHEN r.RegionCode IN ('NorthAmerica','USA','NTSC-U','NTSC-U/C') THEN 0
     WHEN r.RegionCode IN ('Europe','PAL','Europe/PAL','Australia') THEN 1 ELSE 2 END,
r.ReleaseYear,r.ReleaseTitle";
            cmd.Parameters.AddWithValue("$id", selected.GameId);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var rawRegion = reader.IsDBNull(0) ? "Unknown" : reader.GetString(0);
                var region = FriendlyRegion(rawRegion);
                var year = reader.IsDBNull(1) ? "—" : reader.GetInt32(1).ToString(CultureInfo.InvariantCulture);
                var publisher = reader.IsDBNull(2) ? "—" : reader.GetString(2);
                var releaseType = reader.IsDBNull(3) ? "Retail" : reader.GetString(3);
                var isPrimary = !reader.IsDBNull(4) && reader.GetInt32(4) == 1;
                var serial = reader.IsDBNull(5) ? "—" : reader.GetString(5);
                var codes = reader.IsDBNull(6) ? Array.Empty<string>() : reader.GetString(6).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var flags = codes.Select(FlagIconFactory.ForLanguage).Where(x => x is not null).Cast<ImageSource>().ToArray();
                var note = isPrimary ? "Primary release" : region == "PAL" ? "Related PAL release" : region == "NTSC-U/C" ? "North American release" : region == "NTSC-J" ? "Japanese release" : "Related release";
                _catalogSideReleases.Add(new CatalogSideReleaseItem(region, FlagIconFactory.ForRegion(rawRegion), serial, note, releaseType, year, publisher, flags, isPrimary));
                if (isPrimary || _catalogSideReleases.Count == 1)
                {
                    primaryPublisher = publisher;
                    primaryYear = year;
                    primaryLanguageFlags = flags;
                }
            }
        }
        catch
        {
            // The catalogue list remains usable even when the compact side preview cannot be populated.
        }

        _catalogVisualPublisher!.Text = primaryPublisher;
        _catalogVisualReleaseYear!.Text = primaryYear;
        PopulateFlags(_catalogVisualLanguages!, primaryLanguageFlags);

        var ownership = await ReadOwnershipAsync(selected.GameId);
        UpdateCatalogCollectionBadge(ownership.Physical, ownership.Digital);
    }

    private void UpdateCatalogCollectionBadge(int physical, int digital)
    {
        if (_catalogVisualCollectionBadge is null || _catalogVisualCollectionBadgeText is null) return;
        if (physical + digital == 0)
        {
            _catalogVisualCollectionBadge.Background = Brush("#F2F5F8");
            _catalogVisualCollectionBadge.BorderBrush = Brush("#D8E1EA");
            _catalogVisualCollectionBadgeText.Foreground = Brush("#5D7187");
            _catalogVisualCollectionBadgeText.Text = "Not in collection";
            return;
        }

        var parts = new List<string>();
        if (physical > 0) parts.Add($"{physical} Physical");
        if (digital > 0) parts.Add($"{digital} Digital");
        _catalogVisualCollectionBadge.Background = Brush("#E4F8EE");
        _catalogVisualCollectionBadge.BorderBrush = Brush("#C8EDD9");
        _catalogVisualCollectionBadgeText.Foreground = Brush("#087A46");
        _catalogVisualCollectionBadgeText.Text = "✓ In Collection · " + string.Join(" · ", parts);
    }

    private void ClearCatalogVisual()
    {
        if (_catalogVisualTitle is null) return;
        _catalogVisualTitle.Text = "No games found";
        _catalogVisualSerial!.Text = "—";
        _catalogVisualReleaseYear!.Text = "—";
        _catalogVisualDeveloper!.Text = "—";
        _catalogVisualPublisher!.Text = "—";
        _catalogVisualLanguages!.Children.Clear();
        _catalogSideReleases.Clear();
        _catalogVisualCover!.Source = null;
        _catalogVisualCover.Visibility = Visibility.Collapsed;
        _catalogVisualNoCover!.Visibility = Visibility.Visible;
        UpdateCatalogCollectionBadge(0, 0);
    }

    private void GamesForVisualPolish_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScheduleOwnershipRefresh();

    private void ScheduleOwnershipRefresh()
    {
        if (_ownershipRefreshScheduled) return;
        _ownershipRefreshScheduled = true;
        Dispatcher.BeginInvoke(async () =>
        {
            try { await RefreshOwnershipBadgesAsync(); }
            finally { _ownershipRefreshScheduled = false; }
        }, DispatcherPriority.Background);
    }

    private async Task RefreshOwnershipBadgesAsync()
    {
        if (string.IsNullOrWhiteSpace(_databasePath) || !File.Exists(_databasePath)) return;
        var map = new Dictionary<string, (int Physical, int Digital)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await using var db = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly");
            await db.OpenAsync();
            await using var cmd = db.CreateCommand();
            cmd.CommandText = @"SELECT COALESCE(c.GameId,r.GameId) AS GameId,c.CopyType,COUNT(*)
FROM CollectionCopy c LEFT JOIN Release r ON r.ReleaseId=c.ReleaseId
WHERE c.CopyType IN ('Physical','Digital') AND COALESCE(c.GameId,r.GameId) IS NOT NULL
GROUP BY COALESCE(c.GameId,r.GameId),c.CopyType";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var gameId = reader.GetString(0);
                var type = reader.GetString(1);
                var count = reader.GetInt32(2);
                map.TryGetValue(gameId, out var current);
                map[gameId] = type == "Physical" ? (count, current.Digital) : (current.Physical, count);
            }
        }
        catch { }

        _ownershipByGameId.Clear();
        foreach (var pair in map) _ownershipByGameId[pair.Key] = pair.Value;
        GamesGrid.Items.Refresh();
    }

    private async Task<(int Physical, int Digital)> ReadOwnershipAsync(string gameId)
    {
        if (_ownershipByGameId.TryGetValue(gameId, out var cached)) return cached;
        if (string.IsNullOrWhiteSpace(_databasePath)) return (0, 0);
        var physical = 0; var digital = 0;
        try
        {
            await using var db = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly"); await db.OpenAsync();
            await using var cmd = db.CreateCommand();
            cmd.CommandText = @"SELECT c.CopyType,COUNT(*) FROM CollectionCopy c
WHERE c.CopyType IN ('Physical','Digital') AND (c.GameId=$g OR c.ReleaseId IN (SELECT ReleaseId FROM Release WHERE GameId=$g))
GROUP BY c.CopyType";
            cmd.Parameters.AddWithValue("$g", gameId);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (reader.GetString(0) == "Physical") physical = reader.GetInt32(1); else digital = reader.GetInt32(1);
            }
        }
        catch { }
        _ownershipByGameId[gameId] = (physical, digital);
        return (physical, digital);
    }

    private void ApplyCollectionVisualPolish()
    {
        CollectionPage.ColumnDefinitions[1].Width = new GridLength(420);
        CollectionGrid.RowHeight = 48;
        CollectionGrid.ColumnHeaderHeight = 38;
        CollectionGrid.Columns.Clear();
        CollectionGrid.Columns.Add(new DataGridTextColumn { Header = "GAME TITLE", Binding = new Binding("Title"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 260 });
        CollectionGrid.Columns.Add(new DataGridTemplateColumn { Header = "TYPE", Width = 105, CellTemplate = BuildCopyTypePillTemplate() });
        CollectionGrid.Columns.Add(new DataGridTemplateColumn { Header = "REGION", Width = 125, CellTemplate = BuildCollectionRegionPillTemplate() });
        CollectionGrid.Columns.Add(new DataGridTextColumn { Header = "SIZE", Binding = new Binding("SizeDisplay"), Width = 100 });
        CollectionGrid.Columns.Add(new DataGridTextColumn { Header = "NOTES", Binding = new Binding("Notes"), Width = 210 });

        var right = CollectionPage.Children.OfType<Border>().FirstOrDefault(b => Grid.GetColumn(b) == 1);
        if (right is not null) right.Child = BuildCollectionSidePanel();
    }

    private FrameworkElement BuildCollectionSidePanel()
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var stack = new StackPanel(); scroll.Content = stack;

        var coverFrame = new Border
        {
            Height = 275, Background = Brush("#F4F7FA"), BorderBrush = Brush("#D3E1EE"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(8)
        };
        stack.Children.Add(coverFrame);
        var coverGrid = new Grid(); coverFrame.Child = coverGrid;
        _collectionVisualCover = new Image { Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };
        coverGrid.Children.Add(_collectionVisualCover);
        _collectionVisualNoCover = BuildNoCoverPlaceholder(38);
        coverGrid.Children.Add(_collectionVisualNoCover);

        stack.Children.Add(new TextBlock { Text = "Game overview", Foreground = Brush("#72869B"), FontSize = 11, Margin = new Thickness(0, 11, 0, 2) });
        _collectionVisualTitle = new TextBlock { Text = "Select a copy", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Brush("#10243F"), TextWrapping = TextWrapping.Wrap };
        stack.Children.Add(_collectionVisualTitle);

        var meta = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 3; i++) meta.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        stack.Children.Add(meta);
        _collectionVisualSerial = AddMetaTextRow(meta, 0, "Primary serial:");
        AddMetaLabel(meta, 1, "Region:");
        _collectionVisualRegionHost = new ContentControl { Margin = new Thickness(0, 3, 0, 3), HorizontalAlignment = HorizontalAlignment.Left };
        Grid.SetRow(_collectionVisualRegionHost, 1); Grid.SetColumn(_collectionVisualRegionHost, 1); meta.Children.Add(_collectionVisualRegionHost);
        AddMetaLabel(meta, 2, "Type:");
        _collectionVisualTypeHost = new ContentControl { Margin = new Thickness(0, 3, 0, 3), HorizontalAlignment = HorizontalAlignment.Left };
        Grid.SetRow(_collectionVisualTypeHost, 2); Grid.SetColumn(_collectionVisualTypeHost, 1); meta.Children.Add(_collectionVisualTypeHost);

        stack.Children.Add(SectionDivider());
        var selectedHeader = new Grid(); selectedHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); selectedHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        selectedHeader.Children.Add(new TextBlock { Text = "Selected Copy Details", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brush("#17314D"), VerticalAlignment = VerticalAlignment.Center });
        _collectionSelectedBadgeText = new TextBlock { Text = "Selected copy", Foreground = Brush("#1264BD"), FontWeight = FontWeights.SemiBold, FontSize = 11 };
        var selectedChip = new Border { Background = Brush("#E8F3FF"), BorderBrush = Brush("#CAE1F7"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(9, 5, 9, 5), Child = _collectionSelectedBadgeText };
        Grid.SetColumn(selectedChip, 1); selectedHeader.Children.Add(selectedChip); stack.Children.Add(selectedHeader);

        var summary = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        summary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        summary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var conditionCard = MiniInfoCard("Condition", out _collectionConditionText); conditionCard.Margin = new Thickness(0, 0, 5, 0); summary.Children.Add(conditionCard);
        var dateCard = MiniInfoCard("Purchase date", out _collectionPurchaseDateText); dateCard.Margin = new Thickness(5, 0, 0, 0); Grid.SetColumn(dateCard, 1); summary.Children.Add(dateCard);
        stack.Children.Add(summary);
        var notesCard = MiniInfoCard("Notes", out _collectionNotesText); notesCard.Margin = new Thickness(0, 10, 0, 0); stack.Children.Add(notesCard);

        _collectionPhysicalPanel = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        stack.Children.Add(_collectionPhysicalPanel);
        _collectionPhysicalPanel.Children.Add(new TextBlock { Text = "Completeness (Physical)", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brush("#17314D") });
        _collectionPhysicalPanel.Children.Add(new TextBlock { Text = "What this copy includes", Foreground = Brush("#72869B"), FontSize = 11, Margin = new Thickness(0, 2, 0, 8) });
        var completeness = new UniformGrid { Columns = 2 };
        _collectionPhysicalPanel.Children.Add(completeness);
        _collectionCaseCard = CompletenessCard("▣", "Original case", "Original PS2 case", out _collectionCaseState); _collectionCaseCard.Margin = new Thickness(0, 0, 5, 5); completeness.Children.Add(_collectionCaseCard);
        _collectionCoverCard = CompletenessCard("▤", "Cover", "Printed cover included", out _collectionCoverState); _collectionCoverCard.Margin = new Thickness(5, 0, 0, 5); completeness.Children.Add(_collectionCoverCard);
        _collectionDiscCard = CompletenessCard("◉", "Disc", "Game disc included", out _collectionDiscState); _collectionDiscCard.Margin = new Thickness(0, 5, 5, 0); completeness.Children.Add(_collectionDiscCard);
        _collectionManualCard = CompletenessCard("▥", "Manual", "Instruction booklet", out _collectionManualState); _collectionManualCard.Margin = new Thickness(5, 5, 0, 0); completeness.Children.Add(_collectionManualCard);

        _collectionDigitalPanel = new StackPanel { Margin = new Thickness(0, 14, 0, 0), Visibility = Visibility.Collapsed };
        stack.Children.Add(_collectionDigitalPanel);
        _collectionDigitalPanel.Children.Add(new TextBlock { Text = "Digital Entry Details", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brush("#17314D") });
        var dg = new Grid { Margin = new Thickness(0, 9, 0, 0) }; dg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); dg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var sizeCard = MiniInfoCard("Digital size", out _collectionDigitalSizeText); sizeCard.Margin = new Thickness(0, 0, 5, 5); dg.Children.Add(sizeCard);
        var storageCard = MiniInfoCard("Storage location", out _collectionStorageText); storageCard.Margin = new Thickness(5, 0, 0, 5); Grid.SetColumn(storageCard, 1); dg.Children.Add(storageCard);
        dg.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); dg.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var formatCard = MiniInfoCard("File format", out _collectionFormatText); formatCard.Margin = new Thickness(0, 5, 5, 0); Grid.SetRow(formatCard, 1); dg.Children.Add(formatCard);
        var sourceCard = MiniInfoCard("Source / seller", out _collectionDigitalSourceText); sourceCard.Margin = new Thickness(5, 5, 0, 0); Grid.SetRow(sourceCard, 1); Grid.SetColumn(sourceCard, 1); dg.Children.Add(sourceCard);
        _collectionDigitalPanel.Children.Add(dg);
        var digitalPurchase = MiniInfoCard("Purchase date", out _collectionDigitalPurchaseText); digitalPurchase.Margin = new Thickness(0, 10, 0, 0); _collectionDigitalPanel.Children.Add(digitalPurchase);

        return scroll;
    }

    private async Task RefreshCollectionVisualAsync(CollectionListItem row)
    {
        if (_collectionVisualTitle is null || string.IsNullOrWhiteSpace(_databasePath)) return;
        _collectionVisualTitle.Text = row.Title;
        _collectionVisualSerial!.Text = string.IsNullOrWhiteSpace(row.PrimarySerial) ? "—" : row.PrimarySerial;
        _collectionVisualRegionHost!.Content = BuildRegionPill(row.Region, FlagIconFactory.ForRegion(row.Region));
        _collectionVisualTypeHost!.Content = BuildCopyTypePill(row.CopyType);
        _collectionSelectedBadgeText!.Text = $"{row.CopyType} copy";

        var coverPath = _mediaLibrary.FindCoverForSerial(row.PrimarySerial);
        var cover = _mediaLibrary.LoadImage(coverPath);
        _collectionVisualCover!.Source = cover;
        _collectionVisualCover.Visibility = cover is null ? Visibility.Collapsed : Visibility.Visible;
        _collectionVisualNoCover!.Visibility = cover is null ? Visibility.Visible : Visibility.Collapsed;

        _collectionConditionText!.Text = "—";
        _collectionPurchaseDateText!.Text = "—";
        _collectionNotesText!.Text = string.IsNullOrWhiteSpace(row.Notes) ? "—" : row.Notes;
        _collectionDigitalSizeText!.Text = row.SizeDisplay;
        _collectionStorageText!.Text = "—";
        _collectionFormatText!.Text = "—";
        _collectionDigitalPurchaseText!.Text = "—";
        _collectionDigitalSourceText!.Text = "—";

        _collectionPhysicalPanel!.Visibility = row.CopyType == "Physical" ? Visibility.Visible : Visibility.Collapsed;
        _collectionDigitalPanel!.Visibility = row.CopyType == "Digital" ? Visibility.Visible : Visibility.Collapsed;
        SetCompletenessCard(_collectionCaseCard!, _collectionCaseState!, "Unknown");
        SetCompletenessCard(_collectionCoverCard!, _collectionCoverState!, "Unknown");
        SetCompletenessCard(_collectionDiscCard!, _collectionDiscState!, "Unknown");
        SetCompletenessCard(_collectionManualCard!, _collectionManualState!, "Unknown");

        try
        {
            await using var db = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly"); await db.OpenAsync();
            await using (var copyCmd = db.CreateCommand())
            {
                copyCmd.CommandText = "SELECT COALESCE(Condition,''),COALESCE(StorageLocation,''),COALESCE(Notes,''),DigitalSizeBytes FROM CollectionCopy WHERE CopyId=$id";
                copyCmd.Parameters.AddWithValue("$id", row.CopyId);
                await using var reader = await copyCmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    _collectionConditionText.Text = reader.IsDBNull(0) || string.IsNullOrWhiteSpace(reader.GetString(0)) ? "Not rated" : reader.GetString(0);
                    _collectionStorageText.Text = reader.IsDBNull(1) || string.IsNullOrWhiteSpace(reader.GetString(1)) ? "—" : reader.GetString(1);
                    if (!reader.IsDBNull(2) && !string.IsNullOrWhiteSpace(reader.GetString(2))) _collectionNotesText.Text = reader.GetString(2);
                    if (!reader.IsDBNull(3)) _collectionDigitalSizeText.Text = FormatBytes(reader.GetInt64(3));
                }
            }
            await using (var detailCmd = db.CreateCommand())
            {
                detailCmd.CommandText = @"SELECT CoverPresent,CasePresent,DiscPresent,ManualPresent,DigitalFormat,PurchaseDate,PurchasePrice,Seller
FROM CollectionCopyDetails WHERE CopyId=$id";
                detailCmd.Parameters.AddWithValue("$id", row.CopyId);
                await using var reader = await detailCmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    string S(int i) => reader.IsDBNull(i) ? "" : reader.GetString(i);
                    SetCompletenessCard(_collectionCoverCard!, _collectionCoverState!, S(0));
                    SetCompletenessCard(_collectionCaseCard!, _collectionCaseState!, S(1));
                    SetCompletenessCard(_collectionDiscCard!, _collectionDiscState!, S(2));
                    SetCompletenessCard(_collectionManualCard!, _collectionManualState!, S(3));
                    _collectionFormatText!.Text = string.IsNullOrWhiteSpace(S(4)) ? "—" : S(4);
                    var purchaseDate = string.IsNullOrWhiteSpace(S(5)) ? "—" : S(5);
                    _collectionPurchaseDateText!.Text = purchaseDate;
                    _collectionDigitalPurchaseText!.Text = purchaseDate;
                    var seller = S(7);
                    var price = S(6);
                    _collectionDigitalSourceText!.Text = string.IsNullOrWhiteSpace(seller) && string.IsNullOrWhiteSpace(price) ? "—" : string.Join(" · ", new[] { seller, price }.Where(x => !string.IsNullOrWhiteSpace(x)));
                }
            }
        }
        catch { }
    }

    private void ClearCollectionVisual()
    {
        if (_collectionVisualTitle is null) return;
        _collectionVisualTitle.Text = "No copies found";
        _collectionVisualSerial!.Text = "—";
        _collectionVisualRegionHost!.Content = null;
        _collectionVisualTypeHost!.Content = null;
        _collectionSelectedBadgeText!.Text = "No copy selected";
        _collectionConditionText!.Text = "—";
        _collectionPurchaseDateText!.Text = "—";
        _collectionNotesText!.Text = "No collection entries match this view.";
        _collectionVisualCover!.Source = null;
        _collectionVisualCover.Visibility = Visibility.Collapsed;
        _collectionVisualNoCover!.Visibility = Visibility.Visible;
        _collectionPhysicalPanel!.Visibility = Visibility.Collapsed;
        _collectionDigitalPanel!.Visibility = Visibility.Collapsed;
    }

    private DataTemplate BuildOwnedBadgesTemplate()
    {
        var template = new DataTemplate();
        var presenter = new FrameworkElementFactory(typeof(ContentControl));
        presenter.SetBinding(ContentControl.ContentProperty, new Binding("GameId") { Converter = new OwnershipBadgesConverter(this) });
        template.VisualTree = presenter;
        return template;
    }

    private FrameworkElement BuildOwnershipBadges(string? gameId)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (string.IsNullOrWhiteSpace(gameId) || !_ownershipByGameId.TryGetValue(gameId, out var counts) || counts.Physical + counts.Digital == 0)
        {
            panel.Children.Add(new TextBlock { Text = "—", Foreground = Brush("#8191A3"), VerticalAlignment = VerticalAlignment.Center });
            return panel;
        }
        if (counts.Physical > 0) panel.Children.Add(Badge($"{counts.Physical} Physical", "#E4F8EE", "#C8EDD9", "#087A46", new Thickness(0, 0, 5, 0)));
        if (counts.Digital > 0) panel.Children.Add(Badge($"{counts.Digital} Digital", "#E7F2FF", "#CAE1F7", "#1264BD", new Thickness(0)));
        return panel;
    }

    private static DataTemplate BuildRegionPillTemplate()
    {
        var template = new DataTemplate();
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Brush("#EEF6FF"));
        border.SetValue(Border.BorderBrushProperty, Brush("#CFE1F2"));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.PaddingProperty, new Thickness(7, 4, 7, 4));
        border.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        var panel = new FrameworkElementFactory(typeof(StackPanel)); panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var image = new FrameworkElementFactory(typeof(Image)); image.SetBinding(Image.SourceProperty, new Binding("RegionFlag")); image.SetValue(FrameworkElement.WidthProperty, 24d); image.SetValue(FrameworkElement.HeightProperty, 16d); image.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0)); panel.AppendChild(image);
        var text = new FrameworkElementFactory(typeof(TextBlock)); text.SetBinding(TextBlock.TextProperty, new Binding("Region")); text.SetValue(TextBlock.ForegroundProperty, Brush("#244A70")); text.SetValue(TextBlock.FontSizeProperty, 11d); text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); panel.AppendChild(text);
        border.AppendChild(panel); template.VisualTree = border; return template;
    }

    private static DataTemplate BuildCollectionRegionPillTemplate()
    {
        var template = new DataTemplate();
        var presenter = new FrameworkElementFactory(typeof(ContentControl));
        presenter.SetBinding(ContentControl.ContentProperty, new Binding("Region") { Converter = new RegionPillConverter() });
        template.VisualTree = presenter;
        return template;
    }

    private static DataTemplate BuildLanguageFlagsTemplate()
    {
        var template = new DataTemplate();
        var items = new FrameworkElementFactory(typeof(ItemsControl)); items.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("LanguageFlags"));
        var panelTemplate = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(StackPanel))); panelTemplate.VisualTree.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal); items.SetValue(ItemsControl.ItemsPanelProperty, panelTemplate);
        var itemTemplate = new DataTemplate(); var image = new FrameworkElementFactory(typeof(Image)); image.SetBinding(Image.SourceProperty, new Binding(".")); image.SetValue(FrameworkElement.WidthProperty, 22d); image.SetValue(FrameworkElement.HeightProperty, 15d); image.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 5, 0)); itemTemplate.VisualTree = image; items.SetValue(ItemsControl.ItemTemplateProperty, itemTemplate); template.VisualTree = items; return template;
    }

    private static DataTemplate BuildReleasePillTemplate()
    {
        var template = new DataTemplate();
        var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.BackgroundProperty, Brush("#F2F5F8")); border.SetValue(Border.BorderBrushProperty, Brush("#D8E1EA")); border.SetValue(Border.BorderThicknessProperty, new Thickness(1)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8)); border.SetValue(Border.PaddingProperty, new Thickness(8, 4, 8, 4)); border.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        var text = new FrameworkElementFactory(typeof(TextBlock)); text.SetBinding(TextBlock.TextProperty, new Binding("ReleaseType")); text.SetValue(TextBlock.ForegroundProperty, Brush("#536A82")); text.SetValue(TextBlock.FontSizeProperty, 11d); border.AppendChild(text); template.VisualTree = border; return template;
    }

    private static DataTemplate BuildCopyTypePillTemplate()
    {
        var template = new DataTemplate();
        var presenter = new FrameworkElementFactory(typeof(ContentControl)); presenter.SetBinding(ContentControl.ContentProperty, new Binding("CopyType") { Converter = new CopyTypePillConverter() }); template.VisualTree = presenter; return template;
    }

    private static DataTemplate BuildSideReleaseTemplate()
    {
        var template = new DataTemplate();
        var outer = new FrameworkElementFactory(typeof(Border)); outer.SetValue(Border.BorderBrushProperty, Brush("#E1E8EF")); outer.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1)); outer.SetValue(Border.PaddingProperty, new Thickness(0, 8, 0, 8));
        // FrameworkElementFactory cannot receive Grid.ColumnDefinitions directly here; use a compact horizontal row.
        var panel = new FrameworkElementFactory(typeof(StackPanel)); panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var regionBorder = new FrameworkElementFactory(typeof(Border)); regionBorder.SetValue(Border.BackgroundProperty, Brush("#EEF6FF")); regionBorder.SetValue(Border.BorderBrushProperty, Brush("#CFE1F2")); regionBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1)); regionBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(8)); regionBorder.SetValue(Border.PaddingProperty, new Thickness(7, 4, 7, 4)); regionBorder.SetValue(FrameworkElement.WidthProperty, 118d);
        var regionPanel = new FrameworkElementFactory(typeof(StackPanel)); regionPanel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var flag = new FrameworkElementFactory(typeof(Image)); flag.SetBinding(Image.SourceProperty, new Binding("RegionFlag")); flag.SetValue(FrameworkElement.WidthProperty, 24d); flag.SetValue(FrameworkElement.HeightProperty, 16d); flag.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0)); regionPanel.AppendChild(flag);
        var regionText = new FrameworkElementFactory(typeof(TextBlock)); regionText.SetBinding(TextBlock.TextProperty, new Binding("Region")); regionText.SetValue(TextBlock.FontSizeProperty, 11d); regionText.SetValue(TextBlock.ForegroundProperty, Brush("#244A70")); regionText.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); regionPanel.AppendChild(regionText); regionBorder.AppendChild(regionPanel); panel.AppendChild(regionBorder);
        var serial = new FrameworkElementFactory(typeof(TextBlock)); serial.SetBinding(TextBlock.TextProperty, new Binding("Serial")); serial.SetValue(FrameworkElement.WidthProperty, 120d); serial.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold); serial.SetValue(TextBlock.ForegroundProperty, Brush("#123E68")); serial.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 4, 5, 0)); serial.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); panel.AppendChild(serial);
        var note = new FrameworkElementFactory(typeof(TextBlock)); note.SetBinding(TextBlock.TextProperty, new Binding("Note")); note.SetValue(TextBlock.ForegroundProperty, Brush("#6A7F95")); note.SetValue(TextBlock.FontSizeProperty, 10.5d); note.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); note.SetValue(FrameworkElement.WidthProperty, 112d); note.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 3, 0, 0)); panel.AppendChild(note);
        outer.AppendChild(panel); template.VisualTree = outer; return template;
    }

    private static StackPanel BuildNoCoverPlaceholder(double iconSize)
    {
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(new TextBlock { Text = "▧", FontSize = iconSize, Foreground = Brush("#9FB0C4"), HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = "No cover available", FontWeight = FontWeights.SemiBold, Foreground = Brush("#60758D"), Margin = new Thickness(0, 7, 0, 0) });
        return panel;
    }

    private static Border SectionDivider() => new() { Height = 1, Background = Brush("#DCE5ED"), Margin = new Thickness(0, 13, 0, 13) };

    private static void AddMetaLabel(Grid grid, int row, string label)
    {
        var block = new TextBlock { Text = label, Foreground = Brush("#5C7189"), FontSize = 12, Margin = new Thickness(0, 4, 8, 4), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(block, row); grid.Children.Add(block);
    }

    private static TextBlock AddMetaTextRow(Grid grid, int row, string label)
    {
        AddMetaLabel(grid, row, label);
        var value = new TextBlock { Text = "—", Foreground = Brush("#17314D"), FontSize = 12, Margin = new Thickness(0, 4, 0, 4), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(value, row); Grid.SetColumn(value, 1); grid.Children.Add(value); return value;
    }

    private static StackPanel AddMetaFlagsRow(Grid grid, int row, string label)
    {
        AddMetaLabel(grid, row, label);
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(panel, row); Grid.SetColumn(panel, 1); grid.Children.Add(panel); return panel;
    }

    private static void PopulateFlags(Panel panel, IReadOnlyList<ImageSource> flags)
    {
        panel.Children.Clear();
        foreach (var source in flags) panel.Children.Add(new Image { Source = source, Width = 25, Height = 17, Stretch = Stretch.Fill, Margin = new Thickness(0, 0, 5, 0) });
        if (panel.Children.Count == 0) panel.Children.Add(new TextBlock { Text = "—", Foreground = Brush("#8191A3") });
    }

    private static Border Badge(string text, string background, string border, string foreground, Thickness margin)
        => new() { Background = Brush(background), BorderBrush = Brush(border), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 4, 7, 4), Margin = margin, Child = new TextBlock { Text = text, Foreground = Brush(foreground), FontSize = 10.5, FontWeight = FontWeights.SemiBold } };

    private static Border BuildRegionPill(string region, ImageSource? flag)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        if (flag is not null) panel.Children.Add(new Image { Source = flag, Width = 24, Height = 16, Stretch = Stretch.Fill, Margin = new Thickness(0, 0, 6, 0) });
        panel.Children.Add(new TextBlock { Text = region, Foreground = Brush("#244A70"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        return new Border { Background = Brush("#EEF6FF"), BorderBrush = Brush("#CFE1F2"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 4, 7, 4), Child = panel };
    }

    private static Border BuildCopyTypePill(string type)
    {
        var physical = string.Equals(type, "Physical", StringComparison.OrdinalIgnoreCase);
        return Badge(type, physical ? "#E4F8EE" : "#E7F2FF", physical ? "#C8EDD9" : "#CAE1F7", physical ? "#087A46" : "#1264BD", new Thickness(0));
    }

    private static Border MiniInfoCard(string label, out TextBlock value)
    {
        value = new TextBlock { Text = "—", Foreground = Brush("#17314D"), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
        var stack = new StackPanel(); stack.Children.Add(new TextBlock { Text = label, Foreground = Brush("#72869B"), FontSize = 10.5 }); stack.Children.Add(value);
        return new Border { Background = Brush("#F8FBFE"), BorderBrush = Brush("#D8E4EF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(10), Child = stack };
    }

    private static Border CompletenessCard(string icon, string title, string subtitle, out TextBlock state)
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var iconBox = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(8), Background = Brush("#E8F3FF"), VerticalAlignment = VerticalAlignment.Top, Child = new TextBlock { Text = icon, Foreground = Brush("#287BD3"), FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        grid.Children.Add(iconBox);
        var text = new StackPanel(); Grid.SetColumn(text, 1); grid.Children.Add(text);
        text.Children.Add(new TextBlock { Text = title, Foreground = Brush("#17314D"), FontWeight = FontWeights.Bold, FontSize = 11.5 });
        text.Children.Add(new TextBlock { Text = subtitle, Foreground = Brush("#74889C"), FontSize = 9.5, TextWrapping = TextWrapping.Wrap });
        state = new TextBlock { Text = "? Unknown", Foreground = Brush("#6D7F91"), FontSize = 10, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 5, 0, 0) }; text.Children.Add(state);
        return new Border { Background = Brush("#F8FBFE"), BorderBrush = Brush("#D8E4EF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(10), MinHeight = 82, Child = grid };
    }

    private static void SetCompletenessCard(Border card, TextBlock state, string? raw)
    {
        var value = (raw ?? string.Empty).Trim();
        var present = value.Equals("Yes", StringComparison.OrdinalIgnoreCase) || value.Equals("Included", StringComparison.OrdinalIgnoreCase) || value.Equals("Present", StringComparison.OrdinalIgnoreCase) || value.Equals("True", StringComparison.OrdinalIgnoreCase) || value == "1";
        var missing = value.Equals("No", StringComparison.OrdinalIgnoreCase) || value.Equals("Missing", StringComparison.OrdinalIgnoreCase) || value.Equals("Absent", StringComparison.OrdinalIgnoreCase) || value.Equals("False", StringComparison.OrdinalIgnoreCase) || value == "0";
        if (present)
        {
            card.Background = Brush("#F0FAF5"); card.BorderBrush = Brush("#BFE8D3"); state.Text = "✓ Included"; state.Foreground = Brush("#087A46");
        }
        else if (missing)
        {
            card.Background = Brush("#FFF7F8"); card.BorderBrush = Brush("#EAC9CD"); state.Text = "✕ Missing"; state.Foreground = Brush("#B93A45");
        }
        else
        {
            card.Background = Brush("#F8FBFE"); card.BorderBrush = Brush("#D8E4EF"); state.Text = "? Unknown"; state.Foreground = Brush("#6D7F91");
        }
    }

    private sealed class OwnershipBadgesConverter(MainWindow owner) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => owner.BuildOwnershipBadges(value?.ToString());
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    private sealed class RegionPillConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var region = value?.ToString() ?? "Unknown";
            return BuildRegionPill(region, FlagIconFactory.ForRegion(region));
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    private sealed class CopyTypePillConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => BuildCopyTypePill(value?.ToString() ?? "—");
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    private sealed record CatalogSideReleaseItem(string Region, ImageSource? RegionFlag, string Serial, string Note, string ReleaseType, string Year, string Publisher, IReadOnlyList<ImageSource> LanguageFlags, bool IsPrimary);
}
