using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishStudio.Modules.Ielts.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWritingSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WritingTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    PromptText = table.Column<string>(type: "TEXT", nullable: false),
                    ChartSpecJson = table.Column<string>(type: "TEXT", nullable: true),
                    ImagePath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    MinWords = table.Column<int>(type: "INTEGER", nullable: false),
                    RecommendedMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    ChartType = table.Column<int>(type: "INTEGER", nullable: false),
                    TopicCategory = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingTasks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WritingTaskId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    WordCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    UserText = table.Column<string>(type: "TEXT", nullable: false),
                    BandTaskAchievement = table.Column<double>(type: "REAL", nullable: true),
                    BandCoherence = table.Column<double>(type: "REAL", nullable: true),
                    BandLexical = table.Column<double>(type: "REAL", nullable: true),
                    BandGrammar = table.Column<double>(type: "REAL", nullable: true),
                    BandOverall = table.Column<double>(type: "REAL", nullable: true),
                    FeedbackJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WritingAttempts_WritingTasks_WritingTaskId",
                        column: x => x.WritingTaskId,
                        principalTable: "WritingTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WritingModelAnswers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WritingTaskId = table.Column<int>(type: "INTEGER", nullable: false),
                    BandLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    AnswerText = table.Column<string>(type: "TEXT", nullable: false),
                    AnnotationJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingModelAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WritingModelAnswers_WritingTasks_WritingTaskId",
                        column: x => x.WritingTaskId,
                        principalTable: "WritingTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WritingAttempts_StartedAt",
                table: "WritingAttempts",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WritingAttempts_WritingTaskId",
                table: "WritingAttempts",
                column: "WritingTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingModelAnswers_WritingTaskId",
                table: "WritingModelAnswers",
                column: "WritingTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingModelAnswers_WritingTaskId_BandLevel",
                table: "WritingModelAnswers",
                columns: new[] { "WritingTaskId", "BandLevel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WritingTasks_Code",
                table: "WritingTasks",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WritingTasks_Kind",
                table: "WritingTasks",
                column: "Kind");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WritingAttempts");

            migrationBuilder.DropTable(
                name: "WritingModelAnswers");

            migrationBuilder.DropTable(
                name: "WritingTasks");
        }
    }
}
