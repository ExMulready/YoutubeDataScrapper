using System.Text.RegularExpressions;
using YoutubeResearchMcp.Models;

namespace YoutubeResearchMcp.Services;

public class PatternAnalysisService
{
    private static readonly string[] StopWords =
    [
        "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for",
        "of", "with", "by", "from", "is", "are", "was", "were", "be", "been",
        "i", "you", "we", "my", "your", "this", "that", "it", "how", "why",
        "what", "when", "do", "did", "does", "have", "has", "will", "can",
        "get", "got", "make", "made", "if", "not", "no", "so", "as", "up"
    ];

    private static readonly (string Pattern, string Regex)[] TitlePatternDefs =
    [
        ("How to [Action] [Topic]", @"^how to\s+\w+"),
        ("[Number] [Things] to [Action]", @"^\d+\s+\w+.*\bto\b"),
        ("[Number] Ways to [Action]", @"^\d+\s+ways?\b"),
        ("[Number] Best [Things]", @"^\d+\s+best\b"),
        ("I [Did/Tried] [Thing] for [Duration]", @"^i\s+(tried|did|tested|spent|used|went)\b"),
        ("Why [Statement]", @"^why\b"),
        ("[Topic]: [Detail] (Listicle colon)", @"^[^:]+:\s+\S"),
        ("[Superlative] [Topic] [Qualifier]", @"\b(best|worst|most|least|ultimate|only)\b"),
        ("Beginner's Guide to [Topic]", @"\b(guide|beginner|start|intro|basics?)\b"),
        ("[Topic] Tier List / Ranking", @"\b(tier list|ranking|ranked|vs\.?)\b"),
        ("Honest Review: [Product/Topic]", @"\b(honest|review|tested|worth it)\b"),
        ("What Happens When [Scenario]", @"^what happens?\b"),
    ];

    /// <summary>Extracts a full pattern analysis from a list of videos for the given niche.</summary>
    public PatternAnalysis Analyze(string niche, List<VideoMetadata> videos)
    {
        if (videos.Count == 0)
            return new PatternAnalysis { Niche = niche };

        var topPerformers = videos
            .OrderByDescending(v => v.ViewCount)
            .Take(5)
            .ToList();

        return new PatternAnalysis
        {
            Niche                  = niche,
            VideosAnalyzed         = videos.Count,
            TitleFormulas          = ExtractTitleFormulas(videos),
            TopKeywords            = ExtractKeywordFrequencies(videos.Select(v => v.Title).ToList()),
            TopTags                = ExtractTagFrequencies(videos),
            ThumbnailPatterns      = InferThumbnailPatterns(videos),
            HookStyles             = ExtractHookStyles(videos),
            AvgViewCount           = (long)videos.Average(v => v.ViewCount),
            AvgEngagementRate      = Math.Round(videos.Average(v => v.EngagementRate), 2),
            TopPerformers          = topPerformers,
            ContentGaps            = IdentifyContentGaps(niche, videos),
            OptimalDurationSeconds = EstimateOptimalDuration(topPerformers)
        };
    }

    /// <summary>Identifies which title formula patterns appear at least twice, returning up to six sorted by frequency.</summary>
    private static List<TitleFormula> ExtractTitleFormulas(List<VideoMetadata> videos)
    {
        var results = new List<TitleFormula>();

        foreach (var (pattern, regex) in TitlePatternDefs)
        {
            var matches = videos
                .Where(v => Regex.IsMatch(v.Title, regex, RegexOptions.IgnoreCase))
                .ToList();

            if (matches.Count >= 2)
            {
                results.Add(new TitleFormula
                {
                    Pattern   = pattern,
                    Example   = matches.OrderByDescending(v => v.ViewCount).First().Title,
                    Frequency = matches.Count
                });
            }
        }

        return results.OrderByDescending(f => f.Frequency).Take(6).ToList();
    }

    /// <summary>Counts keyword frequency across titles, excluding stop words and short tokens.</summary>
    private static List<KeywordFrequency> ExtractKeywordFrequencies(List<string> titles)
    {
        var freq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var title in titles)
        {
            var words = Regex.Split(title.ToLower(), @"\W+")
                .Where(w => w.Length > 3 && !StopWords.Contains(w));

            foreach (var word in words)
                freq[word] = freq.GetValueOrDefault(word, 0) + 1;
        }

        return freq
            .Where(kv => kv.Value >= 2)
            .OrderByDescending(kv => kv.Value)
            .Take(20)
            .Select(kv => new KeywordFrequency { Keyword = kv.Key, Count = kv.Value })
            .ToList();
    }

    /// <summary>Counts how often each tag appears across all videos.</summary>
    private static List<KeywordFrequency> ExtractTagFrequencies(List<VideoMetadata> videos)
    {
        var freq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var video in videos)
        {
            foreach (var tag in video.Tags)
            {
                var clean = tag.Trim().ToLower();
                if (clean.Length > 1)
                    freq[clean] = freq.GetValueOrDefault(clean, 0) + 1;
            }
        }

        return freq
            .OrderByDescending(kv => kv.Value)
            .Take(25)
            .Select(kv => new KeywordFrequency { Keyword = kv.Key, Count = kv.Value })
            .ToList();
    }

    /// <summary>Infers likely thumbnail styles from title text signals such as numbers, questions, and money references.</summary>
    private static List<string> InferThumbnailPatterns(List<VideoMetadata> videos)
    {
        var patterns = new List<string>();
        var titles   = videos.Select(v => v.Title.ToLower()).ToList();

        var hasNumbers = titles.Count(t => Regex.IsMatch(t, @"\d+"));
        if (hasNumbers > videos.Count * 0.3)
            patterns.Add($"Bold number overlay ({hasNumbers}/{videos.Count} videos use numbers in title — likely in thumbnail too)");

        var hasQuestions = titles.Count(t => t.Contains('?'));
        if (hasQuestions > videos.Count * 0.2)
            patterns.Add($"Question text overlay ({hasQuestions} videos pose a question — creates curiosity gap)");

        var hasBeforeAfter = titles.Count(t => t.Contains("before") || t.Contains("after") || t.Contains("vs"));
        if (hasBeforeAfter > 2)
            patterns.Add($"Before/After or comparison split-screen ({hasBeforeAfter} videos)");

        var hasMoney = titles.Count(t => Regex.IsMatch(t, @"\$|money|earn|income|profit|revenue|salary"));
        if (hasMoney > 2)
            patterns.Add($"Money/earnings visual ({hasMoney} videos reference money — dollar bills, income screenshots work well)");

        var hasPersonal = titles.Count(t => Regex.IsMatch(t, @"\bi\b|my|me\b"));
        if (hasPersonal > videos.Count * 0.25)
            patterns.Add($"Creator face-forward with expressive reaction ({hasPersonal} first-person titles suggest personal brand thumbnails)");

        patterns.Add("High-contrast background (bright red, orange, or yellow) with white bold text");
        patterns.Add("Arrow or circle callout pointing to key element");

        return patterns;
    }

    /// <summary>Classifies hook archetypes from the first line of video descriptions.</summary>
    private static List<string> ExtractHookStyles(List<VideoMetadata> videos)
    {
        var hooks = new List<string>();
        var descriptions = videos
            .Where(v => v.Description.Length > 50)
            .Select(v => v.Description.Split('\n').First().Trim())
            .Where(d => d.Length > 20)
            .Take(10)
            .ToList();

        var questionHooks  = descriptions.Count(d => d.Contains('?'));
        var statHooks      = descriptions.Count(d => Regex.IsMatch(d, @"\d+%|\$[\d,]+|\d+x|\d+ (million|thousand|billion)"));
        var storyHooks     = descriptions.Count(d => Regex.IsMatch(d, @"\bi\b.*(was|were|had|made|lost|found|discovered)", RegexOptions.IgnoreCase));
        var boldClaimHooks = descriptions.Count(d => Regex.IsMatch(d, @"\bnever|always|secret|nobody|everyone\b", RegexOptions.IgnoreCase));

        if (questionHooks > 0)
            hooks.Add($"Open with a provocative question that viewers can't help but answer ({questionHooks} top videos use this)");
        if (statHooks > 0)
            hooks.Add($"Lead with a shocking statistic or number to establish credibility instantly ({statHooks} videos)");
        if (storyHooks > 0)
            hooks.Add($"Personal story cold open: 'I [did X] and [unexpected result]' ({storyHooks} videos)");
        if (boldClaimHooks > 0)
            hooks.Add($"Bold contrarian claim: challenge conventional wisdom in the first 5 seconds ({boldClaimHooks} videos)");

        hooks.Add("Pattern interrupt: start mid-action or mid-sentence to force rewatches");
        hooks.Add("Tease the payoff: show the end result in the first 3 seconds, then 'here's how I got there'");

        return hooks.Take(5).ToList();
    }

    /// <summary>Returns content angles absent from the existing video titles, representing low-competition opportunities.</summary>
    private static List<string> IdentifyContentGaps(string niche, List<VideoMetadata> videos)
    {
        var gaps      = new List<string>();
        var allTitles = string.Join(" ", videos.Select(v => v.Title)).ToLower();

        var angles = new Dictionary<string, string>
        {
            ["beginner"]    = "Beginner-focused content (low competition entry point)",
            ["advanced"]    = "Advanced / deep-dive content for experienced practitioners",
            ["tool"]        = "Tool/software comparison or review content",
            ["mistake"]     = "Common mistakes / failure analysis (high search intent)",
            ["case study"]  = "Real case study with actual numbers and results",
            ["template"]    = "Template, swipe file, or done-for-you resource content",
            ["interview"]   = "Expert interview or behind-the-scenes content",
            ["2024|2025"]   = "Recently updated / current-year evergreen content"
        };

        foreach (var (signal, gap) in angles)
        {
            if (!Regex.IsMatch(allTitles, signal))
                gaps.Add(gap);
        }

        return gaps.Take(5).ToList();
    }

    /// <summary>Averages the duration of top performers to suggest an ideal video length in seconds.</summary>
    private static int EstimateOptimalDuration(List<VideoMetadata> topPerformers)
    {
        if (topPerformers.Count == 0) return 600;

        var durations = topPerformers
            .Select(v => ParseDurationToSeconds(v.Duration))
            .Where(d => d is > 60 and < 3600)
            .ToList();

        return durations.Count > 0 ? (int)durations.Average() : 600;
    }

    /// <summary>Converts a "MM:SS" or "HH:MM:SS" duration string to total seconds.</summary>
    private static int ParseDurationToSeconds(string duration)
    {
        var parts = duration.Split(':');
        return parts.Length switch
        {
            3 => int.Parse(parts[0]) * 3600 + int.Parse(parts[1]) * 60 + int.Parse(parts[2]),
            2 => int.Parse(parts[0]) * 60 + int.Parse(parts[1]),
            _ => 0
        };
    }
}
