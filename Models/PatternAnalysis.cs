using System.Text.Json.Serialization;

namespace YoutubeResearchMcp.Models;

public class PatternAnalysis
{
    [JsonPropertyName("niche")]
    public string Niche { get; set; } = "";

    [JsonPropertyName("videosAnalyzed")]
    public int VideosAnalyzed { get; set; }

    [JsonPropertyName("titleFormulas")]
    public List<TitleFormula> TitleFormulas { get; set; } = [];

    [JsonPropertyName("topKeywords")]
    public List<KeywordFrequency> TopKeywords { get; set; } = [];

    [JsonPropertyName("topTags")]
    public List<KeywordFrequency> TopTags { get; set; } = [];

    [JsonPropertyName("thumbnailPatterns")]
    public List<string> ThumbnailPatterns { get; set; } = [];

    [JsonPropertyName("hookStyles")]
    public List<string> HookStyles { get; set; } = [];

    [JsonPropertyName("avgViewCount")]
    public long AvgViewCount { get; set; }

    [JsonPropertyName("avgEngagementRate")]
    public double AvgEngagementRate { get; set; }

    [JsonPropertyName("topPerformers")]
    public List<VideoMetadata> TopPerformers { get; set; } = [];

    [JsonPropertyName("contentGaps")]
    public List<string> ContentGaps { get; set; } = [];

    [JsonPropertyName("optimalDurationSeconds")]
    public int OptimalDurationSeconds { get; set; }
}

public class TitleFormula
{
    [JsonPropertyName("pattern")]
    public string Pattern { get; set; } = "";

    [JsonPropertyName("example")]
    public string Example { get; set; } = "";

    [JsonPropertyName("frequency")]
    public int Frequency { get; set; }
}

public class KeywordFrequency
{
    [JsonPropertyName("keyword")]
    public string Keyword { get; set; } = "";

    [JsonPropertyName("count")]
    public int Count { get; set; }
}
