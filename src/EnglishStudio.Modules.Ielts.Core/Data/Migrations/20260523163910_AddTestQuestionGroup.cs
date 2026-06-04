using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishStudio.Modules.Ielts.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTestQuestionGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TestQuestionGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TestPartId = table.Column<int>(type: "INTEGER", nullable: false),
                    OrderInPart = table.Column<int>(type: "INTEGER", nullable: false),
                    Layout = table.Column<int>(type: "INTEGER", nullable: false),
                    InstructionText = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    SharedOptionsJson = table.Column<string>(type: "TEXT", nullable: true),
                    SharedListTitle = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ImagePath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ExampleStem = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ExampleAnswer = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    SummaryTemplate = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestQuestionGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestQuestionGroups_TestParts_TestPartId",
                        column: x => x.TestPartId,
                        principalTable: "TestParts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TestQuestions_GroupId",
                table: "TestQuestions",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_TestQuestionGroups_TestPartId",
                table: "TestQuestionGroups",
                column: "TestPartId");

            migrationBuilder.CreateIndex(
                name: "IX_TestQuestionGroups_TestPartId_OrderInPart",
                table: "TestQuestionGroups",
                columns: new[] { "TestPartId", "OrderInPart" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_TestQuestions_TestQuestionGroups_GroupId",
                table: "TestQuestions",
                column: "GroupId",
                principalTable: "TestQuestionGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TestQuestions_TestQuestionGroups_GroupId",
                table: "TestQuestions");

            migrationBuilder.DropTable(
                name: "TestQuestionGroups");

            migrationBuilder.DropIndex(
                name: "IX_TestQuestions_GroupId",
                table: "TestQuestions");
        }
    }
}
