using System.Text.Json.Serialization;

namespace YoutubeResearchMcp.Models;

public class VideoMetadata
{
    [JsonPropertyName("videoId")]
    public string VideoId { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("channelTitle")]
    public string ChannelTitle { get; set; } = "";

    [JsonPropertyName("publishedAt")]
    public string PublishedAt { get; set; } = "";

    [JsonPropertyName("viewCount")]
    public long ViewCount { get; set; }

    [JsonPropertyName("likeCount")]
    public long LikeCount { get; set; }

    [JsonPropertyName("commentCount")]
    public long CommentCount { get; set; }

    [JsonPropertyName("thumbnailUrl")]
    public string ThumbnailUrl { get; set; } = "";

    [JsonPropertyName("duration")]
    public string Duration { get; set; } = "";

    [JsonPropertyName("engagementRate")]
    public double EngagementRate => ViewCount > 0
        ? Math.Round((double)(LikeCount + CommentCount) / ViewCount * 100, 2)
        : 0;
}
