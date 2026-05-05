using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using YoutubeResearchMcp.Models;
using YoutubeResearchMcp.Settings;
using System.Text.RegularExpressions;

namespace YoutubeResearchMcp.Services;

public class YouTubeService
{
    private readonly AppSettings _settings;
    private readonly object      _syncLock = new();
    private Google.Apis.YouTube.v3.YouTubeService? _ytService;
    private string _lastKey = "";

    private const int MaxRetries   = 3;
    private const int RetryBaseMs  = 1000;

    public YouTubeService(AppSettings settings)
    {
        _settings = settings;
    }

    /// <summary>Returns (or lazily creates) a YouTube Data API client, refreshing when the API key changes.</summary>
    private Google.Apis.YouTube.v3.YouTubeService GetClient()
    {
        var key = _settings.YouTubeApiKey ?? "";
        lock (_syncLock)
        {
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
    }

    /// <summary>
    /// Searches YouTube for videos matching the niche with up to 3 attempts using exponential back-off.
    /// Throws a user-friendly exception on exhaustion.
    /// </summary>
    public async Task<List<VideoMetadata>> SearchNicheAsync(
        string niche,
        int maxResults = 20,
        string rankBy = "views")
    {
        Exception? lastEx = null;

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                return await DoSearchAsync(niche, maxResults, rankBy);
            }
            catch (Google.GoogleApiException ex)
                when ((int)ex.HttpStatusCode == 429 || (int)ex.HttpStatusCode >= 500)
            {
                lastEx = ex;
            }
            catch (HttpRequestException ex)
            {
                lastEx = ex;
            }

            if (attempt < MaxRetries)
                await Task.Delay(RetryBaseMs * (int)Math.Pow(2, attempt - 1));
        }

        throw new InvalidOperationException(
            "YouTube API is temporarily rate-limited or unavailable — please wait a moment and try again.",
            lastEx);
    }

    private async Task<List<VideoMetadata>> DoSearchAsync(string niche, int maxResults, string rankBy)
    {
        var client       = GetClient();
        var publishedAfter = DateTime.UtcNow.AddMonths(-1);
        var order = rankBy switch
        {
            "trending"   => SearchResource.ListRequest.OrderEnum.Date,
            "engagement" => SearchResource.ListRequest.OrderEnum.Relevance,
            _            => SearchResource.ListRequest.OrderEnum.ViewCount
        };

        // The API returns at most 50 per page; paginate until we have enough.
        var videoIds  = new List<string>();
        string? pageToken = null;
        const int PageSize = 50;

        while (videoIds.Count < maxResults)
        {
            var req = client.Search.List("snippet");
            req.Q                 = niche;
            req.Type              = "video";
            req.MaxResults        = PageSize;
            req.Order             = order;
            req.RelevanceLanguage = "en";
            req.PublishedAfter    = publishedAfter;
            if (pageToken != null) req.PageToken = pageToken;

            var response = await req.ExecuteAsync();

            foreach (var item in response.Items)
            {
                if (item.Id?.VideoId != null)
                    videoIds.Add(item.Id.VideoId);
                if (videoIds.Count == maxResults) break;
            }

            pageToken = response.NextPageToken;
            if (string.IsNullOrEmpty(pageToken)) break;
        }

        if (videoIds.Count == 0) return [];

        // Fetch full metadata in batches of 50 (Videos.list id limit).
        var allVideos = new List<VideoMetadata>();
        for (int i = 0; i < videoIds.Count; i += PageSize)
        {
            var batch = videoIds.Skip(i).Take(PageSize).ToList();
            var videosRequest = client.Videos.List("snippet,statistics,contentDetails");
            videosRequest.Id = string.Join(",", batch);
            var videosResponse = await videosRequest.ExecuteAsync();

            allVideos.AddRange(videosResponse.Items.Select(v => new VideoMetadata
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
            }));
        }

        return allVideos
            .OrderByDescending(v => rankBy == "engagement" ? (long)(v.EngagementRate * 1000) : v.ViewCount)
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
