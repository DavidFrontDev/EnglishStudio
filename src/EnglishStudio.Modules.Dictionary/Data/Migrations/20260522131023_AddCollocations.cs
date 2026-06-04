using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishStudio.Modules.Dictionary.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCollocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Collocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HeadWordId = table.Column<int>(type: "INTEGER", nullable: true),
                    Headword = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    LinkedText = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Pattern = table.Column<int>(type: "INTEGER", nullable: false),
                    DefinitionEn = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    TranslationRu = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ExampleEn = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    FrequencyRank = table.Column<int>(type: "INTEGER", nullable: true),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Collocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Collocations_Words_HeadWordId",
                        column: x => x.HeadWordId,
                        principalTable: "Words",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Collocations_Headword",
                table: "Collocations",
                column: "Headword");

            migrationBuilder.CreateIndex(
                name: "IX_Collocations_HeadWordId",
                table: "Collocations",
                column: "HeadWordId");

            migrationBuilder.CreateIndex(
                name: "IX_Collocations_LinkedText",
                table: "Collocations",
                column: "LinkedText");

            migrationBuilder.CreateIndex(
                name: "IX_Collocations_LinkedText_Pattern",
                table: "Collocations",
                columns: new[] { "LinkedText", "Pattern" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Collocations_Pattern",
                table: "Collocations",
                column: "Pattern");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Collocations");
        }
    }
}
