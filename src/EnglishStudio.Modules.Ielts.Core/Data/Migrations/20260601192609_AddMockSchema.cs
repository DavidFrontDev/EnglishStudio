using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishStudio.Modules.Ielts.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMockSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MockAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ModeCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Book = table.Column<int>(type: "INTEGER", nullable: true),
                    TestNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CurrentSection = table.Column<int>(type: "INTEGER", nullable: true),
                    ListeningAttemptId = table.Column<int>(type: "INTEGER", nullable: true),
                    ReadingAttemptId = table.Column<int>(type: "INTEGER", nullable: true),
                    WritingAttemptId = table.Column<int>(type: "INTEGER", nullable: true),
                    SpeakingAttemptId = table.Column<int>(type: "INTEGER", nullable: true),
                    SectionsJson = table.Column<string>(type: "TEXT", nullable: false),
                    ListeningBand = table.Column<double>(type: "REAL", nullable: true),
                    ReadingBand = table.Column<double>(type: "REAL", nullable: true),
                    WritingBand = table.Column<double>(type: "REAL", nullable: true),
                    SpeakingBand = table.Column<double>(type: "REAL", nullable: true),
                    OverallBand = table.Column<double>(type: "REAL", nullable: true),
                    IsPartial = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MockAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MockAttempts_SpeakingAttempts_SpeakingAttemptId",
                        column: x => x.SpeakingAttemptId,
                        principalTable: "SpeakingAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MockAttempts_TestAttempts_ListeningAttemptId",
                        column: x => x.ListeningAttemptId,
                        principalTable: "TestAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MockAttempts_TestAttempts_ReadingAttemptId",
                        column: x => x.ReadingAttemptId,
                        principalTable: "TestAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MockAttempts_WritingAttempts_WritingAttemptId",
                        column: x => x.WritingAttemptId,
                        principalTable: "WritingAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MockAttempts_ListeningAttemptId",
                table: "MockAttempts",
                column: "ListeningAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_MockAttempts_ReadingAttemptId",
                table: "MockAttempts",
                column: "ReadingAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_MockAttempts_SpeakingAttemptId",
                table: "MockAttempts",
                column: "SpeakingAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_MockAttempts_StartedAt",
                table: "MockAttempts",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MockAttempts_WritingAttemptId",
                table: "MockAttempts",
                column: "WritingAttemptId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MockAttempts");
        }
    }
}
