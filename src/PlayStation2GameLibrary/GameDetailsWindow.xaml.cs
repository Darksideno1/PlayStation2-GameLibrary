using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Data.Sqlite;

namespace PlayStation2GameLibrary;

public partial class GameDetailsWindow : Window
{
    private readonly string _databasePath;
    private readonly string _gameId;
    private readonly ObservableCollection<ReleaseListItem> _releases = new();

    public GameDetailsWindow(string databasePath, string gameId)
    {
        InitializeComponent();
        _databasePath = databasePath;
        _gameId = gameId;
        ReleasesGrid.ItemsSource = _releases;
        Loaded += async (_, _) => await LoadDetailsAsync();
    }

    private async Task LoadDetailsAsync()
    {
        try
        {
            await using var connection = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly");
            await connection.OpenAsync();
            await using (var gameCommand = connection.CreateCommand())
            {
                gameCommand.CommandText = "SELECT CanonicalTitle,FirstReleaseYear,Developer FROM Game WHERE GameId=$id";
                gameCommand.Parameters.AddWithValue("$id", _gameId);
                await using var gameReader = await gameCommand.ExecuteReaderAsync();
                if (!await gameReader.ReadAsync())
                {
                    GameTitleText.Text = "Game not found";
                    GameMetadataText.Text = "This catalogue entry may have been removed.";
                    DetailsStatusText.Text = "No game record found.";
                    return;
                }
                GameTitleText.Text = gameReader.GetString(0);
                var year = gameReader.IsDBNull(1) ? "Year unknown" : gameReader.GetInt32(1).ToString();
                var developer = gameReader.IsDBNull(2) ? "Developer unknown" : gameReader.GetString(2);
                GameMetadataText.Text = $"{year}  •  {developer}";
            }

            await using var releaseCommand = connection.CreateCommand();
            releaseCommand.CommandText = @"
SELECT r.ReleaseTitle,r.RegionCode,r.ReleaseYear,r.Publisher,
       COALESCE((SELECT group_concat(d.SerialRaw, ', ') FROM Disc d WHERE d.ReleaseId=r.ReleaseId), '—') AS Serials,
       COALESCE((SELECT group_concat(DISTINCT rl.LanguageCode) FROM ReleaseLanguage rl WHERE rl.ReleaseId=r.ReleaseId), '—') AS Languages
FROM Release r WHERE r.GameId=$id
ORDER BY r.RegionCode, r.ReleaseYear, r.ReleaseTitle;";
            releaseCommand.Parameters.AddWithValue("$id", _gameId);
            await using var releaseReader = await releaseCommand.ExecuteReaderAsync();
            while (await releaseReader.ReadAsync())
            {
                _releases.Add(new ReleaseListItem
                {
                    Title = releaseReader.GetString(0),
                    Region = releaseReader.GetString(1),
                    Year = releaseReader.IsDBNull(2) ? "—" : releaseReader.GetInt32(2).ToString(),
                    Publisher = releaseReader.IsDBNull(3) ? "—" : releaseReader.GetString(3),
                    Serial = releaseReader.GetString(4),
                    Languages = releaseReader.GetString(5)
                });
            }
            DetailsStatusText.Text = $"{_releases.Count:N0} regional release(s). Read-only catalogue view; release editing is not implemented yet.";
        }
        catch (Exception ex)
        {
            DetailsStatusText.Text = "Could not load game details.";
            MessageBox.Show(this, ex.Message, "Game details error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private sealed class ReleaseListItem
    {
        public string Title { get; init; } = "";
        public string Region { get; init; } = "";
        public string Year { get; init; } = "";
        public string Serial { get; init; } = "";
        public string Publisher { get; init; } = "";
        public string Languages { get; init; } = "";
    }
}
