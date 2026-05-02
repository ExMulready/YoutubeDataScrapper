using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace YoutubeResearchMcp.Data.Entities;

[Table("model_weights")]
public class ModelWeights
{
    [Key, Column("id")]
    public int Id { get; set; }

    [Column("niche"), MaxLength(255)]
    public string? Niche { get; set; }

    [Column("weights_json"), Required]
    public string WeightsJson { get; set; } = "";

    [Column("biases_json"), Required]
    public string BiasesJson { get; set; } = "";

    [Column("norm_mean_json"), Required]
    public string NormMeanJson { get; set; } = "";

    [Column("norm_stddev_json"), Required]
    public string NormStddevJson { get; set; } = "";

    [Column("videos_used")]
    public int VideosUsed { get; set; }

    [Column("epochs_trained")]
    public int EpochsTrained { get; set; }

    [Column("final_loss")]
    public double FinalLoss { get; set; }

    [Column("trained_at")]
    public DateTime TrainedAt { get; set; } = DateTime.UtcNow;
}
