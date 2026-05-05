using YoutubeResearchMcp.Data.Entities;
using YoutubeResearchMcp.Models;

namespace YoutubeResearchMcp.ML;

public interface IFeatureExtractor
{
    double[] Extract(VideoMetadata video);
    double[] Extract(VideoRecord record);
    double[] ExtractFromConcept(string title, IEnumerable<string> tags, string description = "");
    double LabelFromViewCount(long viewCount);
    string[] FeatureNames { get; }
}
