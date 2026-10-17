using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace PlayStation2GameLibrary;

/// <summary>
/// Routes main-window collection add/edit actions to the complete one-step form
/// before legacy handlers can open the older two-step collection manager.
/// </summary>
internal static class CompleteAddToCollectionBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            Button.ClickEvent,
            new RoutedEventHandler(HandleCollectionActionClick),
            true);
    }

    private static async void HandleCollectionActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        if (Window.GetWindow(button) is not MainWindow main) return;
        var label = button.Content?.ToString()?.Replace("  ", " ").Trim();

        if (string.Equals(label, "+ Add to Collection", StringComparison.OrdinalIgnoreCase))
        {
            e.Handled = true;
            await main.OpenCompleteAddToCollectionAsync();
            return;
        }

        if (string.Equals(label, "Edit selected", StringComparison.OrdinalIgnoreCase))
        {
            e.Handled = true;
            await main.OpenCompleteEditCollectionAsync();
        }
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

    internal async Task OpenCompleteEditCollectionAsync()
    {
        if (string.IsNullOrWhiteSpace(_databasePath)) return;
        if (CollectionGrid.SelectedItem is not CollectionListItem selected)
        {
            MessageBox.Show(this, "Select a collection entry first.", "Edit Collection Entry", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new AddCollectionEntryWindow(_databasePath, selected.GameId, selected.CopyId) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        await RefreshCollectionPageAsync();
        await RefreshGamesAsync();
        await RefreshFooterAsync();
    }
}
