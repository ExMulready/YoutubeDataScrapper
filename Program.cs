using System.Diagnostics;
using System.IO;
using Microsoft.EntityFrameworkCore;
using YoutubeResearchMcp.Data;
using YoutubeResearchMcp.ML;
using YoutubeResearchMcp.Services;
using YoutubeResearchMcp.Settings;

var appSettings = SettingsService.Load();
Directory.CreateDirectory(Path.GetDirectoryName(appSettings.DatabasePath)!);

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages();

// ── Infrastructure ─────────────────────────────────────────���───────────────
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(o =>
{
    o.IdleTimeout        = TimeSpan.FromHours(4);
    o.Cookie.HttpOnly    = true;
    o.Cookie.IsEssential = true;
});
builder.Services.AddHttpContextAccessor();

// ── Settings ───────────────────────────────────────────────────────────────
builder.Services.AddSingleton(appSettings);

// ── Database — one DbContext per operation via factory ─────────────────────
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite($"Data Source={appSettings.DatabasePath}"));

// ── YouTube + Anthropic ────────────────────────────────────────────────────
builder.Services.AddSingleton<YouTubeService>();
// anthropic-version header is set globally; x-api-key is sent per-request from AppSettings
// so a key saved on the Settings page takes effect immediately without a restart.
builder.Services.AddHttpClient<AnthropicService>((_, client) =>
{
    client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
});

// ── ML pipeline ───────────────────────────────────────────────────────────
builder.Services.AddSingleton<IFeatureExtractor, VideoFeatureExtractor>();
builder.Services.AddSingleton<PatternLearner>();

// ── Application services ──────────────────────────────────────────────────
builder.Services.AddSingleton<PatternAnalysisService>();
// Scoped because AnthropicService is transient (typed HttpClient lifetime).
builder.Services.AddScoped<LocalIdeaGeneratorService>();

// ── Session state — one ResearchSession per browser session ID ─────────────
builder.Services.AddSingleton<ResearchSessionStore>();
builder.Services.AddScoped<ResearchSession>(sp =>
{
    var store = sp.GetRequiredService<ResearchSessionStore>();
    var http  = sp.GetRequiredService<IHttpContextAccessor>();
    var id    = http.HttpContext?.Session.Id ?? "default";
    return store.GetOrCreate(id);
});

var app = builder.Build();

// ── Apply pending schema migrations on startup ─────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await using var db = factory.CreateDbContext();
    db.Database.Migrate();
}

// ── Startup validation ─────────────────────────────────────────────────────
var startupLogger = app.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("Startup");

if (string.IsNullOrWhiteSpace(appSettings.YouTubeApiKey))
    startupLogger.LogCritical(
        "YOUTUBE_API_KEY is not configured — searches will fail. Open Settings to add it.");

if (string.IsNullOrWhiteSpace(appSettings.AnthropicApiKey))
    startupLogger.LogWarning(
        "ANTHROPIC_API_KEY is not configured — idea generation will use local templates only.");
else
    startupLogger.LogInformation("Claude-powered idea generation is enabled (claude-sonnet-4-6).");

// ── Middleware pipeline ────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
    app.UseDeveloperExceptionPage();
else
    app.UseExceptionHandler("/Error");

app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.MapRazorPages();

const string url = "http://localhost:5050";

app.Lifetime.ApplicationStarted.Register(() =>
{
    try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
    catch { }
});

Console.WriteLine($"YouTube Research Studio running at {url}");
app.Run(url);
