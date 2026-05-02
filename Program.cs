using System.Diagnostics;
using System.IO;
using YoutubeResearchMcp.Data;
using YoutubeResearchMcp.ML;
using YoutubeResearchMcp.Services;
using YoutubeResearchMcp.Settings;

var appSettings = SettingsService.Load();
Directory.CreateDirectory(Path.GetDirectoryName(appSettings.DatabasePath)!);

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages();

builder.Services.AddSingleton(appSettings);

builder.Services.AddSingleton(sp =>
    new YouTubeService(sp.GetRequiredService<AppSettings>()));

builder.Services.AddSingleton<PatternAnalysisService>();
builder.Services.AddSingleton<LocalIdeaGeneratorService>();

builder.Services.AddSingleton(sp =>
{
    var db = new AppDbContext(sp.GetRequiredService<AppSettings>().DatabasePath);
    db.Database.EnsureCreated();
    return db;
});

builder.Services.AddSingleton<PatternLearner>();
builder.Services.AddSingleton<ResearchSession>();

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

const string url = "http://localhost:5050";

app.Lifetime.ApplicationStarted.Register(() =>
{
    try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
    catch { }
});

Console.WriteLine($"YouTube Research Studio running at {url}");
app.Run(url);
