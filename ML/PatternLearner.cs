using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using YoutubeResearchMcp.Data;
using YoutubeResearchMcp.Data.Entities;
using YoutubeResearchMcp.Models;

namespace YoutubeResearchMcp.ML;

public class PatternLearner
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IFeatureExtractor               _extractor;
    private readonly LruCache<string, NeuralNetwork> _models = new(20);

    public PatternLearner(IDbContextFactory<AppDbContext> factory, IFeatureExtractor extractor)
    {
        _factory   = factory;
        _extractor = extractor;
    }

    /// <summary>Persists new videos to the database, skipping duplicates by video ID.</summary>
    public async Task<SaveResult> SaveVideosAsync(string niche, List<VideoMetadata> videos)
    {
        await using var db = _factory.CreateDbContext();

        int added = 0, skipped = 0;

        // Query existing IDs in chunks of 500 to stay under SQLite's variable limit.
        var incomingIds = videos.Select(v => v.VideoId).Distinct().ToList();
        var existingIds = new HashSet<string>();
        foreach (var chunk in incomingIds.Chunk(500))
        {
            var found = await db.VideoRecords
                .Where(r => chunk.Contains(r.VideoId))
                .Select(r => r.VideoId)
                .ToListAsync();
            existingIds.UnionWith(found);
        }

        foreach (var v in videos)
        {
            // Skip both DB duplicates and within-batch duplicates (pagination can repeat videos).
            if (!existingIds.Add(v.VideoId)) { skipped++; continue; }

            db.VideoRecords.Add(new VideoRecord
            {
                VideoId      = v.VideoId,
                Niche        = niche,
                Title        = v.Title,
                Description  = v.Description,
                TagsCsv      = JsonSerializer.Serialize(v.Tags),
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

        await db.SaveChangesAsync();

        var totalQuery = db.VideoRecords.AsQueryable();
        if (!string.IsNullOrEmpty(niche))
            totalQuery = totalQuery.Where(r => r.Niche == niche);
        int total = await totalQuery.CountAsync();

        return new SaveResult(added, skipped, total);
    }

    /// <summary>
    /// Trains the neural network on stored video records and persists the resulting weights.
    /// Scopes training to <paramref name="niche"/> when provided; otherwise trains on all data.
    /// </summary>
    public async Task<TrainResult> TrainAsync(string? niche, int epochs = 150, int minSamples = 10)
    {
        await using var db = _factory.CreateDbContext();

        var query = db.VideoRecords.AsQueryable();
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

        var features = records.Select(r => _extractor.Extract(r)).ToList();
        var labels   = records.Select(r => _extractor.LabelFromViewCount(r.ViewCount)).ToList();

        var nn   = new NeuralNetwork();
        var loss = nn.Train(features, labels, epochs: epochs, batchSize: 32,
            progress: new Progress<TrainingProgress>(p =>
                Console.WriteLine($"[Training] Epoch {p.Epoch}/{p.TotalEpochs} — loss: {p.Loss:F6}")));

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
        db.ModelWeights.Add(weightsRecord);
        await db.SaveChangesAsync();

        _models.Set(niche ?? "", nn);

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

        var features   = _extractor.ExtractFromConcept(title, tags, description);
        var score      = nn.Predict(features);
        var importance = ComputeFeatureImportance(nn, features);

        return new PredictResult(true, "OK", score, importance);
    }

    /// <summary>Returns metadata for the most recently trained model scoped to the given niche.</summary>
    public async Task<ModelInfo?> GetLatestModelInfoAsync(string? niche)
    {
        await using var db = _factory.CreateDbContext();

        var q = db.ModelWeights.AsQueryable();
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
        await using var db = _factory.CreateDbContext();
        var q = db.VideoRecords.AsQueryable();
        if (!string.IsNullOrWhiteSpace(niche))
            q = q.Where(r => r.Niche == niche);
        return await q.CountAsync();
    }

    /// <summary>Returns stored video records with optional title substring and niche filters.</summary>
    public async Task<List<VideoRecord>> GetAllVideosAsync(string? titleFilter = null, string? niche = null)
    {
        await using var db = _factory.CreateDbContext();
        var q = db.VideoRecords.AsQueryable();
        if (!string.IsNullOrWhiteSpace(niche))
            q = q.Where(r => r.Niche == niche);
        if (!string.IsNullOrWhiteSpace(titleFilter))
            q = q.Where(r => r.Title.Contains(titleFilter));
        return await q.OrderByDescending(r => r.CollectedAt).Take(2000).ToListAsync();
    }

    /// <summary>Returns the model from the LRU cache, loading from the database if not yet cached.</summary>
    private async Task<NeuralNetwork?> GetOrLoadModelAsync(string? niche)
    {
        var cacheKey = niche ?? "";
        if (_models.TryGetValue(cacheKey, out var cached))
            return cached;

        await using var db = _factory.CreateDbContext();

        var q = db.ModelWeights.AsQueryable();
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
        _models.Set(cacheKey, nn);
        return nn;
    }

    /// <summary>Approximates feature importance by perturbing each input by +0.1 and measuring the score delta.</summary>
    private Dictionary<string, double> ComputeFeatureImportance(NeuralNetwork nn, double[] baseFeatures)
    {
        var baseScore  = nn.Predict(baseFeatures);
        var importance = new Dictionary<string, double>();
        var names      = _extractor.FeatureNames;

        for (int i = 0; i < baseFeatures.Length; i++)
        {
            var perturbed = (double[])baseFeatures.Clone();
            perturbed[i] = Math.Clamp(perturbed[i] + 0.1, 0.0, 1.0);
            var delta = nn.Predict(perturbed) - baseScore;
            importance[names[i]] = Math.Round(delta, 4);
        }

        return importance.OrderByDescending(kv => Math.Abs(kv.Value))
                         .ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    /// <summary>Thread-safe fixed-capacity LRU cache backed by a linked list and dictionary.</summary>
    private sealed class LruCache<TKey, TValue> where TKey : notnull
    {
        private readonly int _capacity;
        private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map;
        private readonly LinkedList<(TKey Key, TValue Value)> _list;
        private readonly object _lock = new();

        public LruCache(int capacity)
        {
            _capacity = capacity;
            _map      = new Dictionary<TKey, LinkedListNode<(TKey, TValue)>>(capacity);
            _list     = new LinkedList<(TKey, TValue)>();
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            lock (_lock)
            {
                if (_map.TryGetValue(key, out var node))
                {
                    _list.Remove(node);
                    _list.AddFirst(node);
                    value = node.Value.Value;
                    return true;
                }
            }
            value = default!;
            return false;
        }

        public void Set(TKey key, TValue value)
        {
            lock (_lock)
            {
                if (_map.TryGetValue(key, out var existing))
                {
                    _list.Remove(existing);
                    _map.Remove(key);
                }
                else if (_map.Count >= _capacity)
                {
                    var lru = _list.Last!;
                    _list.RemoveLast();
                    _map.Remove(lru.Value.Key);
                }
                var node = _list.AddFirst((key, value));
                _map[key] = node;
            }
        }
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
