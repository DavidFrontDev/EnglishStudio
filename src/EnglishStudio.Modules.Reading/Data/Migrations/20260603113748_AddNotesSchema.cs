using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishStudio.Modules.Reading.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNotesSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TextBookmarks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReadingTextId = table.Column<int>(type: "INTEGER", nullable: false),
                    WordIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TextBookmarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TextBookmarks_ReadingTexts_ReadingTextId",
                        column: x => x.ReadingTextId,
                        principalTable: "ReadingTexts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TextNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReadingTextId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartOffset = table.Column<int>(type: "INTEGER", nullable: false),
                    Length = table.Column<int>(type: "INTEGER", nullable: false),
                    Quote = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    NoteText = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Color = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TextNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TextNotes_ReadingTexts_ReadingTextId",
                        column: x => x.ReadingTextId,
                        principalTable: "ReadingTexts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TextBookmarks_ReadingTextId",
                table: "TextBookmarks",
                column: "ReadingTextId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TextNotes_ReadingTextId",
                table: "TextNotes",
                column: "ReadingTextId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TextBookmarks");

            migrationBuilder.DropTable(
                name: "TextNotes");
        }
    }
}
