using System.IO;
using System.Text.Json;

namespace PlayStation2GameLibrary;

internal sealed class AppSettings
{
    public string MediaLibraryLocation { get; set; } = @".\Media";
    public bool ThumbnailCacheEnabled { get; set; } = true;
}

internal static class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string SettingsPath => Path.Combine(AppPaths.ConfigDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            AppPaths.EnsurePortableFolders();
            if (!File.Exists(SettingsPath)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        AppPaths.EnsurePortableFolders();
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    public static string ResolveMediaRoot(AppSettings settings)
    {
        var configured = string.IsNullOrWhiteSpace(settings.MediaLibraryLocation) ? @".\Media" : settings.MediaLibraryLocation.Trim();
        if (Path.IsPathRooted(configured)) return Path.GetFullPath(configured);
        return Path.GetFullPath(Path.Combine(AppPaths.PortableRoot, configured));
    }
}
