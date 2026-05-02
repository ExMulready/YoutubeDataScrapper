using System.Text.RegularExpressions;
using YoutubeResearchMcp.ML;
using YoutubeResearchMcp.Models;

namespace YoutubeResearchMcp.Services;

public class LocalIdeaGeneratorService
{
    private readonly PatternLearner _learner;
    private static readonly int CurrentYear = DateTime.UtcNow.Year;

    public LocalIdeaGeneratorService(PatternLearner learner) => _learner = learner;

    /// <summary>Generates 5 ranked video ideas from pattern analysis, scoring each with the neural network.</summary>
    public async Task<VideoIdeaResponse> GenerateVideoIdeasAsync(string niche, PatternAnalysis patterns)
    {
        var candidates = BuildCandidates(niche, patterns);
        var ideas = new List<VideoIdea>();

        foreach (var c in candidates.Take(5))
        {
            var predict = await _learner.PredictAsync(c.Title, c.Tags, "", niche);
            int viralScore = predict.Success
                ? Math.Clamp((int)Math.Round(predict.Score * 100), 1, 99)
                : HeuristicScore(c.Title, c.Tags);

            ideas.Add(new VideoIdea
            {
                Rank                = ideas.Count + 1,
                Title               = c.Title,
                Hook                = c.Hook,
                ThumbnailConcept    = c.Thumbnail,
                SuggestedTags       = c.Tags,
                WhyItWillPerform    = c.Rationale,
                EstimatedViralScore = viralScore,
                ContentOutline      = c.Outline
            });
        }

        return new VideoIdeaResponse
        {
            Niche       = niche,
            GeneratedAt = DateTime.UtcNow.ToString("o"),
            Ideas       = ideas
                .OrderByDescending(i => i.EstimatedViralScore)
                .Select((idea, i) => { idea.Rank = i + 1; return idea; })
                .ToList()
        };
    }

    /// <summary>Constructs 5 candidates covering beginner, mistakes, story, list, and authority angles.</summary>
    private static List<Candidate> BuildCandidates(string niche, PatternAnalysis patterns)
    {
        var hooks      = patterns.HookStyles.Concat(DefaultHooks).ToList();
        var thumbnails = patterns.ThumbnailPatterns.Concat(DefaultThumbnails).ToList();
        var baseTags   = patterns.TopTags.Take(8).Select(t => t.Keyword).ToList();
        var kw1        = patterns.TopKeywords.ElementAtOrDefault(0)?.Keyword ?? niche.Split(' ')[0];
        var kw2        = patterns.TopKeywords.ElementAtOrDefault(1)?.Keyword ?? "results";
        var nicheTitle = TitleCase(niche);

        var candidates = new List<Candidate>
        {
            new(
                Title: $"How to Get Started With {nicheTitle} in {CurrentYear}: Complete Beginner's Guide",
                Tags:  Merge(baseTags, ["beginner", "tutorial", "guide", niche]),
                Hook:  Pick(hooks, 0),
                Thumbnail: "Bold text overlay on high-contrast blue background: 'BEGINNER'S GUIDE'. Creator face showing welcoming expression. Year badge in corner.",
                Outline:
                [
                    "Hook: why most beginners quit too early (0–15s)",
                    $"Foundation: the {kw1} basics everyone skips",
                    "Step-by-step starting framework",
                    "Common beginner mistakes to avoid",
                    "Your first quick win this week",
                    "CTA: What to learn next"
                ],
                Rationale: $"'How to' + 'Beginner' targets the highest-volume evergreen search intent in any niche. The {CurrentYear} timestamp boosts freshness signals. Average view count in this niche is {patterns.AvgViewCount:N0} — beginner content consistently reaches 2–3× that by capturing new audience every month."
            ),

            new(
                Title: $"{5 + patterns.VideosAnalyzed % 3} {nicheTitle} Mistakes That Are Killing Your {UpperFirst(kw2)} (Fix These Now)",
                Tags:  Merge(baseTags, ["mistakes", "tips", kw2, "how to fix"]),
                Hook:  Pick(hooks, 1),
                Thumbnail: "High-contrast red background. Large bold number with an X symbol. Creator showing surprised/concerned expression. White 'MISTAKES' text overlay.",
                Outline:
                [
                    "Hook: the #1 mistake that costs people the most (0–15s)",
                    $"Mistake 1 through {5 + patterns.VideosAnalyzed % 3} — each with cause and exact fix",
                    "The underlying pattern behind all these mistakes",
                    "Quick-fix checklist",
                    "CTA: next video to watch"
                ],
                Rationale: $"'Mistakes' content drives strong comment engagement and has high shareability. At {patterns.AvgEngagementRate:F1}% avg engagement in this niche, mistake videos typically outperform by 40–60% due to emotional resonance. Power words 'mistake' and 'fix' are in the top-performing vocabulary."
            ),

            new(
                Title: $"I Tried {nicheTitle} for 30 Days: Here's What Actually Happened (Real Results)",
                Tags:  Merge(baseTags, ["case study", "honest", "results", kw1, "challenge"]),
                Hook:  Pick(hooks, 2),
                Thumbnail: "Split-screen: 'Day 1' vs 'Day 30' with contrasting before/after visuals. Bold 'BEFORE / AFTER' label. Creator face with different expressions on each side. Bright yellow accent.",
                Outline:
                [
                    "Cold open: show the Day 30 result first — no context",
                    "Why I decided to do this and what I expected",
                    "Week-by-week breakdown with real numbers",
                    "The biggest surprise (positive)",
                    "The biggest failure and what I learned",
                    "Final verdict: would I do it again?"
                ],
                Rationale: $"First-person 'I tried' titles create personal connection and pattern-interrupt curiosity. The top 5 performers in this analysis averaged {patterns.TopPerformers.Take(5).Select(v => v.ViewCount).DefaultIfEmpty(0).Average():N0} views — story-driven content with real data consistently reaches that ceiling. Optimal length for this niche is ~{patterns.OptimalDurationSeconds / 60} min, fitting this format perfectly."
            ),

            new(
                Title: BuildGapTitle(nicheTitle, kw1, patterns),
                Tags:  Merge(baseTags, [kw1, "best", "tips", "secrets"]),
                Hook:  Pick(hooks, 3),
                Thumbnail: Pick(thumbnails, 0),
                Outline:
                [
                    "Hook: tease the most surprising item — build anticipation (0–10s)",
                    "Items counted down from last to #1",
                    "Deep dive on the top 3 most impactful items",
                    "Bonus item most people overlook",
                    "CTA: which one will you try first?"
                ],
                Rationale: $"List videos drive high completion rates because the numbered format creates a completion loop. '{patterns.ContentGaps.FirstOrDefault() ?? "this angle"}' was identified as underrepresented among the {patterns.VideosAnalyzed} videos analyzed — lower competition and clear search demand."
            ),

            new(
                Title: $"The Ultimate {nicheTitle} Guide for {CurrentYear}: Everything You Need to Know",
                Tags:  Merge(baseTags, ["complete guide", CurrentYear.ToString(), "ultimate", kw1, kw2]),
                Hook:  Pick(hooks, 4),
                Thumbnail: $"Clean professional layout. '{CurrentYear}' in very large bold font. '{niche.ToUpper()}' subtitle. Dark navy or deep purple background with crisp white text. No clutter.",
                Outline:
                [
                    $"Why {CurrentYear} is different for {niche} (context hook)",
                    $"The non-negotiable foundation: {kw1}",
                    $"Intermediate strategy: {kw2}",
                    "Advanced tactics only 1% use",
                    "Full action plan and free resource",
                    "CTA: save this video"
                ],
                Rationale: $"'Ultimate Guide' + current year signals freshness and authority — YouTube's algorithm rewards comprehensive content with browse-feature placement. Top tags in this niche ({string.Join(", ", baseTags.Take(3))}) all align, maximizing tag relevance. Optimal video length is ~{patterns.OptimalDurationSeconds / 60} min, which anchor-style guides naturally hit."
            )
        };

        var topFormula = patterns.TitleFormulas.FirstOrDefault();
        if (topFormula != null)
            ReorderByFormulaMatch(candidates, topFormula.Pattern);

        return candidates;
    }

    /// <summary>Selects a list-format title template matched to the first identified content gap.</summary>
    private static string BuildGapTitle(string nicheTitle, string kw1, PatternAnalysis patterns)
    {
        var gap = patterns.ContentGaps.FirstOrDefault() ?? "";
        int n   = 7 + patterns.VideosAnalyzed % 4;

        if (gap.Contains("Beginner"))
            return $"{n} {nicheTitle} Tips for Beginners Nobody Talks About";
        if (gap.Contains("Advanced") || gap.Contains("deep"))
            return $"{n} Advanced {nicheTitle} Strategies That Actually Work in {CurrentYear}";
        if (gap.Contains("Tool") || gap.Contains("software"))
            return $"{n} Best {nicheTitle} Tools in {CurrentYear} (I Tested All of Them)";
        if (gap.Contains("mistake"))
            return $"{n} {nicheTitle} Secrets Top Creators Don't Want You to Know";
        if (gap.Contains("case study") || gap.Contains("numbers"))
            return $"{n} Real {nicheTitle} Case Studies With Actual Numbers ({CurrentYear})";
        if (gap.Contains("Template") || gap.Contains("swipe"))
            return $"{n} Free {nicheTitle} Templates That Get Results Fast";
        if (gap.Contains("interview") || gap.Contains("behind"))
            return $"{n} Things Top {nicheTitle} Creators Do Differently (Behind the Scenes)";

        return $"{n} {nicheTitle} {UpperFirst(kw1)} Tips That Changed Everything for Me";
    }

    /// <summary>Promotes the candidate whose title best matches the dominant title formula to position 0.</summary>
    private static void ReorderByFormulaMatch(List<Candidate> candidates, string formula)
    {
        int idx = -1;
        if (formula.StartsWith("How to", StringComparison.OrdinalIgnoreCase))
            idx = candidates.FindIndex(c => c.Title.StartsWith("How to", StringComparison.OrdinalIgnoreCase));
        else if (Regex.IsMatch(formula, @"\[Number\]"))
            idx = candidates.FindIndex(c => Regex.IsMatch(c.Title, @"^\d+"));

        if (idx > 0)
            (candidates[0], candidates[idx]) = (candidates[idx], candidates[0]);
    }

    /// <summary>Estimates a viral score when no trained model is available, based on presence of high-value title features.</summary>
    private static int HeuristicScore(string title, List<string> tags)
    {
        int score = 40;
        if (Regex.IsMatch(title, @"\d+")) score += 10;
        if (title.Contains('?')) score += 5;
        if (Regex.IsMatch(title, @"^how\s+to\b", RegexOptions.IgnoreCase)) score += 8;
        if (Regex.IsMatch(title, @"\b(best|secret|ultimate|mistake|honest|truth|free)\b", RegexOptions.IgnoreCase)) score += 10;
        if (Regex.IsMatch(title, @"\b20[2-9]\d\b")) score += 7;
        if (tags.Count >= 5) score += 5;
        return Math.Clamp(score, 30, 90);
    }

    /// <summary>Combines base tags with extras, deduplicating and capping the result at 10 items.</summary>
    private static List<string> Merge(List<string> baseList, IEnumerable<string> extras) =>
        baseList.Concat(extras).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();

    /// <summary>Returns the item at <c>index % list.Count</c>, allowing safe cycling through any list.</summary>
    private static string Pick<T>(IList<T> list, int index) where T : notnull =>
        list.Count > 0 ? list[index % list.Count].ToString()! : "";

    /// <summary>Title-cases each space-separated word in a string.</summary>
    private static string TitleCase(string s) =>
        string.Join(" ", s.Split(' ')
            .Select(w => w.Length > 0 ? char.ToUpper(w[0]) + w[1..] : w));

    /// <summary>Returns the string with its first character uppercased.</summary>
    private static string UpperFirst(string s) =>
        s.Length > 0 ? char.ToUpper(s[0]) + s[1..] : s;

    private static readonly string[] DefaultHooks =
    [
        "Open by showing the biggest mistake most people make — then promise to reveal why",
        "Tease the payoff: show the end result in the first 3 seconds, then 'here's exactly how I got there'",
        "Start with a bold claim that challenges what most viewers already believe"
    ];

    private static readonly string[] DefaultThumbnails =
    [
        "High-contrast red or orange background. Bold white text overlay with the core benefit. Creator face showing surprise or excitement. Arrow pointing to key text.",
        "Clean split-panel design: problem on left side, solution on right. Bright yellow accent. Large bold number if list format."
    ];

    private record Candidate(
        string Title,
        List<string> Tags,
        string Hook,
        string Thumbnail,
        List<string> Outline,
        string Rationale);
}
