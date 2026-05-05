using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YoutubeResearchMcp.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "model_weights" (
                    "id" INTEGER NOT NULL CONSTRAINT "PK_model_weights" PRIMARY KEY AUTOINCREMENT,
                    "niche" TEXT NULL,
                    "weights_json" TEXT NOT NULL,
                    "biases_json" TEXT NOT NULL,
                    "norm_mean_json" TEXT NOT NULL,
                    "norm_stddev_json" TEXT NOT NULL,
                    "videos_used" INTEGER NOT NULL,
                    "epochs_trained" INTEGER NOT NULL,
                    "final_loss" REAL NOT NULL,
                    "trained_at" TEXT NOT NULL
                );
                """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "video_records" (
                    "id" INTEGER NOT NULL CONSTRAINT "PK_video_records" PRIMARY KEY AUTOINCREMENT,
                    "video_id" TEXT NOT NULL,
                    "niche" TEXT NOT NULL,
                    "title" TEXT NOT NULL,
                    "description" TEXT NOT NULL,
                    "tags" TEXT NOT NULL,
                    "channel_title" TEXT NOT NULL,
                    "published_at" TEXT NULL,
                    "view_count" INTEGER NOT NULL,
                    "like_count" INTEGER NOT NULL,
                    "comment_count" INTEGER NOT NULL,
                    "thumbnail_url" TEXT NOT NULL,
                    "duration" TEXT NOT NULL,
                    "collected_at" TEXT NOT NULL
                );
                """);

            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_model_weights_niche\" ON \"model_weights\" (\"niche\");");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_model_weights_trained_at\" ON \"model_weights\" (\"trained_at\");");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_video_records_collected_at\" ON \"video_records\" (\"collected_at\");");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_video_records_niche\" ON \"video_records\" (\"niche\");");
            migrationBuilder.Sql("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_video_records_video_id\" ON \"video_records\" (\"video_id\");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "model_weights");

            migrationBuilder.DropTable(
                name: "video_records");
        }
    }
}
