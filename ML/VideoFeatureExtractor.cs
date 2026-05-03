using System.Text.RegularExpressions;
using YoutubeResearchMcp.Data.Entities;
using YoutubeResearchMcp.Models;

namespace YoutubeResearchMcp.ML;

public class VideoFeatureExtractor : IFeatureExtractor
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

    public double[] Extract(VideoMetadata video) =>
        ExtractCore(video.Title, video.Tags, video.Description);

    public double[] Extract(VideoRecord record) =>
        ExtractCore(record.Title, record.Tags, record.Description);

    public double[] ExtractFromConcept(string title, IEnumerable<string> tags, string description = "") =>
        ExtractCore(title, [.. tags], description);

    public double LabelFromViewCount(long viewCount) =>
        Math.Log10(viewCount + 1) / 8.0;

    private static double[] ExtractCore(string title, List<string> tags, string description)
    {
        var titleLower = title.ToLowerInvariant();
        var titleWords = WordSplitRe.Split(titleLower).Where(w => w.Length > 0).ToArray();

        double titleLengthNorm = Math.Clamp(title.Length / 120.0, 0.0, 1.0);
        double wordCountNorm   = Math.Clamp(titleWords.Length / 20.0, 0.0, 1.0);
        double hasNumber       = DigitRe.IsMatch(title) ? 1.0 : 0.0;
        double hasQuestion     = title.Contains('?') ? 1.0 : 0.0;
        double hasHowTo        = HowToRe.IsMatch(title) ? 1.0 : 0.0;
        double hasListPattern  = ListRe.IsMatch(title) ? 1.0 : 0.0;
        double hasPowerWord    = titleWords.Any(w => PowerWords.Contains(w)) ? 1.0 : 0.0;
        double tagCountNorm    = Math.Clamp(tags.Count / 30.0, 0.0, 1.0);
        double descLengthNorm  = Math.Clamp(description.Length / 5000.0, 0.0, 1.0);

        var alphaChars = title.Where(char.IsLetter).ToArray();
        double titleCapsRatio = alphaChars.Length > 0
            ? Math.Clamp((double)alphaChars.Count(char.IsUpper) / alphaChars.Length, 0.0, 1.0)
            : 0.0;

        double hasYear            = YearRe.IsMatch(title) ? 1.0 : 0.0;
        int    powerWordCount     = titleWords.Count(w => PowerWords.Contains(w));
        double powerWordCountNorm = Math.Clamp(powerWordCount / 5.0, 0.0, 1.0);

        return
        [
            titleLengthNorm, wordCountNorm, hasNumber, hasQuestion,
            hasHowTo, hasListPattern, hasPowerWord, tagCountNorm,
            descLengthNorm, titleCapsRatio, hasYear, powerWordCountNorm
        ];
    }

    public string[] FeatureNames =>
    [
        "TitleLength", "WordCount", "HasNumber", "HasQuestion",
        "HasHowTo", "HasListPattern", "HasPowerWord", "TagCount",
        "DescriptionLength", "TitleCapsRatio", "HasYear", "PowerWordCount"
    ];
}
