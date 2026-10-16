using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Data.Sqlite;

namespace PlayStation2GameLibrary;

public partial class GameDetailsWindow : Window
{
    private readonly string _databasePath;
    private readonly string _gameId;
    private readonly ObservableCollection<ReleaseListItem> _releases = new();
    private readonly MediaLibraryService _mediaLibrary = new(AppSettingsService.Load());

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

            string title;
            string year;
            string developer;
            string description;
            await using (var gameCommand = connection.CreateCommand())
            {
                gameCommand.CommandText = "SELECT CanonicalTitle,FirstReleaseYear,Developer,COALESCE(Description,'') FROM Game WHERE GameId=$id";
                gameCommand.Parameters.AddWithValue("$id", _gameId);
                await using var gameReader = await gameCommand.ExecuteReaderAsync();
                if (!await gameReader.ReadAsync())
                {
                    GameTitleText.Text = "Game not found";
                    GameMetadataText.Text = "This catalogue entry may have been removed.";
                    DetailsStatusText.Text = "No game record found.";
                    return;
                }
                title = gameReader.GetString(0);
                year = gameReader.IsDBNull(1) ? "Year unknown" : gameReader.GetInt32(1).ToString();
                developer = gameReader.IsDBNull(2) ? "Developer unknown" : gameReader.GetString(2);
                description = gameReader.GetString(3);
                GameTitleText.Text = title;
                GameMetadataText.Text = $"{year}  •  {developer}";
                DescriptionText.Text = string.IsNullOrWhiteSpace(description) ? "No description available." : description;
            }

            _releases.Clear();
            await using var releaseCommand = connection.CreateCommand();
            releaseCommand.CommandText = @"
SELECT r.ReleaseTitle,r.RegionCode,r.ReleaseYear,r.Publisher,r.ReleaseType,r.IsPrimary,
       COALESCE((SELECT group_concat(d.SerialRaw, ', ') FROM Disc d WHERE d.ReleaseId=r.ReleaseId), '—') AS Serials,
       COALESCE((SELECT group_concat(DISTINCT rl.LanguageCode) FROM ReleaseLanguage rl WHERE rl.ReleaseId=r.ReleaseId), '') AS Languages
FROM Release r WHERE r.GameId=$id
ORDER BY r.IsPrimary DESC,
         CASE WHEN r.RegionCode IN ('NorthAmerica','USA','NTSC-U','NTSC-U/C') THEN 0
              WHEN r.RegionCode IN ('Europe','PAL','Europe/PAL','Australia') THEN 1 ELSE 2 END,
         r.ReleaseYear, r.ReleaseTitle;";
            releaseCommand.Parameters.AddWithValue("$id", _gameId);
            await using var releaseReader = await releaseCommand.ExecuteReaderAsync();
            while (await releaseReader.ReadAsync())
            {
                var rawRegion = releaseReader.GetString(1);
                var languageCodes = releaseReader.GetString(7).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                _releases.Add(new ReleaseListItem
                {
                    Title = releaseReader.GetString(0),
                    Region = FriendlyRegion(rawRegion),
                    RegionFlag = FlagIconFactory.ForRegion(rawRegion),
                    Year = releaseReader.IsDBNull(2) ? "—" : releaseReader.GetInt32(2).ToString(),
                    Publisher = releaseReader.IsDBNull(3) ? "—" : releaseReader.GetString(3),
                    ReleaseType = releaseReader.GetString(4),
                    IsPrimary = releaseReader.GetInt32(5) == 1,
                    Serial = releaseReader.GetString(6),
                    Languages = string.Join(", ", languageCodes),
                    LanguageFlags = languageCodes.Select(FlagIconFactory.ForLanguage).Where(x => x is not null).Cast<ImageSource>().ToArray()
                });
            }

            var primary = _releases.FirstOrDefault(r => r.IsPrimary) ?? _releases.FirstOrDefault();
            if (primary is not null)
            {
                PrimarySerialText.Text = primary.Serial;
                PrimaryRegionText.Text = primary.Region;
                PrimaryRegionFlag.Source = primary.RegionFlag;
                PublisherText.Text = primary.Publisher;
                LanguageFlagsPanel.Children.Clear();
                foreach (var flag in primary.LanguageFlags)
                    LanguageFlagsPanel.Children.Add(new Image { Source = flag, Width = 27, Height = 18, Stretch = Stretch.Fill, Margin = new Thickness(0, 0, 6, 0) });
                if (primary.LanguageFlags.Count == 0)
                    LanguageFlagsPanel.Children.Add(new TextBlock { Text = "—", Foreground = Brushes.SlateGray });

                var serial = primary.Serial.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
                var coverPath = _mediaLibrary.FindCoverForSerial(serial);
                var cover = _mediaLibrary.LoadImage(coverPath);
                CoverImage.Source = cover;
                CoverImage.Visibility = cover is null ? Visibility.Collapsed : Visibility.Visible;
                NoCoverPlaceholder.Visibility = cover is null ? Visibility.Visible : Visibility.Collapsed;
                DetailsStatusText.Text = cover is null
                    ? $"{_releases.Count:N0} regional release(s). No local front cover found for {serial}."
                    : $"{_releases.Count:N0} regional release(s). Cover: {Path.GetFileName(coverPath)}";
            }
            else
            {
                PrimarySerialText.Text = "—";
                PrimaryRegionText.Text = "—";
                PublisherText.Text = "—";
                DetailsStatusText.Text = "No regional releases are linked to this game.";
            }
        }
        catch (Exception ex)
        {
            DetailsStatusText.Text = "Could not load game details.";
            MessageBox.Show(this, ex.Message, "Game details error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string FriendlyRegion(string region)
    {
        var value = region.ToLowerInvariant();
        if (value.Contains("north") || value.Contains("usa") || value.Contains("ntsc-u")) return "NTSC-U/C";
        if (value.Contains("japan") || value.Contains("ntsc-j")) return "NTSC-J";
        if (value.Contains("europe") || value.Contains("pal") || value.Contains("australia")) return "PAL";
        return region;
    }

    private sealed class ReleaseListItem
    {
        public string Title { get; init; } = "";
        public string Region { get; init; } = "";
        public ImageSource? RegionFlag { get; init; }
        public string Year { get; init; } = "";
        public string Serial { get; init; } = "";
        public string Publisher { get; init; } = "";
        public string ReleaseType { get; init; } = "";
        public bool IsPrimary { get; init; }
        public string Languages { get; init; } = "";
        public IReadOnlyList<ImageSource> LanguageFlags { get; init; } = Array.Empty<ImageSource>();
    }
}
