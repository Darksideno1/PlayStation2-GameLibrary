using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Data.Sqlite;

namespace PlayStation2GameLibrary;

internal sealed class DashboardWindow : Window
{
    private readonly string _databasePath;
    private readonly MediaLibraryService _mediaLibrary = new(AppSettingsService.Load());
    private readonly TextBlock _withCover = MetricNumber();
    private readonly TextBlock _withoutCover = MetricNumber();
    private readonly TextBlock _coverage = MetricNumber();
    private readonly TextBlock _legendWithCover = LegendNumber();
    private readonly TextBlock _legendWithoutCover = LegendNumber();
    private readonly TextBlock _legendTotal = LegendNumber();
    private readonly TextBlock _digitalSize = MetricNumber();
    private readonly TextBlock _digitalCount = new() { FontSize = 13, Foreground = Brush("#506980") };
    private readonly CoverageRing _ring = new();
    private readonly ObservableCollection<MissingCoverRow> _missing = new();
    private readonly TextBlock _status = new() { Foreground = Brush("#60758D"), FontSize = 11, TextWrapping = TextWrapping.Wrap };

    public DashboardWindow(string databasePath)
    {
        _databasePath = databasePath;
        Title = "Dashboard — PLAYSTATION 2 GAME LIBRARY";
        Width = 1240;
        Height = 780;
        MinWidth = 940;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#EEF5FB");
        FontFamily = new FontFamily("Segoe UI");

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Content = scroll;
        var root = new StackPanel { Margin = new Thickness(22) };
        scroll.Content = root;
        root.Children.Add(new TextBlock { Text = "Dashboard", FontSize = 30, FontWeight = FontWeights.Bold, Foreground = Brush("#10243F") });
        root.Children.Add(new TextBlock { Text = "Overview of your PlayStation 2 game library and front cover coverage.", Foreground = Brush("#60758D"), Margin = new Thickness(0, 5, 0, 18) });

        var metrics = new UniformGrid { Columns = 3 };
        root.Children.Add(metrics);
        metrics.Children.Add(MetricCard("▧", "Games with cover", _withCover, "Local front cover available", "#E8F7EF", "#1E9F61"));
        metrics.Children.Add(MetricCard("⊘", "Games without cover", _withoutCover, "Missing local front cover", "#FFF0F1", "#D94752"));
        metrics.Children.Add(MetricCard("%", "Coverage", _coverage, "One cover per conceptual game", "#E8F3FF", "#287BD3"));

        var middle = new Grid { Margin = new Thickness(0, 14, 0, 14) };
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.85, GridUnitType.Star) });
        root.Children.Add(middle);

        var coverageCard = Card();
        middle.Children.Add(coverageCard);
        var coveragePanel = new StackPanel();
        coverageCard.Child = coveragePanel;
        coveragePanel.Children.Add(new TextBlock { Text = "Cover Coverage", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Brush("#17314D") });
        coveragePanel.Children.Add(new TextBlock { Text = "Based on local front cover files in the configured Media Library.", Foreground = Brush("#70849A"), FontSize = 12, Margin = new Thickness(0, 3, 0, 8) });

        var coverageGrid = new Grid();
        coverageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });
        coverageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        coveragePanel.Children.Add(coverageGrid);
        _ring.Width = 245;
        _ring.Height = 245;
        _ring.Margin = new Thickness(10);
        coverageGrid.Children.Add(_ring);
        var legend = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) };
        Grid.SetColumn(legend, 1);
        coverageGrid.Children.Add(legend);
        legend.Children.Add(LegendRow("Games with cover", _legendWithCover));
        legend.Children.Add(LegendRow("Games without cover", _legendWithoutCover));
        legend.Children.Add(LegendRow("Total games", _legendTotal));

        var digitalCard = Card();
        digitalCard.Margin = new Thickness(14, 0, 0, 0);
        Grid.SetColumn(digitalCard, 1);
        middle.Children.Add(digitalCard);
        var digitalPanel = new StackPanel();
        digitalCard.Child = digitalPanel;
        var digitalHead = new Grid();
        digitalHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        digitalHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        digitalPanel.Children.Add(digitalHead);
        digitalHead.Children.Add(new Border
        {
            Width = 62,
            Height = 62,
            Background = Brush("#E8F3FF"),
            CornerRadius = new CornerRadius(13),
            Child = new TextBlock { Text = "▰", FontSize = 29, Foreground = Brush("#287BD3"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        });
        var digitalText = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(digitalText, 1);
        digitalHead.Children.Add(digitalText);
        digitalText.Children.Add(new TextBlock { Text = "Total Digital Size", Foreground = Brush("#506980"), FontSize = 13 });
        _digitalSize.Foreground = Brush("#176AC7");
        digitalText.Children.Add(_digitalSize);
        _digitalCount.Margin = new Thickness(0, 15, 0, 0);
        digitalPanel.Children.Add(_digitalCount);
        digitalPanel.Children.Add(new Border
        {
            Margin = new Thickness(0, 14, 0, 0),
            Padding = new Thickness(12),
            Background = Brush("#EAF5FF"),
            CornerRadius = new CornerRadius(10),
            Child = new TextBlock { Text = "Total Digital Size is calculated automatically from saved Digital collection entries.", Foreground = Brush("#355D83"), FontSize = 11, TextWrapping = TextWrapping.Wrap }
        });

        var missingCard = Card();
        root.Children.Add(missingCard);
        var missingPanel = new DockPanel();
        missingCard.Child = missingPanel;
        var missingHead = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(missingHead, Dock.Top);
        missingPanel.Children.Add(missingHead);
        missingHead.Children.Add(new TextBlock { Text = "Games Without Covers", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Brush("#17314D") });
        missingHead.Children.Add(new TextBlock { Text = "A sample of catalog games whose primary serial has no local front cover.", Foreground = Brush("#70849A"), FontSize = 12, Margin = new Thickness(0, 3, 0, 0) });
        var grid = new DataGrid
        {
            ItemsSource = _missing,
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            BorderThickness = new Thickness(0),
            Height = 235
        };
        grid.Columns.Add(new DataGridTextColumn { Header = "ID", Binding = new System.Windows.Data.Binding("Serial"), Width = 130 });
        grid.Columns.Add(new DataGridTextColumn { Header = "TITLE", Binding = new System.Windows.Data.Binding("Title"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "REGION", Binding = new System.Windows.Data.Binding("Region"), Width = 120 });
        missingPanel.Children.Add(grid);
        _status.Margin = new Thickness(0, 10, 0, 0);
        root.Children.Add(_status);

        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _status.Text = "Calculating cover coverage…";
            await using var db = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly");
            await db.OpenAsync();

            var games = new List<GameCoverKey>();
            await using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = @"SELECT g.GameId,g.CanonicalTitle,
COALESCE((SELECT d.SerialRaw FROM Release r LEFT JOIN Disc d ON d.ReleaseId=r.ReleaseId WHERE r.GameId=g.GameId ORDER BY r.IsPrimary DESC,d.DiscNumber LIMIT 1),''),
COALESCE((SELECT r.RegionCode FROM Release r WHERE r.GameId=g.GameId ORDER BY r.IsPrimary DESC LIMIT 1),'Unknown')
FROM Game g ORDER BY g.SortTitle COLLATE NOCASE";
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    games.Add(new GameCoverKey(reader.GetString(0), reader.GetString(1), reader.GetString(2), FriendlyRegion(reader.GetString(3))));
            }

            var coverKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unmatched = 0;
            if (Directory.Exists(_mediaLibrary.CoversDirectory))
            {
                var known = new HashSet<string>(games.Select(g => g.Serial).Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.OrdinalIgnoreCase);
                foreach (var file in Directory.EnumerateFiles(_mediaLibrary.CoversDirectory))
                {
                    var extension = Path.GetExtension(file).ToLowerInvariant();
                    if (extension is not ".jpg" and not ".jpeg" and not ".png" and not ".webp") continue;
                    var key = Path.GetFileNameWithoutExtension(file);
                    if (known.Contains(key)) coverKeys.Add(key); else unmatched++;
                }
            }

            var covered = games.Count(g => !string.IsNullOrWhiteSpace(g.Serial) && coverKeys.Contains(g.Serial));
            var missing = games.Count - covered;
            var percent = games.Count == 0 ? 0 : covered * 100d / games.Count;
            _withCover.Text = covered.ToString("N0");
            _withoutCover.Text = missing.ToString("N0");
            _coverage.Text = $"{percent:0.0}%";
            _legendWithCover.Text = $"{covered:N0}  ({percent:0.0}%)";
            _legendWithoutCover.Text = $"{missing:N0}  ({100d - percent:0.0}%)";
            _legendTotal.Text = games.Count.ToString("N0");
            _ring.Percentage = percent;
            _ring.CenterPrimary = $"{percent:0.0}%";
            _ring.CenterSecondary = $"{covered:N0} of {games.Count:N0} games";

            await using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*),COALESCE(SUM(DigitalSizeBytes),0) FROM CollectionCopy WHERE CopyType='Digital'";
                await using var reader = await cmd.ExecuteReaderAsync();
                await reader.ReadAsync();
                var digitalCount = reader.GetInt64(0);
                var digitalBytes = reader.GetInt64(1);
                _digitalSize.Text = FormatBytes(digitalBytes);
                _digitalCount.Text = $"Digital games: {digitalCount:N0}";
            }

            _missing.Clear();
            foreach (var item in games.Where(g => string.IsNullOrWhiteSpace(g.Serial) || !coverKeys.Contains(g.Serial)).Take(12))
                _missing.Add(new MissingCoverRow(item.Serial.Length == 0 ? "—" : item.Serial, item.Title, item.Region));

            _status.Text = $"Media Library: {_mediaLibrary.CoversDirectory}  •  Unmatched cover files: {unmatched:N0}";
        }
        catch (Exception ex)
        {
            _status.Text = "Dashboard could not be calculated.";
            MessageBox.Show(this, ex.Message, "Dashboard error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static TextBlock MetricNumber() => new() { Text = "—", FontSize = 29, FontWeight = FontWeights.Bold, Foreground = Brush("#10243F") };
    private static TextBlock LegendNumber() => new() { Text = "—", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brush("#17314D") };
    private static Brush Brush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
    private static Border Card() => new() { Background = Brushes.White, BorderBrush = Brush("#D3E1EE"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(15), Padding = new Thickness(18) };

    private static Border MetricCard(string glyph, string label, TextBlock value, string note, string iconBackground, string iconForeground)
    {
        var card = Card();
        card.Margin = new Thickness(0, 0, 12, 0);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        card.Child = grid;
        grid.Children.Add(new Border
        {
            Width = 54,
            Height = 54,
            CornerRadius = new CornerRadius(11),
            Background = Brush(iconBackground),
            Child = new TextBlock { Text = glyph, Foreground = Brush(iconForeground), FontSize = 24, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        });
        var stack = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);
        stack.Children.Add(new TextBlock { Text = label, Foreground = Brush("#455F78"), FontSize = 12 });
        stack.Children.Add(value);
        stack.Children.Add(new TextBlock { Text = note, Foreground = Brush("#70849A"), FontSize = 10, TextWrapping = TextWrapping.Wrap });
        return card;
    }

    private static Border LegendRow(string label, TextBlock value)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = label, Foreground = Brush("#455F78") });
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
        return new Border { BorderBrush = Brush("#EDF2F7"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 7, 0, 7), Child = grid };
    }

    private static string FormatBytes(long bytes) => bytes >= 1_000_000_000_000L ? $"{bytes / 1_000_000_000_000d:0.##} TB" : $"{bytes / 1_000_000_000d:0.##} GB";

    private static string FriendlyRegion(string region)
    {
        var value = region.ToLowerInvariant();
        if (value.Contains("north") || value.Contains("usa") || value.Contains("ntsc-u")) return "NTSC-U/C";
        if (value.Contains("japan") || value.Contains("ntsc-j")) return "NTSC-J";
        if (value.Contains("europe") || value.Contains("pal") || value.Contains("australia")) return "PAL";
        return region;
    }

    private sealed record GameCoverKey(string GameId, string Title, string Serial, string Region);
    private sealed record MissingCoverRow(string Serial, string Title, string Region);

    private sealed class CoverageRing : FrameworkElement
    {
        private double _percentage;
        private string _centerPrimary = "0%";
        private string _centerSecondary = "0 games";

        public double Percentage { get => _percentage; set { _percentage = Math.Clamp(value, 0, 100); InvalidateVisual(); } }
        public string CenterPrimary { get => _centerPrimary; set { _centerPrimary = value; InvalidateVisual(); } }
        public string CenterSecondary { get => _centerSecondary; set { _centerSecondary = value; InvalidateVisual(); } }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            var size = Math.Min(ActualWidth, ActualHeight);
            var center = new Point(ActualWidth / 2, ActualHeight / 2);
            var radius = Math.Max(10, size / 2 - 22);
            const double thickness = 21d;
            dc.DrawEllipse(null, new Pen(Brush("#DFE8F0"), thickness), center, radius, radius);

            if (Percentage >= 99.999)
            {
                dc.DrawEllipse(null, new Pen(Brush("#2E83E5"), thickness), center, radius, radius);
            }
            else if (Percentage > 0.01)
            {
                const double start = -90d;
                var end = start + Percentage * 3.6;
                var startPoint = PointOnCircle(center, radius, start);
                var endPoint = PointOnCircle(center, radius, end);
                var figure = new PathFigure { StartPoint = startPoint, IsClosed = false };
                figure.Segments.Add(new ArcSegment(endPoint, new Size(radius, radius), 0, Percentage > 50, SweepDirection.Clockwise, true));
                dc.DrawGeometry(null, new Pen(Brush("#2E83E5"), thickness), new PathGeometry(new[] { figure }));
            }

            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var family = new FontFamily("Segoe UI");
            var primary = new FormattedText(CenterPrimary, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(family, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 28, Brush("#10243F"), dpi);
            dc.DrawText(primary, new Point(center.X - primary.Width / 2, center.Y - primary.Height));
            var secondary = new FormattedText(CenterSecondary, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 11, Brush("#60758D"), dpi);
            dc.DrawText(secondary, new Point(center.X - secondary.Width / 2, center.Y + 12));
        }

        private static Point PointOnCircle(Point center, double radius, double degrees)
        {
            var radians = degrees * Math.PI / 180d;
            return new Point(center.X + Math.Cos(radians) * radius, center.Y + Math.Sin(radians) * radius);
        }
    }
}
