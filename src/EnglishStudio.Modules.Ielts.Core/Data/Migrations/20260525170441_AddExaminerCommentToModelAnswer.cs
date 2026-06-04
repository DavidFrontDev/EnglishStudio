using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishStudio.Modules.Ielts.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExaminerCommentToModelAnswer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExaminerComment",
                table: "WritingModelAnswers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExaminerComment",
                table: "WritingModelAnswers");
        }
    }
}
