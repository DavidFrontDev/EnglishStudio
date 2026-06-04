using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishStudio.Modules.Dictionary.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSrsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserWordProgress_WordId",
                table: "UserWordProgress");

            migrationBuilder.AlterColumn<int>(
                name: "WordId",
                table: "UserWordProgress",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<int>(
                name: "CollocationId",
                table: "UserWordProgress",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "UserWordProgress",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "PhrasalVerbId",
                table: "UserWordProgress",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "UserWordProgress",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateTable(
                name: "ReviewLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserWordProgressId = table.Column<int>(type: "INTEGER", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Rating = table.Column<int>(type: "INTEGER", nullable: false),
                    StateBefore = table.Column<int>(type: "INTEGER", nullable: false),
                    StateAfter = table.Column<int>(type: "INTEGER", nullable: false),
                    StabilityBefore = table.Column<double>(type: "REAL", nullable: false),
                    StabilityAfter = table.Column<double>(type: "REAL", nullable: false),
                    DifficultyBefore = table.Column<double>(type: "REAL", nullable: false),
                    DifficultyAfter = table.Column<double>(type: "REAL", nullable: false),
                    ElapsedDays = table.Column<double>(type: "REAL", nullable: false),
                    ScheduledIntervalDays = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewLogs_UserWordProgress_UserWordProgressId",
                        column: x => x.UserWordProgressId,
                        principalTable: "UserWordProgress",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserWordProgress_CollocationId",
                table: "UserWordProgress",
                column: "CollocationId",
                unique: true,
                filter: "\"CollocationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserWordProgress_PhrasalVerbId",
                table: "UserWordProgress",
                column: "PhrasalVerbId",
                unique: true,
                filter: "\"PhrasalVerbId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserWordProgress_State",
                table: "UserWordProgress",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_UserWordProgress_WordId",
                table: "UserWordProgress",
                column: "WordId",
                unique: true,
                filter: "\"WordId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewLogs_ReviewedAt",
                table: "ReviewLogs",
                column: "ReviewedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewLogs_UserWordProgressId",
                table: "ReviewLogs",
                column: "UserWordProgressId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserWordProgress_Collocations_CollocationId",
                table: "UserWordProgress",
                column: "CollocationId",
                principalTable: "Collocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserWordProgress_PhrasalVerbs_PhrasalVerbId",
                table: "UserWordProgress",
                column: "PhrasalVerbId",
                principalTable: "PhrasalVerbs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserWordProgress_Collocations_CollocationId",
                table: "UserWordProgress");

            migrationBuilder.DropForeignKey(
                name: "FK_UserWordProgress_PhrasalVerbs_PhrasalVerbId",
                table: "UserWordProgress");

            migrationBuilder.DropTable(
                name: "ReviewLogs");

            migrationBuilder.DropIndex(
                name: "IX_UserWordProgress_CollocationId",
                table: "UserWordProgress");

            migrationBuilder.DropIndex(
                name: "IX_UserWordProgress_PhrasalVerbId",
                table: "UserWordProgress");

            migrationBuilder.DropIndex(
                name: "IX_UserWordProgress_State",
                table: "UserWordProgress");

            migrationBuilder.DropIndex(
                name: "IX_UserWordProgress_WordId",
                table: "UserWordProgress");

            migrationBuilder.DropColumn(
                name: "CollocationId",
                table: "UserWordProgress");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "UserWordProgress");

            migrationBuilder.DropColumn(
                name: "PhrasalVerbId",
                table: "UserWordProgress");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "UserWordProgress");

            migrationBuilder.AlterColumn<int>(
                name: "WordId",
                table: "UserWordProgress",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserWordProgress_WordId",
                table: "UserWordProgress",
                column: "WordId",
                unique: true);
        }
    }
}
