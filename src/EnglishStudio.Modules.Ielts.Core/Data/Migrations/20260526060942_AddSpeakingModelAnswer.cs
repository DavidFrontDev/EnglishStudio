using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishStudio.Modules.Ielts.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSpeakingModelAnswer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ModelAnswer",
                table: "SpeakingQuestions",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ModelAnswer",
                table: "SpeakingQuestions");
        }
    }
}
