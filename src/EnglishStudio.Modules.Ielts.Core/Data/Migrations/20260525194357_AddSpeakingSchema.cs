using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishStudio.Modules.Ielts.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSpeakingSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SpeakingQuestionBanks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Part = table.Column<int>(type: "INTEGER", nullable: false),
                    TopicCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TopicLabel = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CueCardPrompt = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CueCardSubpointsJson = table.Column<string>(type: "TEXT", nullable: true),
                    LinkedPart2BankId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingQuestionBanks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeakingQuestionBanks_SpeakingQuestionBanks_LinkedPart2BankId",
                        column: x => x.LinkedPart2BankId,
                        principalTable: "SpeakingQuestionBanks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    TopicBankId = table.Column<int>(type: "INTEGER", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    BandFluencyCoherence = table.Column<double>(type: "REAL", nullable: true),
                    BandLexicalResource = table.Column<double>(type: "REAL", nullable: true),
                    BandGrammar = table.Column<double>(type: "REAL", nullable: true),
                    BandPronunciation = table.Column<double>(type: "REAL", nullable: true),
                    BandOverall = table.Column<double>(type: "REAL", nullable: true),
                    FeedbackJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeakingAttempts_SpeakingQuestionBanks_TopicBankId",
                        column: x => x.TopicBankId,
                        principalTable: "SpeakingQuestionBanks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingQuestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BankId = table.Column<int>(type: "INTEGER", nullable: false),
                    OrderInBank = table.Column<int>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    FollowUpToQuestionId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeakingQuestions_SpeakingQuestionBanks_BankId",
                        column: x => x.BankId,
                        principalTable: "SpeakingQuestionBanks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SpeakingQuestions_SpeakingQuestions_FollowUpToQuestionId",
                        column: x => x.FollowUpToQuestionId,
                        principalTable: "SpeakingQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingResponses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SpeakingAttemptId = table.Column<int>(type: "INTEGER", nullable: false),
                    SpeakingQuestionId = table.Column<int>(type: "INTEGER", nullable: false),
                    OrderInAttempt = table.Column<int>(type: "INTEGER", nullable: false),
                    AudioPath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Transcript = table.Column<string>(type: "TEXT", nullable: true),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    WpmRate = table.Column<double>(type: "REAL", nullable: true),
                    PauseRatio = table.Column<double>(type: "REAL", nullable: true),
                    FillerCount = table.Column<int>(type: "INTEGER", nullable: true),
                    TypeTokenRatio = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingResponses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeakingResponses_SpeakingAttempts_SpeakingAttemptId",
                        column: x => x.SpeakingAttemptId,
                        principalTable: "SpeakingAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SpeakingResponses_SpeakingQuestions_SpeakingQuestionId",
                        column: x => x.SpeakingQuestionId,
                        principalTable: "SpeakingQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingAttempts_StartedAt",
                table: "SpeakingAttempts",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingAttempts_TopicBankId",
                table: "SpeakingAttempts",
                column: "TopicBankId");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingQuestionBanks_LinkedPart2BankId",
                table: "SpeakingQuestionBanks",
                column: "LinkedPart2BankId");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingQuestionBanks_Part",
                table: "SpeakingQuestionBanks",
                column: "Part");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingQuestionBanks_TopicCode",
                table: "SpeakingQuestionBanks",
                column: "TopicCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingQuestions_BankId",
                table: "SpeakingQuestions",
                column: "BankId");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingQuestions_BankId_OrderInBank",
                table: "SpeakingQuestions",
                columns: new[] { "BankId", "OrderInBank" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingQuestions_FollowUpToQuestionId",
                table: "SpeakingQuestions",
                column: "FollowUpToQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingResponses_SpeakingAttemptId",
                table: "SpeakingResponses",
                column: "SpeakingAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingResponses_SpeakingAttemptId_OrderInAttempt",
                table: "SpeakingResponses",
                columns: new[] { "SpeakingAttemptId", "OrderInAttempt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingResponses_SpeakingQuestionId",
                table: "SpeakingResponses",
                column: "SpeakingQuestionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SpeakingResponses");

            migrationBuilder.DropTable(
                name: "SpeakingAttempts");

            migrationBuilder.DropTable(
                name: "SpeakingQuestions");

            migrationBuilder.DropTable(
                name: "SpeakingQuestionBanks");
        }
    }
}
