using System.IO;
using System.Text.Json;

namespace YoutubeResearchMcp.Settings;

public static class SettingsService
{
    private static readonly string AppDataFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "YouTubeResearchStudio");

    private static readonly string SettingsPath =
        Path.Combine(AppDataFolder, "settings.json");

    public static string DefaultDatabasePath =>
        Path.Combine(AppDataFolder, "youtube_research.db");

    /// <summary>Loads application settings from disk, creating a default file if none exists.</summary>
    public static AppSettings Load()
    {
        try
        {
            Directory.CreateDirectory(AppDataFolder);

            if (!File.Exists(SettingsPath))
                return CreateDefaults();

            var json     = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? CreateDefaults();

            if (string.IsNullOrWhiteSpace(settings.DatabasePath))
                settings.DatabasePath = DefaultDatabasePath;

            return settings;
        }
        catch
        {
            return CreateDefaults();
        }
    }

    /// <summary>Serialises settings to the app-data folder as indented JSON.</summary>
    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(AppDataFolder);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }

    /// <summary>Returns the platform-specific app-data folder path used for settings and the database.</summary>
    public static string GetAppDataFolder() => AppDataFolder;

    /// <summary>Creates an AppSettings instance with the default database path pre-filled.</summary>
    private static AppSettings CreateDefaults() => new()
    {
        DatabasePath = DefaultDatabasePath
    };
}
