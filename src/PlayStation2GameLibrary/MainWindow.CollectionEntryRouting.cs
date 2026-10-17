using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace PlayStation2GameLibrary;

/// <summary>
/// Keeps My Collection on the same complete Add/Edit entry flow as the Catalog.
/// "+ Add copy" creates another copy of the currently selected game and
/// "Edit selected" edits exactly the selected collection copy.
/// </summary>
internal static class CollectionEntryRoutingBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            Button.ClickEvent,
            new RoutedEventHandler(HandleCollectionEntryClick),
            true);
    }

    private static async void HandleCollectionEntryClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        if (Window.GetWindow(button) is not MainWindow main) return;

        var label = button.Content?.ToString()?.Replace("  ", " ").Trim();
        if (string.Equals(label, "+ Add copy", StringComparison.OrdinalIgnoreCase))
        {
            e.Handled = true;
            await main.OpenCompleteAddCopyFromCollectionAsync();
            return;
        }

        if (string.Equals(label, "Edit selected", StringComparison.OrdinalIgnoreCase))
        {
            e.Handled = true;
            await main.OpenCompleteEditSelectedCopyAsync();
        }
    }
}

public partial class MainWindow
{
    internal async Task OpenCompleteAddCopyFromCollectionAsync()
    {
        if (string.IsNullOrWhiteSpace(_databasePath)) return;
        if (CollectionGrid.SelectedItem is not CollectionListItem selected)
        {
            MessageBox.Show(
                this,
                "Select a collection entry first. + Add copy adds another Physical or Digital copy of the selected game.",
                "Add copy",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new AddCollectionEntryWindow(_databasePath, selected.GameId) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        await RefreshCollectionPageAsync();
        await RefreshGamesAsync();
        await RefreshFooterAsync();
    }

    internal async Task OpenCompleteEditSelectedCopyAsync()
    {
        if (string.IsNullOrWhiteSpace(_databasePath)) return;
        if (CollectionGrid.SelectedItem is not CollectionListItem selected)
        {
            MessageBox.Show(this, "Select the copy you want to edit.", "Edit selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new AddCollectionEntryWindow(_databasePath, selected.GameId, selected.CopyId) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        await RefreshCollectionPageAsync();
        await RefreshGamesAsync();
        await RefreshFooterAsync();
    }
}
