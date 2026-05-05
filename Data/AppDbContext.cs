using Microsoft.EntityFrameworkCore;
using YoutubeResearchMcp.Data.Entities;

namespace YoutubeResearchMcp.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<VideoRecord>  VideoRecords => Set<VideoRecord>();
    public DbSet<ModelWeights> ModelWeights => Set<ModelWeights>();

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
