using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace PlayStation2GameLibrary;

internal static class ConceptualCatalogGridBootstrap
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

        // MainWindow.VisualPolish calls base.OnContentRendered() first and only then
        // rebuilds the legacy catalogue columns. Queue our rebuild so it runs after
        // the entire OnContentRendered override has returned.
        window.ContentRendered += (_, _) =>
            window.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(window.ApplyConceptualCatalogGrid));
        window.Dispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(window.ApplyConceptualCatalogGrid));
    }
}

public partial class MainWindow
{
    private bool _conceptualCatalogGridApplied;

    internal void ApplyConceptualCatalogGrid()
    {
        GamesGrid.RowHeight = double.NaN;
        GamesGrid.MinRowHeight = 48;
        GamesGrid.Columns.Clear();
        GamesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "TITLE", Binding = new Binding("Title"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 240
        });
        GamesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "YEAR", Binding = new Binding("Year"), Width = 70, MinWidth = 62
        });
        GamesGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "REGIONS", Width = 185, MinWidth = 150, CellTemplate = AggregateTemplate(AggregateCellKind.Regions)
        });
        GamesGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "LANGUAGES", Width = 215, MinWidth = 165, CellTemplate = AggregateTemplate(AggregateCellKind.Languages)
        });
        GamesGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "RELEASE TYPES", Width = 150, MinWidth = 125, CellTemplate = AggregateTemplate(AggregateCellKind.ReleaseTypes)
        });
        GamesGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "OWNED", Width = 180, MinWidth = 145, CellTemplate = BuildEnhancedOwnedTemplate()
        });

        if (!_conceptualCatalogGridApplied)
        {
            _conceptualCatalogGridApplied = true;
            GamesGrid.SelectionChanged += async (_, _) =>
            {
                if (GamesGrid.SelectedItem is GameListItem selected)
                    await UpdateConceptualCatalogSideAsync(selected);
            };
        }

        if (GamesGrid.SelectedItem is GameListItem current)
            _ = UpdateConceptualCatalogSideAsync(current);
    }

    private DataTemplate AggregateTemplate(AggregateCellKind kind)
    {
        var template = new DataTemplate();
        var presenter = new FrameworkElementFactory(typeof(ContentControl));
        presenter.SetBinding(ContentControl.ContentProperty,
            new Binding("GameId") { Converter = new CatalogAggregateConverter(this, kind) });
        presenter.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 3, 0, 3));
        template.VisualTree = presenter;
        return template;
    }

    private FrameworkElement BuildCatalogAggregateCell(string? gameId, AggregateCellKind kind)
    {
        var wrap = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 1, 0, 1)
        };

        if (string.IsNullOrWhiteSpace(gameId) || !_catalogAggregateByGameId.TryGetValue(gameId, out var aggregate))
        {
            wrap.Children.Add(new TextBlock { Text = "—", Foreground = Brush("#8191A3") });
            return wrap;
        }

        switch (kind)
        {
            case AggregateCellKind.Regions:
                foreach (var region in aggregate.Regions)
                {
                    var pill = CreateRegionPill(region);
                    pill.Margin = new Thickness(0, 0, 5, 4);
                    wrap.Children.Add(pill);
                }
                break;

            case AggregateCellKind.Languages:
                foreach (var language in aggregate.Languages)
                    wrap.Children.Add(CreateCatalogLanguageVisual(language));
                break;

            case AggregateCellKind.ReleaseTypes:
                foreach (var releaseType in aggregate.ReleaseTypes)
                {
                    var pill = CreateReleasePill(releaseType);
                    pill.Margin = new Thickness(0, 0, 5, 4);
                    wrap.Children.Add(pill);
                }
                break;
        }

        if (wrap.Children.Count == 0)
            wrap.Children.Add(new TextBlock { Text = "—", Foreground = Brush("#8191A3") });
        return wrap;
    }

    private static FrameworkElement CreateCatalogLanguageVisual(string language)
    {
        var flag = FlagIconFactory.ForLanguage(language);
        if (flag is not null)
        {
            var image = new Image
            {
                Source = flag,
                Width = 25,
                Height = 17,
                Stretch = Stretch.Fill,
                Margin = new Thickness(0, 1, 6, 4),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = language
            };
            ToolTipService.SetInitialShowDelay(image, 200);
            ToolTipService.SetShowDuration(image, 10000);
            return image;
        }

        return CreateSoftPill(language, "#F6F8FA", "#D6DFE8", "#536A82", new Thickness(0, 0, 5, 4), "#8294A6");
    }

    private async Task UpdateConceptualCatalogSideAsync(GameListItem selected)
    {
        // Keep the preferred serial internally for cover lookup, but never present it as the ID of the conceptual game.
        if (_catalogVisualSerial is null || !_catalogAggregateByGameId.TryGetValue(selected.GameId, out var aggregate)) return;

        if (_catalogVisualSerial.Parent is Grid meta)
        {
            foreach (var label in meta.Children.OfType<TextBlock>())
            {
                if (Grid.GetRow(label) == 0 && Grid.GetColumn(label) == 0)
                {
                    label.Text = "Linked releases:";
                    break;
                }
            }
        }
        _catalogVisualSerial.Text = aggregate.ReleaseCount == 1 ? "1 release" : $"{aggregate.ReleaseCount} releases";

        if (_catalogVisualLanguages is not null)
        {
            _catalogVisualLanguages.Children.Clear();
            foreach (var language in aggregate.Languages)
                _catalogVisualLanguages.Children.Add(CreateCatalogLanguageVisual(language));
            if (_catalogVisualLanguages.Children.Count == 0)
                _catalogVisualLanguages.Children.Add(new TextBlock { Text = "—", Foreground = Brush("#8191A3") });
        }

        await Task.CompletedTask;
    }

    private enum AggregateCellKind { Regions, Languages, ReleaseTypes }

    private sealed class CatalogAggregateConverter : IValueConverter
    {
        private readonly MainWindow _owner;
        private readonly AggregateCellKind _kind;

        public CatalogAggregateConverter(MainWindow owner, AggregateCellKind kind)
        {
            _owner = owner;
            _kind = kind;
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => _owner.BuildCatalogAggregateCell(value?.ToString(), _kind);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
