using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishStudio.Modules.Reading.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddComprehensionSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ComprehensionQuestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReadingTextId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Prompt = table.Column<string>(type: "TEXT", nullable: false),
                    OptionsJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CorrectOptionIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    ModelAnswer = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    OrderIndex = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComprehensionQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComprehensionQuestions_ReadingTexts_ReadingTextId",
                        column: x => x.ReadingTextId,
                        principalTable: "ReadingTexts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComprehensionQuestions_ReadingTextId",
                table: "ComprehensionQuestions",
                column: "ReadingTextId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ComprehensionQuestions");
        }
    }
}
