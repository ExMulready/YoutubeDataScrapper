using System.Text.Json.Serialization;

namespace YoutubeResearchMcp.Models;

public class VideoIdea
{
    [JsonPropertyName("rank")]
    public int Rank { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("hook")]
    public string Hook { get; set; } = "";

    [JsonPropertyName("thumbnailConcept")]
    public string ThumbnailConcept { get; set; } = "";

    [JsonPropertyName("suggestedTags")]
    public List<string> SuggestedTags { get; set; } = [];

    [JsonPropertyName("whyItWillPerform")]
    public string WhyItWillPerform { get; set; } = "";

    [JsonPropertyName("estimatedViralScore")]
    public int EstimatedViralScore { get; set; }

    [JsonPropertyName("contentOutline")]
    public List<string> ContentOutline { get; set; } = [];
}

public class VideoIdeaResponse
{
    [JsonPropertyName("niche")]
    public string Niche { get; set; } = "";

    [JsonPropertyName("generatedAt")]
    public string GeneratedAt { get; set; } = DateTime.UtcNow.ToString("o");

    [JsonPropertyName("ideas")]
    public List<VideoIdea> Ideas { get; set; } = [];
}
