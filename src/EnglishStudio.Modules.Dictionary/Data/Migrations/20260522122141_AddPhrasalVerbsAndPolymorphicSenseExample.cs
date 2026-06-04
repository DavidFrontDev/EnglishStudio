using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishStudio.Modules.Dictionary.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPhrasalVerbsAndPolymorphicSenseExample : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "WordId",
                table: "Senses",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<int>(
                name: "PhrasalVerbId",
                table: "Senses",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "WordId",
                table: "Examples",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<int>(
                name: "PhrasalVerbId",
                table: "Examples",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PhrasalVerbs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Headword = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Lemma = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    BaseWordId = table.Column<int>(type: "INTEGER", nullable: true),
                    Particle = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CefrLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    FrequencyRank = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhrasalVerbs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhrasalVerbs_Words_BaseWordId",
                        column: x => x.BaseWordId,
                        principalTable: "Words",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Senses_PhrasalVerbId",
                table: "Senses",
                column: "PhrasalVerbId");

            migrationBuilder.CreateIndex(
                name: "IX_Examples_PhrasalVerbId",
                table: "Examples",
                column: "PhrasalVerbId");

            migrationBuilder.CreateIndex(
                name: "IX_PhrasalVerbs_BaseWordId",
                table: "PhrasalVerbs",
                column: "BaseWordId");

            migrationBuilder.CreateIndex(
                name: "IX_PhrasalVerbs_BaseWordId_Particle",
                table: "PhrasalVerbs",
                columns: new[] { "BaseWordId", "Particle" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhrasalVerbs_CefrLevel",
                table: "PhrasalVerbs",
                column: "CefrLevel");

            migrationBuilder.CreateIndex(
                name: "IX_PhrasalVerbs_Headword",
                table: "PhrasalVerbs",
                column: "Headword");

            migrationBuilder.CreateIndex(
                name: "IX_PhrasalVerbs_Lemma",
                table: "PhrasalVerbs",
                column: "Lemma");

            migrationBuilder.CreateIndex(
                name: "IX_PhrasalVerbs_Source",
                table: "PhrasalVerbs",
                column: "Source");

            migrationBuilder.AddForeignKey(
                name: "FK_Examples_PhrasalVerbs_PhrasalVerbId",
                table: "Examples",
                column: "PhrasalVerbId",
                principalTable: "PhrasalVerbs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Senses_PhrasalVerbs_PhrasalVerbId",
                table: "Senses",
                column: "PhrasalVerbId",
                principalTable: "PhrasalVerbs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Examples_PhrasalVerbs_PhrasalVerbId",
                table: "Examples");

            migrationBuilder.DropForeignKey(
                name: "FK_Senses_PhrasalVerbs_PhrasalVerbId",
                table: "Senses");

            migrationBuilder.DropTable(
                name: "PhrasalVerbs");

            migrationBuilder.DropIndex(
                name: "IX_Senses_PhrasalVerbId",
                table: "Senses");

            migrationBuilder.DropIndex(
                name: "IX_Examples_PhrasalVerbId",
                table: "Examples");

            migrationBuilder.DropColumn(
                name: "PhrasalVerbId",
                table: "Senses");

            migrationBuilder.DropColumn(
                name: "PhrasalVerbId",
                table: "Examples");

            migrationBuilder.AlterColumn<int>(
                name: "WordId",
                table: "Senses",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "WordId",
                table: "Examples",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
