using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace PlayStation2GameLibrary;

internal static class ConceptualStatusGuardBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(typeof(TextBlock), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded), true);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBlock { Name: "StatusText" } text || Window.GetWindow(text) is not MainWindow) return;
        var descriptor = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
        descriptor?.AddValueChanged(text, (_, _) => Sanitize(text));
        Sanitize(text);
    }

    private static void Sanitize(TextBlock text)
    {
        const string marker = "No local front cover found for ";
        var value = text.Text ?? string.Empty;
        var index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return;

        var replacement = value[..index] + "No local front cover found.";
        if (!string.Equals(value, replacement, StringComparison.Ordinal)) text.Text = replacement;
    }
}
