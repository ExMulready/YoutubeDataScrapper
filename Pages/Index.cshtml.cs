using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using YoutubeResearchMcp.ML;
using YoutubeResearchMcp.Services;

namespace YoutubeResearchMcp.Pages;

public class IndexModel : PageModel
{
    private readonly YouTubeService _youtube;
    private readonly PatternAnalysisService _patterns;
    private readonly PatternLearner _learner;
    public readonly ResearchSession Session;

    public IndexModel(
        YouTubeService youtube,
        PatternAnalysisService patterns,
        PatternLearner learner,
        ResearchSession session)
    {
        _youtube  = youtube;
        _patterns = patterns;
        _learner  = learner;
        Session   = session;
    }

    public int MaxResults { get; private set; } = 50;
    public string RankBy { get; private set; } = "views";

    public void OnGet() { }

    /// <summary>Searches YouTube and stores results in the session.</summary>
    public async Task<IActionResult> OnPostSearchAsync(
        string niche, int maxResults = 25, string rankBy = "views")
    {
        if (string.IsNullOrWhiteSpace(niche))
        {
            TempData["Error"] = "Please enter a niche or keywords.";
            return RedirectToPage();
        }

        try
        {
            var results = await _youtube.SearchNicheAsync(niche, maxResults, rankBy);
            Session.CurrentNiche  = niche;
            Session.SearchResults = results;
            TempData["Success"]   = $"Found {results.Count} videos for \"{niche}\".";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Search failed: {ex.Message}";
        }

        return RedirectToPage();
    }

    /// <summary>Saves session results to the database and triggers a model retrain if new videos were added.</summary>
    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (Session.SearchResults.Count == 0)
        {
            TempData["Error"] = "No search results to save. Run a search first.";
            return RedirectToPage();
        }

        try
        {
            var saveResult = await _learner.SaveVideosAsync(Session.CurrentNiche, Session.SearchResults);

            var msg = $"Saved {saveResult.Added} new videos ({saveResult.Skipped} duplicates skipped). " +
                      $"Total in database: {saveResult.TotalInDb}.";

            if (saveResult.Added > 0)
            {
                var trainResult = await _learner.TrainAsync(Session.CurrentNiche);
                if (trainResult.Success)
                {
                    Session.TrainSuccess    = true;
                    Session.TrainResultText = $"Model retrained on {trainResult.VideosUsed} videos. Loss: {trainResult.FinalLoss:F6}.";
                    msg += $" Neural network retrained (loss: {trainResult.FinalLoss:F6}).";
                }
            }

            TempData["Success"] = msg;
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Save failed: {ex.Message}";
        }

        return RedirectToPage();
    }

    /// <summary>Runs pattern analysis on session results and redirects to the Analysis page.</summary>
    public IActionResult OnPostAnalyze()
    {
        if (Session.SearchResults.Count == 0)
        {
            TempData["Error"] = "No search results to analyze. Run a search first.";
            return RedirectToPage();
        }

        try
        {
            Session.CurrentPatterns = _patterns.Analyze(Session.CurrentNiche, Session.SearchResults);
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Analysis failed: {ex.Message}";
            return RedirectToPage();
        }

        return RedirectToPage("/Analysis");
    }
}
