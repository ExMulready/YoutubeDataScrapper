using System.Text.RegularExpressions;
using YoutubeResearchMcp.Data.Entities;
using YoutubeResearchMcp.Models;

namespace YoutubeResearchMcp.ML;

public static class VideoFeatureExtractor
{
    public const int FeatureCount = 12;

    private static readonly string[] PowerWords =
    [
        "best", "worst", "secret", "secrets", "ultimate", "never", "always",
        "shocking", "amazing", "insane", "viral", "only", "instant", "proven",
        "easy", "simple", "free", "complete", "real", "honest", "truth",
        "mistake", "mistakes", "hack", "hacks", "surprising", "unbelievable"
    ];

    private static readonly Regex DigitRe     = new(@"\d",               RegexOptions.Compiled);
    private static readonly Regex HowToRe     = new(@"^how\s+to\b",      RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ListRe      = new(@"\d+\s+(ways?|tips?|things?|secrets?|steps?|ideas?|tricks?|mistakes?|hacks?)\b",
                                                     RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex YearRe      = new(@"\b20[2-9]\d\b",    RegexOptions.Compiled);
    private static readonly Regex WordSplitRe = new(@"\W+",              RegexOptions.Compiled);

    /// <summary>Extracts the 12-dimensional feature vector from a VideoMetadata object.</summary>
    public static double[] Extract(VideoMetadata video) =>
        ExtractCore(video.Title, video.Tags, video.Description);

    /// <summary>Extracts the 12-dimensional feature vector from a stored database record.</summary>
    public static double[] Extract(VideoRecord record) =>
        ExtractCore(record.Title, record.Tags, record.Description);

    /// <summary>Extracts features from raw inputs to score a concept before publishing.</summary>
    public static double[] ExtractFromConcept(string title, IEnumerable<string> tags, string description = "") =>
        ExtractCore(title, [.. tags], description);

    /// <summary>Converts a view count to a normalised training label in [0, 1] using log10 scaling.</summary>
    public static double LabelFromViewCount(long viewCount) =>
        Math.Log10(viewCount + 1) / 8.0;

    /// <summary>Computes all 12 features from raw title, tags, and description strings.</summary>
    private static double[] ExtractCore(string title, List<string> tags, string description)
    {
        var titleLower = title.ToLowerInvariant();
        var titleWords = WordSplitRe.Split(titleLower).Where(w => w.Length > 0).ToArray();

        double titleLengthNorm  = Math.Min(title.Length / 120.0, 1.0);
        double wordCountNorm    = Math.Min(titleWords.Length / 20.0, 1.0);
        double hasNumber        = DigitRe.IsMatch(title) ? 1.0 : 0.0;
        double hasQuestion      = title.Contains('?') ? 1.0 : 0.0;
        double hasHowTo         = HowToRe.IsMatch(title) ? 1.0 : 0.0;
        double hasListPattern   = ListRe.IsMatch(title) ? 1.0 : 0.0;
        double hasPowerWord     = titleWords.Any(w => PowerWords.Contains(w)) ? 1.0 : 0.0;
        double tagCountNorm     = Math.Min(tags.Count / 30.0, 1.0);
        double descLengthNorm   = Math.Min(description.Length / 5000.0, 1.0);

        var alphaChars = title.Where(char.IsLetter).ToArray();
        double titleCapsRatio = alphaChars.Length > 0
            ? (double)alphaChars.Count(char.IsUpper) / alphaChars.Length
            : 0.0;

        double hasYear            = YearRe.IsMatch(title) ? 1.0 : 0.0;
        int    powerWordCount     = titleWords.Count(w => PowerWords.Contains(w));
        double powerWordCountNorm = Math.Min(powerWordCount / 5.0, 1.0);

        return
        [
            titleLengthNorm, wordCountNorm, hasNumber, hasQuestion,
            hasHowTo, hasListPattern, hasPowerWord, tagCountNorm,
            descLengthNorm, titleCapsRatio, hasYear, powerWordCountNorm
        ];
    }

    public static string[] FeatureNames =>
    [
        "TitleLength", "WordCount", "HasNumber", "HasQuestion",
        "HasHowTo", "HasListPattern", "HasPowerWord", "TagCount",
        "DescriptionLength", "TitleCapsRatio", "HasYear", "PowerWordCount"
    ];
}
