namespace YoutubeResearchMcp.Settings;

public class AppSettings
{
    public string YouTubeApiKey    { get; set; } = "";
    public string AnthropicApiKey  { get; set; } = "";
    public string DatabasePath     { get; set; } = "";
    public string LastNiche        { get; set; } = "";
    public int    LastMaxResults   { get; set; } = 20;
    public string LastRankBy       { get; set; } = "views";
}
