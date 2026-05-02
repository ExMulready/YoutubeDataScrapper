using Microsoft.AspNetCore.Mvc.RazorPages;
using YoutubeResearchMcp.Services;

namespace YoutubeResearchMcp.Pages;

public class AnalysisModel : PageModel
{
    public readonly ResearchSession Session;

    public AnalysisModel(ResearchSession session)
    {
        Session = session;
    }

    public void OnGet() { }
}
