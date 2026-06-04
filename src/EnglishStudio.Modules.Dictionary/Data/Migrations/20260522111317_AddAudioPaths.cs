using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishStudio.Modules.Dictionary.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAudioPaths : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AudioUkPath",
                table: "Words",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AudioUsPath",
                table: "Words",
                type: "TEXT",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AudioUkPath",
                table: "Words");

            migrationBuilder.DropColumn(
                name: "AudioUsPath",
                table: "Words");
        }
    }
}
