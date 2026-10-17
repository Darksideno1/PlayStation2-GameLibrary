using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace PlayStation2GameLibrary;

/// <summary>
/// Routes every main-window "+ Add to Collection" action to the complete
/// one-step entry dialog, before the older collection-manager handler runs.
/// </summary>
internal static class CompleteAddToCollectionBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            Button.ClickEvent,
            new RoutedEventHandler(HandleAddToCollectionClick),
            true);
    }

    private static async void HandleAddToCollectionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        if (Window.GetWindow(button) is not MainWindow main) return;
        var label = button.Content?.ToString()?.Replace("  ", " ").Trim();
        if (!string.Equals(label, "+ Add to Collection", StringComparison.OrdinalIgnoreCase)) return;

        // Class handlers run before the instance Click handlers. Marking the routed
        // event handled prevents the legacy two-step CollectionWindow from opening.
        e.Handled = true;
        await main.OpenCompleteAddToCollectionAsync();
    }
}

public partial class MainWindow
{
    internal async Task OpenCompleteAddToCollectionAsync()
    {
        if (string.IsNullOrWhiteSpace(_databasePath)) return;
        if (GamesGrid.SelectedItem is not GameListItem selected)
        {
            MessageBox.Show(this, "Select a game first.", "Add to Collection", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new AddCollectionEntryWindow(_databasePath, selected.GameId) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        await RefreshGamesAsync();
        await RefreshFooterAsync();
        await RefreshCollectionPageAsync();

        if (_inlineGameDetailsPage is not null && string.Equals(PageTitle.Text, "Game Details", StringComparison.Ordinal))
            await LoadInlineGameDetailsAsync(selected.GameId, _inlineGameDetailsPage);
    }
}
