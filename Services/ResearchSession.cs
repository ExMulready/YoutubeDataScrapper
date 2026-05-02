using YoutubeResearchMcp.Models;

namespace YoutubeResearchMcp.Services;

public class ResearchSession
{
    public string CurrentNiche { get; set; } = "";
    public List<VideoMetadata> SearchResults { get; set; } = [];

    public PatternAnalysis? CurrentPatterns { get; set; }

    public VideoIdeaResponse? CurrentIdeas { get; set; }

    public string? TrainResultText { get; set; }
    public bool TrainSuccess { get; set; }

    public bool HasScoreResult { get; set; }
    public double ScorePercent { get; set; }
    public string? ScoreResultText { get; set; }
    public Dictionary<string, double>? FeatureImportance { get; set; }
}
