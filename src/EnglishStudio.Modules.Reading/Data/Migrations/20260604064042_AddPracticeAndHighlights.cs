using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishStudio.Modules.Reading.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPracticeAndHighlights : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReadingPracticeItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReadingTextId = table.Column<int>(type: "INTEGER", nullable: false),
                    WordId = table.Column<int>(type: "INTEGER", nullable: false),
                    Headword = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingPracticeItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadingPracticeItems_ReadingTexts_ReadingTextId",
                        column: x => x.ReadingTextId,
                        principalTable: "ReadingTexts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TextHighlights",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReadingTextId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartOffset = table.Column<int>(type: "INTEGER", nullable: false),
                    Length = table.Column<int>(type: "INTEGER", nullable: false),
                    Quote = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Color = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TextHighlights", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TextHighlights_ReadingTexts_ReadingTextId",
                        column: x => x.ReadingTextId,
                        principalTable: "ReadingTexts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingPracticeItems_ReadingTextId_WordId",
                table: "ReadingPracticeItems",
                columns: new[] { "ReadingTextId", "WordId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TextHighlights_ReadingTextId",
                table: "TextHighlights",
                column: "ReadingTextId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReadingPracticeItems");

            migrationBuilder.DropTable(
                name: "TextHighlights");
        }
    }
}
