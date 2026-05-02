using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using YoutubeResearchMcp.ML;
using YoutubeResearchMcp.Services;

namespace YoutubeResearchMcp.Pages;

public class NeuralNetworkModel : PageModel
{
    private readonly PatternLearner _learner;
    public readonly ResearchSession Session;

    public NeuralNetworkModel(PatternLearner learner, ResearchSession session)
    {
        _learner = learner;
        Session  = session;
    }

    public void OnGet() { }

    /// <summary>Trains the model on stored videos for the given niche and stores the result in the session.</summary>
    public async Task<IActionResult> OnPostTrainAsync(string? niche, int epochs = 150)
    {
        try
        {
            var result = await _learner.TrainAsync(niche, epochs);
            Session.TrainSuccess    = result.Success;
            Session.TrainResultText = result.Success
                ? $"Trained on {result.VideosUsed} videos for {result.Epochs} epochs. Final loss: {result.FinalLoss:F6}. Model ID: {result.ModelId}."
                : result.Message;

            if (result.Success)
                TempData["Success"] = Session.TrainResultText;
            else
                TempData["Error"] = Session.TrainResultText;
        }
        catch (Exception ex)
        {
            Session.TrainSuccess    = false;
            Session.TrainResultText = ex.Message;
            TempData["Error"]       = $"Training failed: {ex.Message}";
        }

        return RedirectToPage();
    }

    /// <summary>Scores a proposed video concept using the trained model and stores the result in the session.</summary>
    public async Task<IActionResult> OnPostScoreAsync(string title, string tags, string? niche)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            TempData["Error"] = "Please enter a video title to score.";
            return RedirectToPage();
        }

        var tagList = string.IsNullOrWhiteSpace(tags)
            ? new List<string>()
            : tags.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList();

        try
        {
            var result = await _learner.PredictAsync(title, tagList, "", niche);
            Session.HasScoreResult   = result.Success;
            Session.ScorePercent     = result.Score * 100;
            Session.FeatureImportance = result.FeatureImportance;
            Session.ScoreResultText  = result.Message;

            if (!result.Success)
                TempData["Error"] = result.Message;
        }
        catch (Exception ex)
        {
            Session.HasScoreResult = false;
            TempData["Error"]      = $"Scoring failed: {ex.Message}";
        }

        return RedirectToPage();
    }
}
