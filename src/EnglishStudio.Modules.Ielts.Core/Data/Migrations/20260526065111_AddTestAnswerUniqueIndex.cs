using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishStudio.Modules.Ielts.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTestAnswerUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Defensive: legacy data may contain duplicate (attempt, question) rows from the
            // old race-prone upsert path. Keep MAX(Id) per group — without an UpdatedAt column
            // that's the closest proxy for "most recent answer the user actually submitted",
            // which is preferable to the first insert from the race winner.
            migrationBuilder.Sql(@"
                DELETE FROM TestAnswers
                WHERE Id NOT IN (
                    SELECT MAX(Id) FROM TestAnswers
                    GROUP BY TestAttemptId, TestQuestionId
                );");

            migrationBuilder.CreateIndex(
                name: "IX_TestAnswers_TestAttemptId_TestQuestionId",
                table: "TestAnswers",
                columns: new[] { "TestAttemptId", "TestQuestionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TestAnswers_TestAttemptId_TestQuestionId",
                table: "TestAnswers");
        }
    }
}
