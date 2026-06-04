using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishStudio.Modules.Ielts.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIeltsCoreSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TestSets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Section = table.Column<int>(type: "INTEGER", nullable: false),
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    AuthorAttribution = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestSets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TestAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TestSetId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    RawScore = table.Column<int>(type: "INTEGER", nullable: false),
                    BandEstimate = table.Column<double>(type: "REAL", nullable: false),
                    IsTrainingMode = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestAttempts_TestSets_TestSetId",
                        column: x => x.TestSetId,
                        principalTable: "TestSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TestParts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TestSetId = table.Column<int>(type: "INTEGER", nullable: false),
                    OrderInTest = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    BodyText = table.Column<string>(type: "TEXT", nullable: true),
                    IntroNoteRu = table.Column<string>(type: "TEXT", nullable: true),
                    AudioPath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ImagePath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestParts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestParts_TestSets_TestSetId",
                        column: x => x.TestSetId,
                        principalTable: "TestSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TestQuestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TestPartId = table.Column<int>(type: "INTEGER", nullable: false),
                    OrderInPart = table.Column<int>(type: "INTEGER", nullable: false),
                    GroupId = table.Column<int>(type: "INTEGER", nullable: true),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Stem = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    OptionsJson = table.Column<string>(type: "TEXT", nullable: true),
                    AnswerKeyJson = table.Column<string>(type: "TEXT", nullable: false),
                    AcceptableAnswersJson = table.Column<string>(type: "TEXT", nullable: true),
                    Points = table.Column<int>(type: "INTEGER", nullable: false),
                    WordLimitMax = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestQuestions_TestParts_TestPartId",
                        column: x => x.TestPartId,
                        principalTable: "TestParts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TestAnswers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TestAttemptId = table.Column<int>(type: "INTEGER", nullable: false),
                    TestQuestionId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserAnswerJson = table.Column<string>(type: "TEXT", nullable: false),
                    IsCorrect = table.Column<bool>(type: "INTEGER", nullable: false),
                    PointsEarned = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestAnswers_TestAttempts_TestAttemptId",
                        column: x => x.TestAttemptId,
                        principalTable: "TestAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TestAnswers_TestQuestions_TestQuestionId",
                        column: x => x.TestQuestionId,
                        principalTable: "TestQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TestAnswers_TestAttemptId",
                table: "TestAnswers",
                column: "TestAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_TestAnswers_TestQuestionId",
                table: "TestAnswers",
                column: "TestQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_TestAttempts_StartedAt",
                table: "TestAttempts",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TestAttempts_TestSetId",
                table: "TestAttempts",
                column: "TestSetId");

            migrationBuilder.CreateIndex(
                name: "IX_TestParts_TestSetId",
                table: "TestParts",
                column: "TestSetId");

            migrationBuilder.CreateIndex(
                name: "IX_TestParts_TestSetId_OrderInTest",
                table: "TestParts",
                columns: new[] { "TestSetId", "OrderInTest" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestQuestions_TestPartId",
                table: "TestQuestions",
                column: "TestPartId");

            migrationBuilder.CreateIndex(
                name: "IX_TestQuestions_TestPartId_OrderInPart",
                table: "TestQuestions",
                columns: new[] { "TestPartId", "OrderInPart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestSets_Code",
                table: "TestSets",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestSets_Section",
                table: "TestSets",
                column: "Section");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TestAnswers");

            migrationBuilder.DropTable(
                name: "TestAttempts");

            migrationBuilder.DropTable(
                name: "TestQuestions");

            migrationBuilder.DropTable(
                name: "TestParts");

            migrationBuilder.DropTable(
                name: "TestSets");
        }
    }
}
