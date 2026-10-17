using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PlayStation2GameLibrary;

public partial class MainWindow
{
    static MainWindow()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(MainWindowLoadedForBadgePolish), true);
        EventManager.RegisterClassHandler(typeof(Image), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(LanguageFlagImageLoaded), true);
    }

    private static void MainWindowLoadedForBadgePolish(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window)) return;
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(window.ApplyEnhancedBadgePolish));
    }

    private void ApplyEnhancedBadgePolish()
    {
        // MainWindow.VisualPolish creates these six columns during ContentRendered.
        // Replacing only the templates keeps the catalogue logic intact while bringing
        // the visual language closer to the approved HTML v11 prototype.
        if (GamesGrid.Columns.Count >= 6)
        {
            GamesGrid.Columns[2] = new DataGridTemplateColumn
            {
                Header = "REGION", Width = 126, MinWidth = 112, CellTemplate = BuildEnhancedRegionTemplate()
            };
            GamesGrid.Columns[3] = new DataGridTemplateColumn
            {
                Header = "LANGUAGES", Width = 158, MinWidth = 128, CellTemplate = BuildEnhancedLanguageTemplate()
            };
            GamesGrid.Columns[4] = new DataGridTemplateColumn
            {
                Header = "RELEASE", Width = 104, MinWidth = 92, CellTemplate = BuildEnhancedReleaseTemplate()
            };
            GamesGrid.Columns[5] = new DataGridTemplateColumn
            {
                Header = "OWNED", Width = 190, MinWidth = 150, CellTemplate = BuildEnhancedOwnedTemplate()
            };
        }

        if (CollectionGrid.Columns.Count >= 3)
        {
            CollectionGrid.Columns[1] = new DataGridTemplateColumn
            {
                Header = "TYPE", Width = 110, CellTemplate = BuildEnhancedCopyTypeTemplate()
            };
            CollectionGrid.Columns[2] = new DataGridTemplateColumn
            {
                Header = "REGION", Width = 128, CellTemplate = BuildEnhancedCollectionRegionTemplate()
            };
        }
    }

    private static DataTemplate BuildEnhancedRegionTemplate()
    {
        var template = new DataTemplate();
        var presenter = new FrameworkElementFactory(typeof(ContentControl));
        presenter.SetBinding(ContentControl.ContentProperty,
            new Binding("Region") { Converter = new EnhancedRegionPillConverter() });
        template.VisualTree = presenter;
        return template;
    }

    private static DataTemplate BuildEnhancedCollectionRegionTemplate()
    {
        var template = new DataTemplate();
        var presenter = new FrameworkElementFactory(typeof(ContentControl));
        presenter.SetBinding(ContentControl.ContentProperty,
            new Binding("Region") { Converter = new EnhancedRegionPillConverter() });
        template.VisualTree = presenter;
        return template;
    }

    private static DataTemplate BuildEnhancedReleaseTemplate()
    {
        var template = new DataTemplate();
        var presenter = new FrameworkElementFactory(typeof(ContentControl));
        presenter.SetBinding(ContentControl.ContentProperty,
            new Binding("ReleaseType") { Converter = new EnhancedReleasePillConverter() });
        template.VisualTree = presenter;
        return template;
    }

    private DataTemplate BuildEnhancedOwnedTemplate()
    {
        var template = new DataTemplate();
        var presenter = new FrameworkElementFactory(typeof(ContentControl));
        presenter.SetBinding(ContentControl.ContentProperty,
            new Binding("GameId") { Converter = new EnhancedOwnershipPillConverter(this) });
        template.VisualTree = presenter;
        return template;
    }

    private static DataTemplate BuildEnhancedCopyTypeTemplate()
    {
        var template = new DataTemplate();
        var presenter = new FrameworkElementFactory(typeof(ContentControl));
        presenter.SetBinding(ContentControl.ContentProperty,
            new Binding("CopyType") { Converter = new EnhancedCopyTypePillConverter() });
        template.VisualTree = presenter;
        return template;
    }

    private static DataTemplate BuildEnhancedLanguageTemplate()
    {
        var template = new DataTemplate();
        var items = new FrameworkElementFactory(typeof(ItemsControl));
        items.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("LanguageFlags"));

        var panelFactory = new FrameworkElementFactory(typeof(StackPanel));
        panelFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        panelFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        items.SetValue(ItemsControl.ItemsPanelProperty, new ItemsPanelTemplate(panelFactory));

        var itemTemplate = new DataTemplate();
        var image = new FrameworkElementFactory(typeof(Image));
        image.SetBinding(Image.SourceProperty, new Binding("."));
        image.SetBinding(FrameworkElement.ToolTipProperty,
            new Binding(".") { Converter = new LanguageFlagTooltipConverter() });
        image.SetValue(FrameworkElement.WidthProperty, 23d);
        image.SetValue(FrameworkElement.HeightProperty, 16d);
        image.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
        image.SetValue(Image.StretchProperty, Stretch.Fill);
        itemTemplate.VisualTree = image;
        items.SetValue(ItemsControl.ItemTemplateProperty, itemTemplate);
        template.VisualTree = items;
        return template;
    }

    private FrameworkElement BuildEnhancedOwnershipPills(string? gameId)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (string.IsNullOrWhiteSpace(gameId) || !_ownershipByGameId.TryGetValue(gameId, out var counts) || counts.Physical + counts.Digital == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "—", Foreground = Brush("#8191A3"), VerticalAlignment = VerticalAlignment.Center
            });
            return panel;
        }

        if (counts.Physical > 0)
            panel.Children.Add(CreateSoftPill($"{counts.Physical} Physical", "#EFFAF4", "#B9E3CC", "#087A46", new Thickness(0, 0, 6, 0), "#27A96B"));
        if (counts.Digital > 0)
            panel.Children.Add(CreateSoftPill($"{counts.Digital} Digital", "#EFF7FF", "#C2DCF3", "#1264BD", new Thickness(0), "#3B86D9"));
        return panel;
    }

    private static Border CreateSoftPill(string text, string background, string border, string foreground, Thickness margin, string dot)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock
        {
            Text = "●", FontSize = 7.5, Foreground = Brush(dot), VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });
        panel.Children.Add(new TextBlock
        {
            Text = text, Foreground = Brush(foreground), FontSize = 10.5,
            FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center
        });
        return new Border
        {
            Background = Brush(background), BorderBrush = Brush(border), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11), Padding = new Thickness(9, 4, 9, 4), Margin = margin,
            MinHeight = 27, VerticalAlignment = VerticalAlignment.Center, Child = panel
        };
    }

    private static Border CreateRegionPill(string region)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var flag = FlagIconFactory.ForRegion(region);
        if (flag is not null)
            panel.Children.Add(new Image
            {
                Source = flag, Width = 23, Height = 15, Stretch = Stretch.Fill,
                Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center
            });
        panel.Children.Add(new TextBlock
        {
            Text = region, Foreground = Brush("#244A70"), FontSize = 10.5,
            FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center
        });
        return new Border
        {
            Background = Brush("#F4F9FF"), BorderBrush = Brush("#BED7ED"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11), Padding = new Thickness(9, 4, 9, 4), MinHeight = 27,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Child = panel
        };
    }

    private static Border CreateReleasePill(string releaseType)
    {
        var isPlatinum = releaseType.Contains("Platinum", StringComparison.OrdinalIgnoreCase);
        return CreateSoftPill(
            string.IsNullOrWhiteSpace(releaseType) ? "Retail" : releaseType,
            isPlatinum ? "#F7F3FF" : "#F6F8FA",
            isPlatinum ? "#D9CDEE" : "#D6DFE8",
            isPlatinum ? "#6E4F95" : "#536A82",
            new Thickness(0),
            isPlatinum ? "#8A68B3" : "#8294A6");
    }

    private static Border CreateCopyTypePill(string type)
    {
        var physical = string.Equals(type, "Physical", StringComparison.OrdinalIgnoreCase);
        return CreateSoftPill(type,
            physical ? "#EFFAF4" : "#EFF7FF",
            physical ? "#B9E3CC" : "#C2DCF3",
            physical ? "#087A46" : "#1264BD",
            new Thickness(0), physical ? "#27A96B" : "#3B86D9");
    }

    private static void LanguageFlagImageLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Image image || image.Source is null || image.ToolTip is not null) return;
        // Region flags are 23/24 x 15/16 in the UI. Language flags use the other
        // established sizes (22x15, 25x17, 27x18), so avoid labelling region flags.
        if ((Math.Abs(image.Width - 23) < .1 && Math.Abs(image.Height - 15) < .1) ||
            (Math.Abs(image.Width - 24) < .1 && Math.Abs(image.Height - 16) < .1)) return;
        var language = LanguageNameFromFlag(image.Source);
        if (language is not null) image.ToolTip = language;
    }

    private static string? LanguageNameFromFlag(ImageSource? source)
    {
        if (source is null) return null;
        var languages = new (string Code, string Name)[]
        {
            ("en", "English"), ("fr", "French"), ("de", "German"),
            ("it", "Italian"), ("es", "Spanish"), ("ja", "Japanese")
        };
        foreach (var item in languages)
            if (ReferenceEquals(source, FlagIconFactory.ForLanguage(item.Code))) return item.Name;
        return null;
    }

    private sealed class EnhancedRegionPillConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => CreateRegionPill(value?.ToString() ?? "—");
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    private sealed class EnhancedReleasePillConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => CreateReleasePill(value?.ToString() ?? "Retail");
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    private sealed class EnhancedCopyTypePillConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => CreateCopyTypePill(value?.ToString() ?? "Physical");
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    private sealed class EnhancedOwnershipPillConverter : IValueConverter
    {
        private readonly MainWindow _owner;
        public EnhancedOwnershipPillConverter(MainWindow owner) => _owner = owner;
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => _owner.BuildEnhancedOwnershipPills(value?.ToString());
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    private sealed class LanguageFlagTooltipConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is ImageSource source ? LanguageNameFromFlag(source) : null;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
