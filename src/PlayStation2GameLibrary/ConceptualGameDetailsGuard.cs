using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PlayStation2GameLibrary;

internal static class ConceptualGameDetailsGuardBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(typeof(TextBlock), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnTextLoaded), true);
        EventManager.RegisterClassHandler(typeof(Border), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnBorderLoaded), true);
    }

    private static void OnTextLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBlock text || FindMainWindow(text) is not MainWindow window) return;
        window.AttachConceptualGameDetailsGuard(text);
    }

    private static void OnBorderLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Border { Name: "InlinePrimaryRegionPill" } border && FindMainWindow(border) is MainWindow)
            border.Visibility = Visibility.Collapsed;
    }

    private static MainWindow? FindMainWindow(DependencyObject start)
    {
        DependencyObject? current = start;
        while (current is not null)
        {
            if (current is MainWindow window) return window;
            current = LogicalTreeHelper.GetParent(current) ?? VisualParent(current);
        }
        return null;
    }

    private static DependencyObject? VisualParent(DependencyObject value)
    {
        try { return VisualTreeHelper.GetParent(value); }
        catch { return null; }
    }
}

public partial class MainWindow
{
    private readonly HashSet<TextBlock> _conceptualDetailsGuardedText = new();
    private bool _conceptualDetailsGuardUpdating;

    internal void AttachConceptualGameDetailsGuard(TextBlock text)
    {
        if (text.Text is "Primary release" or "Primary serial")
            text.Text = "Linked releases";

        if (text.Name is not ("InlineCollectionPrimary" or "InlinePrimarySerial" or "InlineQuickSerial")) return;
        if (!_conceptualDetailsGuardedText.Add(text)) return;

        var descriptor = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
        descriptor?.AddValueChanged(text, (_, _) => NormalizeConceptualDetailsValue(text));
        NormalizeConceptualDetailsValue(text);
    }

    private void NormalizeConceptualDetailsValue(TextBlock text)
    {
        if (_conceptualDetailsGuardUpdating || PageTitle.Text != "Game Details") return;
        if (_inlineReleases.Count == 0)
        {
            if (LooksLikePs2Serial(text.Text)) text.Text = "—";
            return;
        }

        var desired = _inlineReleases.Count == 1 ? "1 release" : $"{_inlineReleases.Count} releases";
        if (text.Text == desired) return;

        _conceptualDetailsGuardUpdating = true;
        try { text.Text = desired; }
        finally { _conceptualDetailsGuardUpdating = false; }
    }

    private static bool LooksLikePs2Serial(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim().ToUpperInvariant();
        return text.Contains('-') && text.Any(char.IsDigit)
            && new[] { "SLES", "SLUS", "SCES", "SCUS", "SLPM", "SLPS", "SCPS", "SLKA", "SCAJ", "SCKA", "SLAJ", "SLED" }
                .Any(text.StartsWith);
    }
}
