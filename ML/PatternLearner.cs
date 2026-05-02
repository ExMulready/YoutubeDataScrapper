using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using YoutubeResearchMcp.Data;
using YoutubeResearchMcp.Data.Entities;
using YoutubeResearchMcp.Models;

namespace YoutubeResearchMcp.ML;

public class PatternLearner
{
    private readonly AppDbContext _db;
    private readonly Dictionary<string, NeuralNetwork> _models = new();

    public PatternLearner(AppDbContext db) => _db = db;

    /// <summary>Persists new videos to the database, skipping duplicates by video ID.</summary>
    public async Task<SaveResult> SaveVideosAsync(string niche, List<VideoMetadata> videos)
    {
        int added = 0, skipped = 0;

        foreach (var v in videos)
        {
            bool exists = await _db.VideoRecords.AnyAsync(r => r.VideoId == v.VideoId);
            if (exists) { skipped++; continue; }

            _db.VideoRecords.Add(new VideoRecord
            {
                VideoId      = v.VideoId,
                Niche        = niche,
                Title        = v.Title,
                Description  = v.Description,
                TagsCsv      = string.Join("|", v.Tags),
                ChannelTitle = v.ChannelTitle,
                PublishedAt  = DateTime.TryParse(v.PublishedAt, out var dt) ? dt : null,
                ViewCount    = v.ViewCount,
                LikeCount    = v.LikeCount,
                CommentCount = v.CommentCount,
                ThumbnailUrl = v.ThumbnailUrl,
                Duration     = v.Duration,
                CollectedAt  = DateTime.UtcNow
            });
            added++;
        }

        await _db.SaveChangesAsync();

        int total = await _db.VideoRecords
            .Where(r => niche == "" || r.Niche == niche)
            .CountAsync();

        return new SaveResult(added, skipped, total);
    }

    /// <summary>
    /// Trains the neural network on stored video records and persists the resulting weights.
    /// Scopes training to <paramref name="niche"/> when provided; otherwise trains on all data.
    /// </summary>
    public async Task<TrainResult> TrainAsync(string? niche, int epochs = 150, int minSamples = 10)
    {
        var query = _db.VideoRecords.AsQueryable();
        if (!string.IsNullOrWhiteSpace(niche))
            query = query.Where(r => r.Niche == niche);

        var records = await query
            .Where(r => r.ViewCount > 0)
            .OrderByDescending(r => r.CollectedAt)
            .Take(5000)
            .ToListAsync();

        if (records.Count < minSamples)
            return new TrainResult(false,
                $"Not enough training data: {records.Count} videos (need at least {minSamples}). " +
                "Run save_research_data first.",
                records.Count, 0, 0, 0);

        var features = records.Select(r => VideoFeatureExtractor.Extract(r)).ToList();
        var labels   = records.Select(r => VideoFeatureExtractor.LabelFromViewCount(r.ViewCount)).ToList();

        var nn   = new NeuralNetwork();
        var loss = nn.Train(features, labels, epochs: epochs, batchSize: 32);
        var snapshot = nn.ToSnapshot();

        var weightsRecord = new ModelWeights
        {
            Niche          = string.IsNullOrWhiteSpace(niche) ? null : niche,
            WeightsJson    = JsonSerializer.Serialize(snapshot.Weights),
            BiasesJson     = JsonSerializer.Serialize(snapshot.Biases),
            NormMeanJson   = JsonSerializer.Serialize(snapshot.NormMean),
            NormStddevJson = JsonSerializer.Serialize(snapshot.NormStdDev),
            VideosUsed     = records.Count,
            EpochsTrained  = epochs,
            FinalLoss      = loss,
            TrainedAt      = DateTime.UtcNow
        };
        _db.ModelWeights.Add(weightsRecord);
        await _db.SaveChangesAsync();

        _models[niche ?? ""] = nn;

        return new TrainResult(true, "Training complete.", records.Count, epochs, loss,
            weightsRecord.Id);
    }

    /// <summary>Predicts the performance score for a proposed video concept using the trained model.</summary>
    public async Task<PredictResult> PredictAsync(
        string title, List<string> tags, string description, string? niche)
    {
        var nn = await GetOrLoadModelAsync(niche);
        if (nn is null)
            return new PredictResult(false, "No trained model found. Run train_pattern_model first.",
                0, null);

        var features   = VideoFeatureExtractor.ExtractFromConcept(title, tags, description);
        var score      = nn.Predict(features);
        var importance = ComputeFeatureImportance(nn, features);

        return new PredictResult(true, "OK", score, importance);
    }

    /// <summary>Returns metadata for the most recently trained model scoped to the given niche.</summary>
    public async Task<ModelInfo?> GetLatestModelInfoAsync(string? niche)
    {
        var q = _db.ModelWeights.AsQueryable();
        if (!string.IsNullOrWhiteSpace(niche))
            q = q.Where(m => m.Niche == niche);
        else
            q = q.Where(m => m.Niche == null);

        var latest = await q.OrderByDescending(m => m.TrainedAt).FirstOrDefaultAsync();
        if (latest is null) return null;

        return new ModelInfo(
            latest.Niche ?? "global",
            latest.VideosUsed,
            latest.EpochsTrained,
            latest.FinalLoss,
            latest.TrainedAt);
    }

    /// <summary>Returns the count of stored video records, optionally scoped to a niche.</summary>
    public async Task<int> GetVideoCountAsync(string? niche)
    {
        var q = _db.VideoRecords.AsQueryable();
        if (!string.IsNullOrWhiteSpace(niche))
            q = q.Where(r => r.Niche == niche);
        return await q.CountAsync();
    }

    /// <summary>Returns stored video records with optional title substring and niche filters.</summary>
    public async Task<List<VideoRecord>> GetAllVideosAsync(string? titleFilter = null, string? niche = null)
    {
        var q = _db.VideoRecords.AsQueryable();
        if (!string.IsNullOrWhiteSpace(niche))
            q = q.Where(r => r.Niche == niche);
        if (!string.IsNullOrWhiteSpace(titleFilter))
            q = q.Where(r => r.Title.Contains(titleFilter));
        return await q.OrderByDescending(r => r.CollectedAt).Take(2000).ToListAsync();
    }

    /// <summary>Returns the model from the in-memory cache, loading from the database if not yet cached.</summary>
    private async Task<NeuralNetwork?> GetOrLoadModelAsync(string? niche)
    {
        var cacheKey = niche ?? "";
        if (_models.TryGetValue(cacheKey, out var cached))
            return cached;

        var q = _db.ModelWeights.AsQueryable();
        if (!string.IsNullOrWhiteSpace(niche))
            q = q.Where(m => m.Niche == niche);
        else
            q = q.Where(m => m.Niche == null);

        var row = await q.OrderByDescending(m => m.TrainedAt).FirstOrDefaultAsync();
        if (row is null) return null;

        var snap = new NetworkSnapshot
        {
            Weights    = JsonSerializer.Deserialize<double[][][]>(row.WeightsJson)!,
            Biases     = JsonSerializer.Deserialize<double[][]>(row.BiasesJson)!,
            NormMean   = JsonSerializer.Deserialize<double[]>(row.NormMeanJson)!,
            NormStdDev = JsonSerializer.Deserialize<double[]>(row.NormStddevJson)!
        };

        var nn = new NeuralNetwork(snap);
        _models[cacheKey] = nn;
        return nn;
    }

    /// <summary>Approximates feature importance by perturbing each input by +0.1 and measuring the score delta.</summary>
    private static Dictionary<string, double> ComputeFeatureImportance(
        NeuralNetwork nn, double[] baseFeatures)
    {
        var baseScore  = nn.Predict(baseFeatures);
        var importance = new Dictionary<string, double>();
        var names      = VideoFeatureExtractor.FeatureNames;

        for (int i = 0; i < baseFeatures.Length; i++)
        {
            var perturbed = (double[])baseFeatures.Clone();
            perturbed[i] += 0.1;
            var delta = nn.Predict(perturbed) - baseScore;
            importance[names[i]] = Math.Round(delta, 4);
        }

        return importance.OrderByDescending(kv => Math.Abs(kv.Value))
                         .ToDictionary(kv => kv.Key, kv => kv.Value);
    }
}

public record SaveResult(int Added, int Skipped, int TotalInDb);

public record TrainResult(
    bool Success, string Message,
    int VideosUsed, int Epochs, double FinalLoss, int ModelId);

public record PredictResult(
    bool Success, string Message,
    double Score,
    Dictionary<string, double>? FeatureImportance);

public record ModelInfo(
    string Niche, int VideosUsed, int Epochs, double FinalLoss, DateTime TrainedAt);
