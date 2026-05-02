using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using YoutubeResearchMcp.Settings;

namespace YoutubeResearchMcp.Pages;

public class SettingsModel : PageModel
{
    private readonly AppSettings _settings;

    public SettingsModel(AppSettings settings)
    {
        _settings = settings;
    }

    public AppSettings Settings => _settings;
    public string DefaultDbPath => SettingsService.DefaultDatabasePath;

    public void OnGet() { }

    /// <summary>Persists updated API keys and database path to settings, falling back to the default path when blank.</summary>
    public IActionResult OnPostSave(string youTubeApiKey, string anthropicApiKey, string databasePath)
    {
        _settings.YouTubeApiKey   = youTubeApiKey   ?? "";
        _settings.AnthropicApiKey = anthropicApiKey ?? "";
        _settings.DatabasePath    = string.IsNullOrWhiteSpace(databasePath)
            ? SettingsService.DefaultDatabasePath
            : databasePath;

        SettingsService.Save(_settings);
        TempData["Success"] = "Settings saved.";
        return RedirectToPage();
    }
}
