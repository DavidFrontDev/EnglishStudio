using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishStudio.Modules.Dictionary.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPronunciationAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PronunciationAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WordId = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetText = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    RecognizedText = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: false),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PronunciationAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PronunciationAttempts_Words_WordId",
                        column: x => x.WordId,
                        principalTable: "Words",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PronunciationAttempts_RecordedAt",
                table: "PronunciationAttempts",
                column: "RecordedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PronunciationAttempts_WordId",
                table: "PronunciationAttempts",
                column: "WordId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PronunciationAttempts");
        }
    }
}
