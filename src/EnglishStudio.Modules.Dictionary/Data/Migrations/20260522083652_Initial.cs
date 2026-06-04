using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishStudio.Modules.Dictionary.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    NameEn = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    NameRu = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ParentCategoryId = table.Column<int>(type: "INTEGER", nullable: true),
                    OrderIndex = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Categories_Categories_ParentCategoryId",
                        column: x => x.ParentCategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PartsOfSpeech",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    NameEn = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    NameRu = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartsOfSpeech", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    NameRu = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Words",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Headword = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Lemma = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    IpaUk = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    IpaUs = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    FrequencyRank = table.Column<int>(type: "INTEGER", nullable: true),
                    CefrLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    PartOfSpeechId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Words", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Words_PartsOfSpeech_PartOfSpeechId",
                        column: x => x.PartOfSpeechId,
                        principalTable: "PartsOfSpeech",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Senses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WordId = table.Column<int>(type: "INTEGER", nullable: false),
                    DefinitionEn = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    DefinitionRu = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    OrderIndex = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Senses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Senses_Words_WordId",
                        column: x => x.WordId,
                        principalTable: "Words",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserWordProgress",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WordId = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    Stability = table.Column<double>(type: "REAL", nullable: false),
                    Difficulty = table.Column<double>(type: "REAL", nullable: false),
                    LastReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NextReviewAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReviewCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LapseCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserWordProgress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserWordProgress_Words_WordId",
                        column: x => x.WordId,
                        principalTable: "Words",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WordCategories",
                columns: table => new
                {
                    WordId = table.Column<int>(type: "INTEGER", nullable: false),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WordCategories", x => new { x.WordId, x.CategoryId });
                    table.ForeignKey(
                        name: "FK_WordCategories_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WordCategories_Words_WordId",
                        column: x => x.WordId,
                        principalTable: "Words",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WordForms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WordId = table.Column<int>(type: "INTEGER", nullable: false),
                    Form = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    IsIrregular = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WordForms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WordForms_Words_WordId",
                        column: x => x.WordId,
                        principalTable: "Words",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WordTags",
                columns: table => new
                {
                    WordId = table.Column<int>(type: "INTEGER", nullable: false),
                    TagId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WordTags", x => new { x.WordId, x.TagId });
                    table.ForeignKey(
                        name: "FK_WordTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WordTags_Words_WordId",
                        column: x => x.WordId,
                        principalTable: "Words",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Examples",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WordId = table.Column<int>(type: "INTEGER", nullable: false),
                    SenseId = table.Column<int>(type: "INTEGER", nullable: true),
                    TextEn = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    TextRu = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Examples", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Examples_Senses_SenseId",
                        column: x => x.SenseId,
                        principalTable: "Senses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Examples_Words_WordId",
                        column: x => x.WordId,
                        principalTable: "Words",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaAssets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WordId = table.Column<int>(type: "INTEGER", nullable: true),
                    SenseId = table.Column<int>(type: "INTEGER", nullable: true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    LocalPath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Locale = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Attribution = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaAssets_Senses_SenseId",
                        column: x => x.SenseId,
                        principalTable: "Senses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MediaAssets_Words_WordId",
                        column: x => x.WordId,
                        principalTable: "Words",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Translations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SenseId = table.Column<int>(type: "INTEGER", nullable: false),
                    TextRu = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    OrderIndex = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Translations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Translations_Senses_SenseId",
                        column: x => x.SenseId,
                        principalTable: "Senses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Code",
                table: "Categories",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_ParentCategoryId",
                table: "Categories",
                column: "ParentCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Examples_SenseId",
                table: "Examples",
                column: "SenseId");

            migrationBuilder.CreateIndex(
                name: "IX_Examples_WordId",
                table: "Examples",
                column: "WordId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_SenseId",
                table: "MediaAssets",
                column: "SenseId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_WordId",
                table: "MediaAssets",
                column: "WordId");

            migrationBuilder.CreateIndex(
                name: "IX_PartsOfSpeech_Code",
                table: "PartsOfSpeech",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Senses_WordId",
                table: "Senses",
                column: "WordId");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Code",
                table: "Tags",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Translations_SenseId",
                table: "Translations",
                column: "SenseId");

            migrationBuilder.CreateIndex(
                name: "IX_UserWordProgress_NextReviewAt",
                table: "UserWordProgress",
                column: "NextReviewAt");

            migrationBuilder.CreateIndex(
                name: "IX_UserWordProgress_WordId",
                table: "UserWordProgress",
                column: "WordId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WordCategories_CategoryId",
                table: "WordCategories",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_WordForms_Form",
                table: "WordForms",
                column: "Form");

            migrationBuilder.CreateIndex(
                name: "IX_WordForms_WordId",
                table: "WordForms",
                column: "WordId");

            migrationBuilder.CreateIndex(
                name: "IX_Words_CefrLevel",
                table: "Words",
                column: "CefrLevel");

            migrationBuilder.CreateIndex(
                name: "IX_Words_FrequencyRank",
                table: "Words",
                column: "FrequencyRank");

            migrationBuilder.CreateIndex(
                name: "IX_Words_Headword",
                table: "Words",
                column: "Headword");

            migrationBuilder.CreateIndex(
                name: "IX_Words_Headword_PartOfSpeechId",
                table: "Words",
                columns: new[] { "Headword", "PartOfSpeechId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Words_Lemma",
                table: "Words",
                column: "Lemma");

            migrationBuilder.CreateIndex(
                name: "IX_Words_PartOfSpeechId",
                table: "Words",
                column: "PartOfSpeechId");

            migrationBuilder.CreateIndex(
                name: "IX_Words_Source",
                table: "Words",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "IX_WordTags_TagId",
                table: "WordTags",
                column: "TagId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Examples");

            migrationBuilder.DropTable(
                name: "MediaAssets");

            migrationBuilder.DropTable(
                name: "Translations");

            migrationBuilder.DropTable(
                name: "UserWordProgress");

            migrationBuilder.DropTable(
                name: "WordCategories");

            migrationBuilder.DropTable(
                name: "WordForms");

            migrationBuilder.DropTable(
                name: "WordTags");

            migrationBuilder.DropTable(
                name: "Senses");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "Words");

            migrationBuilder.DropTable(
                name: "PartsOfSpeech");
        }
    }
}
