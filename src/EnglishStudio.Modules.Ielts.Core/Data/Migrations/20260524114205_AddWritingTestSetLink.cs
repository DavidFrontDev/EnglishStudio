using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishStudio.Modules.Ielts.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWritingTestSetLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OrderInSet",
                table: "WritingTasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TestSetId",
                table: "WritingTasks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WritingTasks_TestSetId",
                table: "WritingTasks",
                column: "TestSetId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingTasks_TestSetId_OrderInSet",
                table: "WritingTasks",
                columns: new[] { "TestSetId", "OrderInSet" });

            migrationBuilder.AddForeignKey(
                name: "FK_WritingTasks_TestSets_TestSetId",
                table: "WritingTasks",
                column: "TestSetId",
                principalTable: "TestSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WritingTasks_TestSets_TestSetId",
                table: "WritingTasks");

            migrationBuilder.DropIndex(
                name: "IX_WritingTasks_TestSetId",
                table: "WritingTasks");

            migrationBuilder.DropIndex(
                name: "IX_WritingTasks_TestSetId_OrderInSet",
                table: "WritingTasks");

            migrationBuilder.DropColumn(
                name: "OrderInSet",
                table: "WritingTasks");

            migrationBuilder.DropColumn(
                name: "TestSetId",
                table: "WritingTasks");
        }
    }
}
