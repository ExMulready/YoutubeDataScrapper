using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace YoutubeResearchMcp.Data.Entities;

[Table("video_records")]
public class VideoRecord
{
    [Key, Column("id")]
    public int Id { get; set; }

    [Column("video_id"), MaxLength(30), Required]
    public string VideoId { get; set; } = "";

    [Column("niche"), MaxLength(255)]
    public string Niche { get; set; } = "";

    [Column("title")]
    public string Title { get; set; } = "";

    [Column("description")]
    public string Description { get; set; } = "";

    [Column("tags")]
    public string TagsCsv { get; set; } = "";

    [Column("channel_title"), MaxLength(255)]
    public string ChannelTitle { get; set; } = "";

    [Column("published_at")]
    public DateTime? PublishedAt { get; set; }

    [Column("view_count")]
    public long ViewCount { get; set; }

    [Column("like_count")]
    public long LikeCount { get; set; }

    [Column("comment_count")]
    public long CommentCount { get; set; }

    [Column("thumbnail_url")]
    public string ThumbnailUrl { get; set; } = "";

    [Column("duration"), MaxLength(20)]
    public string Duration { get; set; } = "";

    [Column("collected_at")]
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;

    // Supports both new JSON format and legacy pipe-delimited format for backward compatibility.
    [NotMapped]
    public List<string> Tags
    {
        get
        {
            if (string.IsNullOrEmpty(TagsCsv)) return [];
            if (TagsCsv.TrimStart().StartsWith('['))
            {
                try { return JsonSerializer.Deserialize<List<string>>(TagsCsv) ?? []; }
                catch (JsonException) { /* fall through to legacy pipe format */ }
            }
            return [.. TagsCsv.Split('|', StringSplitOptions.RemoveEmptyEntries)];
        }
    }

    [NotMapped]
    public double EngagementRate =>
        ViewCount > 0 ? Math.Round((double)(LikeCount + CommentCount) / ViewCount * 100, 4) : 0;
}
