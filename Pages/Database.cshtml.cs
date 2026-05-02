using Microsoft.AspNetCore.Mvc.RazorPages;
using YoutubeResearchMcp.Data.Entities;
using YoutubeResearchMcp.ML;

namespace YoutubeResearchMcp.Pages;

public class DatabaseModel : PageModel
{
    private readonly PatternLearner _learner;

    public DatabaseModel(PatternLearner learner)
    {
        _learner = learner;
    }

    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)]
    public string? TitleFilter { get; set; }

    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)]
    public string? Niche { get; set; }

    public List<VideoRecord> Videos { get; private set; } = [];

    /// <summary>Loads video records filtered by the bound title substring and niche query parameters.</summary>
    public async Task OnGetAsync()
    {
        Videos = await _learner.GetAllVideosAsync(TitleFilter, Niche);
    }
}
