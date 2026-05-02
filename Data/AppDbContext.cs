using Microsoft.EntityFrameworkCore;
using YoutubeResearchMcp.Data.Entities;

namespace YoutubeResearchMcp.Data;

public class AppDbContext : DbContext
{
    private readonly string _dbPath;

    public AppDbContext(string dbPath)
    {
        _dbPath = dbPath;
    }

    public DbSet<VideoRecord>   VideoRecords => Set<VideoRecord>();
    public DbSet<ModelWeights>  ModelWeights => Set<ModelWeights>();

    /// <summary>Configures SQLite as the database provider using the path supplied at construction.</summary>
    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseSqlite($"Data Source={_dbPath}");

    /// <summary>Adds unique and covering indexes to the VideoRecord and ModelWeights tables.</summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<VideoRecord>(e =>
        {
            e.HasIndex(v => v.VideoId).IsUnique();
            e.HasIndex(v => v.Niche);
            e.HasIndex(v => v.CollectedAt);
        });

        modelBuilder.Entity<ModelWeights>(e =>
        {
            e.HasIndex(m => m.Niche);
            e.HasIndex(m => m.TrainedAt);
        });
    }
}
