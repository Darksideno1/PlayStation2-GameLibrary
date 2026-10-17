using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace PlayStation2GameLibrary;

internal static class CatalogSelectionStatusBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(typeof(DataGrid), Selector.SelectionChangedEvent,
            new SelectionChangedEventHandler(OnSelectionChanged), true);
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded), true);
    }

    private static void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not DataGrid { Name: "GamesGrid" } grid || Window.GetWindow(grid) is not MainWindow window) return;
        window.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(window.SanitizeCatalogCoverStatus));
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window)) return;
        window.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(window.SanitizeCatalogCoverStatus));
    }
}

public partial class MainWindow
{
    internal void SanitizeCatalogCoverStatus()
    {
        const string marker = "No local front cover found for ";
        var value = StatusText.Text ?? string.Empty;
        var index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index >= 0) StatusText.Text = value[..index] + "No local front cover found.";
    }
}
