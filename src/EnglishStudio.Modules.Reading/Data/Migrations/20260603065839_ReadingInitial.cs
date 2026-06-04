using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishStudio.Modules.Reading.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReadingInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReadingTexts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    BodyText = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    WordCount = table.Column<int>(type: "INTEGER", nullable: false),
                    EstimatedCefr = table.Column<int>(type: "INTEGER", nullable: false),
                    Tags = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastOpenedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingTexts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReadingSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReadingTextId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DurationSec = table.Column<int>(type: "INTEGER", nullable: false),
                    WordsRead = table.Column<int>(type: "INTEGER", nullable: false),
                    Wpm = table.Column<double>(type: "REAL", nullable: false),
                    AccuracyPct = table.Column<double>(type: "REAL", nullable: false),
                    AudioPath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Completed = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadingSessions_ReadingTexts_ReadingTextId",
                        column: x => x.ReadingTextId,
                        principalTable: "ReadingTexts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReadingWordStats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReadingSessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    TokenIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    Skipped = table.Column<bool>(type: "INTEGER", nullable: false),
                    Mispronounced = table.Column<bool>(type: "INTEGER", nullable: false),
                    Score = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingWordStats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadingWordStats_ReadingSessions_ReadingSessionId",
                        column: x => x.ReadingSessionId,
                        principalTable: "ReadingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingSessions_ReadingTextId",
                table: "ReadingSessions",
                column: "ReadingTextId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingSessions_StartedAt",
                table: "ReadingSessions",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingTexts_CreatedAt",
                table: "ReadingTexts",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingTexts_Source",
                table: "ReadingTexts",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingWordStats_ReadingSessionId",
                table: "ReadingWordStats",
                column: "ReadingSessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReadingWordStats");

            migrationBuilder.DropTable(
                name: "ReadingSessions");

            migrationBuilder.DropTable(
                name: "ReadingTexts");
        }
    }
}
