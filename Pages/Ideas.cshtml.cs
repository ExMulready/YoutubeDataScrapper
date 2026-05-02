using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using YoutubeResearchMcp.Services;

namespace YoutubeResearchMcp.Pages;

public class IdeasModel : PageModel
{
    private readonly LocalIdeaGeneratorService _generator;
    public readonly ResearchSession Session;

    public IdeasModel(LocalIdeaGeneratorService generator, ResearchSession session)
    {
        _generator = generator;
        Session    = session;
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostGenerateAsync()
    {
        if (Session.CurrentPatterns == null)
        {
            TempData["Error"] = "Run pattern analysis first before generating ideas.";
            return RedirectToPage();
        }

        try
        {
            Session.CurrentIdeas = await _generator.GenerateVideoIdeasAsync(
                Session.CurrentNiche, Session.CurrentPatterns);
            TempData["Success"] = $"Generated {Session.CurrentIdeas.Ideas.Count} video ideas for \"{Session.CurrentNiche}\".";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Failed to generate ideas: {ex.Message}";
        }

        return RedirectToPage();
    }
}
