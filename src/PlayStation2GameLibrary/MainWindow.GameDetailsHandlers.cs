using System.Windows;
using System.Windows.Input;

namespace PlayStation2GameLibrary;

public partial class MainWindow
{
    private async void InlineGameDetails_Click(object sender, RoutedEventArgs e)
    {
        if (GamesGrid.SelectedItem is not GameListItem selected) return;
        await ShowInlineGameDetailsAsync(selected.GameId);
    }

    private async void InlineGameDetails_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GamesGrid.SelectedItem is not GameListItem selected) return;
        await ShowInlineGameDetailsAsync(selected.GameId);
    }
}
