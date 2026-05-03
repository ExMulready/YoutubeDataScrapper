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
            migrationBuilder.CreateTable(
                name: "model_weights",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    niche = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    weights_json = table.Column<string>(type: "TEXT", nullable: false),
                    biases_json = table.Column<string>(type: "TEXT", nullable: false),
                    norm_mean_json = table.Column<string>(type: "TEXT", nullable: false),
                    norm_stddev_json = table.Column<string>(type: "TEXT", nullable: false),
                    videos_used = table.Column<int>(type: "INTEGER", nullable: false),
                    epochs_trained = table.Column<int>(type: "INTEGER", nullable: false),
                    final_loss = table.Column<double>(type: "REAL", nullable: false),
                    trained_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_weights", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "video_records",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    video_id = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    niche = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: false),
                    tags = table.Column<string>(type: "TEXT", nullable: false),
                    channel_title = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    published_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    view_count = table.Column<long>(type: "INTEGER", nullable: false),
                    like_count = table.Column<long>(type: "INTEGER", nullable: false),
                    comment_count = table.Column<long>(type: "INTEGER", nullable: false),
                    thumbnail_url = table.Column<string>(type: "TEXT", nullable: false),
                    duration = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    collected_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_video_records", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_model_weights_niche",
                table: "model_weights",
                column: "niche");

            migrationBuilder.CreateIndex(
                name: "IX_model_weights_trained_at",
                table: "model_weights",
                column: "trained_at");

            migrationBuilder.CreateIndex(
                name: "IX_video_records_collected_at",
                table: "video_records",
                column: "collected_at");

            migrationBuilder.CreateIndex(
                name: "IX_video_records_niche",
                table: "video_records",
                column: "niche");

            migrationBuilder.CreateIndex(
                name: "IX_video_records_video_id",
                table: "video_records",
                column: "video_id",
                unique: true);
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
