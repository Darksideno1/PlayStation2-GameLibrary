using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace PlayStation2GameLibrary;

internal static class GameDetailsPolishBootstrap
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
        window.EnableGameDetailsVisualPolish();
    }
}

public partial class MainWindow
{
    private Grid? _lastPolishedDetailsPage;

    internal void EnableGameDetailsVisualPolish()
    {
        LayoutUpdated += (_, _) => PolishInlineGameDetailsIfNeeded();
    }

    private void PolishInlineGameDetailsIfNeeded()
    {
        if (_inlineGameDetailsPage is null || !string.Equals(PageTitle.Text, "Game Details", StringComparison.Ordinal)) return;
        if (ReferenceEquals(_lastPolishedDetailsPage, _inlineGameDetailsPage)) return;
        _lastPolishedDetailsPage = _inlineGameDetailsPage;

        var cover = FindNamed<Image>(_inlineGameDetailsPage, "InlineCoverImage");
        if (cover?.Parent is Grid coverGrid && coverGrid.Parent is Border coverFrame)
        {
            coverFrame.Width = 230;
            coverFrame.Height = 310;
        }

        var overview = FindNamed<StackPanel>(_inlineGameDetailsPage, "InlineOverviewPanel");
        if (overview is not null)
        {
            foreach (var row in overview.Children.OfType<Grid>())
                row.Margin = new Thickness(0, 0, 0, 5);
        }

        var quickSerial = FindNamed<TextBlock>(_inlineGameDetailsPage, "InlineQuickSerial");
        if (quickSerial?.Parent is Grid firstQuickRow && firstQuickRow.Parent is StackPanel quickStack && quickStack.Parent is Border quickCard)
        {
            quickCard.Padding = new Thickness(12, 10, 12, 10);
            foreach (var row in quickStack.Children.OfType<Grid>())
                row.Margin = new Thickness(0, 0, 0, 5);
            var heading = quickStack.Children.OfType<TextBlock>().FirstOrDefault(x => x.Text == "Quick Facts");
            if (heading is not null) heading.Margin = new Thickness(0, 0, 0, 7);
        }
    }
}
