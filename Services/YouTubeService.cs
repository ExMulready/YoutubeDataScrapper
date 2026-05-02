using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using YoutubeResearchMcp.Models;
using YoutubeResearchMcp.Settings;
using System.Text.RegularExpressions;

namespace YoutubeResearchMcp.Services;

public class YouTubeService
{
    private readonly AppSettings _settings;
    private Google.Apis.YouTube.v3.YouTubeService? _ytService;
    private string _lastKey = "";

    public YouTubeService(AppSettings settings)
    {
        _settings = settings;
    }

    /// <summary>Returns (or lazily creates) a YouTube Data API client, refreshing when the API key changes.</summary>
    private Google.Apis.YouTube.v3.YouTubeService GetClient()
    {
        var key = _settings.YouTubeApiKey ?? "";
        if (_ytService is null || key != _lastKey)
        {
            _ytService = new Google.Apis.YouTube.v3.YouTubeService(new BaseClientService.Initializer
            {
                ApiKey          = key,
                ApplicationName = "YouTubeResearchMcp"
            });
            _lastKey = key;
        }
        return _ytService;
    }

    /// <summary>Searches YouTube for videos matching the niche and returns enriched metadata sorted by the chosen metric.</summary>
    public async Task<List<VideoMetadata>> SearchNicheAsync(
        string niche,
        int maxResults = 20,
        string rankBy = "views")
    {
        var searchRequest = GetClient().Search.List("snippet");
        searchRequest.Q = niche;
        searchRequest.Type = "video";
        searchRequest.MaxResults = Math.Min(maxResults, 50);
        searchRequest.Order = rankBy switch
        {
            "trending"   => SearchResource.ListRequest.OrderEnum.Date,
            "engagement" => SearchResource.ListRequest.OrderEnum.Relevance,
            _            => SearchResource.ListRequest.OrderEnum.ViewCount
        };
        searchRequest.VideoDefinition  = SearchResource.ListRequest.VideoDefinitionEnum.High;
        searchRequest.RelevanceLanguage = "en";

        var searchResponse = await searchRequest.ExecuteAsync();
        var videoIds = searchResponse.Items
            .Where(i => i.Id?.VideoId != null)
            .Select(i => i.Id.VideoId)
            .ToList();

        if (videoIds.Count == 0)
            return [];

        var videosRequest = GetClient().Videos.List("snippet,statistics,contentDetails");
        videosRequest.Id = string.Join(",", videoIds);

        var videosResponse = await videosRequest.ExecuteAsync();

        return videosResponse.Items
            .Select(v => new VideoMetadata
            {
                VideoId      = v.Id,
                Title        = v.Snippet?.Title ?? "",
                Description  = v.Snippet?.Description ?? "",
                Tags         = v.Snippet?.Tags?.ToList() ?? [],
                ChannelTitle = v.Snippet?.ChannelTitle ?? "",
                PublishedAt  = v.Snippet?.PublishedAtDateTimeOffset?.ToString("o") ?? "",
                ViewCount    = long.TryParse(v.Statistics?.ViewCount?.ToString(),   out var vc) ? vc : 0,
                LikeCount    = long.TryParse(v.Statistics?.LikeCount?.ToString(),   out var lc) ? lc : 0,
                CommentCount = long.TryParse(v.Statistics?.CommentCount?.ToString(), out var cc) ? cc : 0,
                ThumbnailUrl = v.Snippet?.Thumbnails?.Maxres?.Url
                    ?? v.Snippet?.Thumbnails?.High?.Url
                    ?? v.Snippet?.Thumbnails?.Default__?.Url
                    ?? "",
                Duration = ParseDuration(v.ContentDetails?.Duration ?? "")
            })
            .OrderByDescending(v => rankBy == "engagement" ? (long)(v.EngagementRate * 1000) : v.ViewCount)
            .Take(maxResults)
            .ToList();
    }

    /// <summary>Converts an ISO 8601 duration string (e.g. "PT4M13S") to a human-readable "MM:SS" or "HH:MM:SS" format.</summary>
    private static string ParseDuration(string isoDuration)
    {
        var match = Regex.Match(isoDuration, @"PT(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?");
        if (!match.Success) return isoDuration;

        var hours   = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 0;
        var minutes = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;
        var seconds = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;

        return hours > 0
            ? $"{hours}:{minutes:D2}:{seconds:D2}"
            : $"{minutes}:{seconds:D2}";
    }
}
