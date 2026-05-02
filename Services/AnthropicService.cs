using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using YoutubeResearchMcp.Models;

namespace YoutubeResearchMcp.Services;

public class AnthropicService
{
    private readonly HttpClient _http;
    private const string BaseUrl = "https://api.anthropic.com/v1/messages";
    private const string Model = "claude-sonnet-4-6";

    public AnthropicService(HttpClient http, string apiKey)
    {
        _http = http;
        _http.DefaultRequestHeaders.Clear();
        _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    }

    public async Task<VideoIdeaResponse> GenerateVideoIdeasAsync(string niche, PatternAnalysis patterns)
    {
        var prompt = BuildIdeaGenerationPrompt(niche, patterns);

        var requestBody = new
        {
            model = Model,
            max_tokens = 4096,
            system = "You are an expert YouTube content strategist with deep knowledge of viral video mechanics, SEO, and audience psychology. Generate highly specific, data-backed video ideas in JSON format only.",
            messages = new[]
            {
                new { role = "user", content = prompt }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync(BaseUrl, content);
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

        var topTags = patterns.TopTags.Take(15).Select(t => t.Keyword).ToList();
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
        // Strip markdown code fences if present
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
            var parsed = JsonSerializer.Deserialize<GeneratedIdeasWrapper>(cleaned)
                ?? throw new InvalidOperationException("Null response from JSON parse");

            return new VideoIdeaResponse
            {
                Niche = niche,
                GeneratedAt = DateTime.UtcNow.ToString("o"),
                Ideas = parsed.Ideas ?? []
            };
        }
        catch
        {
            // Return the raw text wrapped in a single idea if JSON parsing fails
            return new VideoIdeaResponse
            {
                Niche = niche,
                GeneratedAt = DateTime.UtcNow.ToString("o"),
                Ideas =
                [
                    new VideoIdea
                    {
                        Rank = 1,
                        Title = "Error parsing structured response",
                        Hook = rawText,
                        WhyItWillPerform = "Raw response returned due to parse error"
                    }
                ]
            };
        }
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
