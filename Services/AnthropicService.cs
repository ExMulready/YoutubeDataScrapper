using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using YoutubeResearchMcp.Models;
using YoutubeResearchMcp.Settings;

namespace YoutubeResearchMcp.Services;

public class AnthropicService
{
    private readonly HttpClient _http;
    private readonly AppSettings _settings;

    private const string BaseUrl = "https://api.anthropic.com/v1/messages";
    private const string Model   = "claude-sonnet-4-6";

    // Read IsConfigured from settings each call so a key saved on the Settings page takes effect immediately.
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.AnthropicApiKey);

    // anthropic-version is set globally in Program.cs; x-api-key is added per-request below.
    public AnthropicService(HttpClient http, AppSettings settings)
    {
        _http     = http;
        _settings = settings;
    }

    public async Task<VideoIdeaResponse> GenerateVideoIdeasAsync(string niche, PatternAnalysis patterns)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Anthropic API key is not configured.");

        var prompt = BuildIdeaGenerationPrompt(niche, patterns);

        var requestBody = new
        {
            model      = Model,
            max_tokens = 4096,
            system     = "You are an expert YouTube content strategist with deep knowledge of viral video mechanics, SEO, and audience psychology. Generate highly specific, data-backed video ideas in JSON format only.",
            messages   = new[] { new { role = "user", content = prompt } }
        };

        var json = JsonSerializer.Serialize(requestBody);

        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-api-key", _settings.AnthropicApiKey);

        var response     = await _http.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Anthropic API error {response.StatusCode}: {responseBody}");

        var apiResponse = JsonSerializer.Deserialize<AnthropicApiResponse>(responseBody)
            ?? throw new InvalidOperationException("Failed to deserialize Anthropic response");

        var rawText = apiResponse.Content?.FirstOrDefault()?.Text ?? "";
        return ParseVideoIdeas(niche, rawText);
    }

    private static string BuildIdeaGenerationPrompt(string niche, PatternAnalysis patterns)
    {
        var topPerformers = patterns.TopPerformers.Take(5)
            .Select(v => $"  - \"{v.Title}\" — {v.ViewCount:N0} views, {v.EngagementRate}% engagement")
            .ToList();

        var titleFormulas = patterns.TitleFormulas.Take(5)
            .Select(f => $"  - Pattern: \"{f.Pattern}\" (used {f.Frequency}x, e.g. \"{f.Example}\")")
            .ToList();

        var topTags     = patterns.TopTags.Take(15).Select(t => t.Keyword).ToList();
        var topKeywords = patterns.TopKeywords.Take(15).Select(k => k.Keyword).ToList();

        return $$"""
You are analyzing the YouTube niche: "{{niche}}"

## Data from top {{patterns.VideosAnalyzed}} videos:

### Performance Benchmarks
- Average view count: {{patterns.AvgViewCount:N0}}
- Average engagement rate: {{patterns.AvgEngagementRate:F2}}%
- Optimal video length: ~{{patterns.OptimalDurationSeconds / 60}} minutes

### Top Performing Videos
{{string.Join("\n", topPerformers)}}

### Title Formulas That Work
{{string.Join("\n", titleFormulas)}}

### Top Tags
{{string.Join(", ", topTags)}}

### High-Frequency Keywords in Titles
{{string.Join(", ", topKeywords)}}

### Thumbnail Patterns Observed
{{string.Join("\n", patterns.ThumbnailPatterns.Select(p => $"  - {p}"))}}

### Hook Styles That Perform
{{string.Join("\n", patterns.HookStyles.Select(h => $"  - {h}"))}}

### Content Gaps Identified
{{string.Join("\n", patterns.ContentGaps.Select(g => $"  - {g}"))}}

---

Generate exactly 5 data-driven video ideas for this niche. Each idea must exploit the patterns above AND fill a content gap where possible.

Respond with ONLY valid JSON matching this exact structure (no markdown, no explanation):

{
  "ideas": [
    {
      "rank": 1,
      "title": "Exact video title (use proven title formula)",
      "hook": "First 10 seconds concept — what you say/show to instantly hook viewers",
      "thumbnailConcept": "Detailed thumbnail description: colors, text overlay, imagery, face expression if any",
      "suggestedTags": ["tag1", "tag2", "tag3", "tag4", "tag5", "tag6", "tag7", "tag8"],
      "whyItWillPerform": "2-3 sentences explaining the data-backed reasons this will get views",
      "estimatedViralScore": 85,
      "contentOutline": ["Intro hook (0-10s)", "Section 2", "Section 3", "Section 4", "CTA"]
    }
  ]
}
""";
    }

    private static VideoIdeaResponse ParseVideoIdeas(string niche, string rawText)
    {
        var cleaned = rawText.Trim();
        if (cleaned.StartsWith("```"))
        {
            cleaned = string.Join("\n", cleaned.Split('\n').Skip(1));
            var lastFence = cleaned.LastIndexOf("```");
            if (lastFence >= 0)
                cleaned = cleaned[..lastFence].TrimEnd();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<GeneratedIdeasWrapper>(cleaned);
            if (parsed?.Ideas is { Count: > 0 } ideas)
                return new VideoIdeaResponse
                {
                    Niche       = niche,
                    GeneratedAt = DateTime.UtcNow.ToString("o"),
                    Ideas       = ideas
                };
        }
        catch (JsonException) { /* fall through to raw-text fallback */ }

        return new VideoIdeaResponse
        {
            Niche       = niche,
            GeneratedAt = DateTime.UtcNow.ToString("o"),
            Ideas       =
            [
                new VideoIdea
                {
                    Rank             = 1,
                    Title            = "Error parsing structured response",
                    Hook             = rawText,
                    WhyItWillPerform = "Raw response returned due to JSON parse error"
                }
            ]
        };
    }

    private class GeneratedIdeasWrapper
    {
        [JsonPropertyName("ideas")]
        public List<VideoIdea>? Ideas { get; set; }
    }

    private class AnthropicApiResponse
    {
        [JsonPropertyName("content")]
        public List<ContentBlock>? Content { get; set; }
    }

    private class ContentBlock
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
