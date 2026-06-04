using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishStudio.Modules.Ielts.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTestAttemptFeedbackJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FeedbackJson",
                table: "TestAttempts",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FeedbackJson",
                table: "TestAttempts");
        }
    }
}
