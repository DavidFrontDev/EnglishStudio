using EnglishStudio.Modules.Dictionary.Entities;
using Microsoft.EntityFrameworkCore;

namespace EnglishStudio.Modules.Dictionary.Data;

public class DictionaryDbContext : DbContext
{
    public DictionaryDbContext(DbContextOptions<DictionaryDbContext> options) : base(options)
    {
    }

    public DbSet<Word> Words => Set<Word>();
    public DbSet<PartOfSpeech> PartsOfSpeech => Set<PartOfSpeech>();
    public DbSet<Sense> Senses => Set<Sense>();
    public DbSet<Translation> Translations => Set<Translation>();
    public DbSet<Example> Examples => Set<Example>();
    public DbSet<WordForm> WordForms => Set<WordForm>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<WordCategory> WordCategories => Set<WordCategory>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<WordTag> WordTags => Set<WordTag>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<UserWordProgress> UserWordProgress => Set<UserWordProgress>();
    public DbSet<PhrasalVerb> PhrasalVerbs => Set<PhrasalVerb>();
    public DbSet<Collocation> Collocations => Set<Collocation>();
    public DbSet<ReviewLog> ReviewLogs => Set<ReviewLog>();
    public DbSet<PronunciationAttempt> PronunciationAttempts => Set<PronunciationAttempt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<PartOfSpeech>(b =>
        {
            b.HasIndex(x => x.Code).IsUnique();
            b.Property(x => x.Code).HasMaxLength(16).IsRequired();
            b.Property(x => x.NameEn).HasMaxLength(64).IsRequired();
            b.Property(x => x.NameRu).HasMaxLength(64).IsRequired();
        });

        modelBuilder.Entity<Word>(b =>
        {
            b.HasIndex(x => x.Headword);
            b.HasIndex(x => x.Lemma);
            b.HasIndex(x => x.FrequencyRank);
            b.HasIndex(x => x.CefrLevel);
            b.HasIndex(x => x.Source);
            b.HasIndex(x => new { x.Headword, x.PartOfSpeechId }).IsUnique();

            b.Property(x => x.Headword).HasMaxLength(128).IsRequired();
            b.Property(x => x.Lemma).HasMaxLength(128).IsRequired();
            b.Property(x => x.IpaUk).HasMaxLength(128);
            b.Property(x => x.IpaUs).HasMaxLength(128);
            b.Property(x => x.AudioUkPath).HasMaxLength(256);
            b.Property(x => x.AudioUsPath).HasMaxLength(256);

            b.HasOne(x => x.PartOfSpeech)
                .WithMany(p => p.Words)
                .HasForeignKey(x => x.PartOfSpeechId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Sense>(b =>
        {
            b.HasIndex(x => x.WordId);
            b.HasIndex(x => x.PhrasalVerbId);
            b.Property(x => x.DefinitionEn).HasMaxLength(2000);
            b.Property(x => x.DefinitionRu).HasMaxLength(2000);

            b.HasOne(x => x.Word)
                .WithMany(w => w.Senses)
                .HasForeignKey(x => x.WordId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(x => x.PhrasalVerb)
                .WithMany(pv => pv.Senses)
                .HasForeignKey(x => x.PhrasalVerbId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Translation>(b =>
        {
            b.HasIndex(x => x.SenseId);
            b.Property(x => x.TextRu).HasMaxLength(256).IsRequired();

            b.HasOne(x => x.Sense)
                .WithMany(s => s.Translations)
                .HasForeignKey(x => x.SenseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Example>(b =>
        {
            b.HasIndex(x => x.WordId);
            b.HasIndex(x => x.PhrasalVerbId);
            b.HasIndex(x => x.SenseId);
            b.Property(x => x.TextEn).HasMaxLength(1000).IsRequired();
            b.Property(x => x.TextRu).HasMaxLength(1000);
            b.Property(x => x.Source).HasMaxLength(128);

            b.HasOne(x => x.Word)
                .WithMany(w => w.Examples)
                .HasForeignKey(x => x.WordId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(x => x.PhrasalVerb)
                .WithMany(pv => pv.Examples)
                .HasForeignKey(x => x.PhrasalVerbId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(x => x.Sense)
                .WithMany(s => s.Examples)
                .HasForeignKey(x => x.SenseId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<WordForm>(b =>
        {
            b.HasIndex(x => x.WordId);
            b.HasIndex(x => x.Form);
            b.Property(x => x.Form).HasMaxLength(128).IsRequired();

            b.HasOne(x => x.Word)
                .WithMany(w => w.Forms)
                .HasForeignKey(x => x.WordId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Category>(b =>
        {
            b.HasIndex(x => x.Code).IsUnique();
            b.Property(x => x.Code).HasMaxLength(64).IsRequired();
            b.Property(x => x.NameEn).HasMaxLength(128).IsRequired();
            b.Property(x => x.NameRu).HasMaxLength(128).IsRequired();

            b.HasOne(x => x.ParentCategory)
                .WithMany(p => p.Children)
                .HasForeignKey(x => x.ParentCategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WordCategory>(b =>
        {
            b.HasKey(x => new { x.WordId, x.CategoryId });

            b.HasOne(x => x.Word)
                .WithMany(w => w.WordCategories)
                .HasForeignKey(x => x.WordId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(x => x.Category)
                .WithMany(c => c.WordCategories)
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Tag>(b =>
        {
            b.HasIndex(x => x.Code).IsUnique();
            b.Property(x => x.Code).HasMaxLength(64).IsRequired();
            b.Property(x => x.NameRu).HasMaxLength(128).IsRequired();
        });

        modelBuilder.Entity<WordTag>(b =>
        {
            b.HasKey(x => new { x.WordId, x.TagId });

            b.HasOne(x => x.Word)
                .WithMany(w => w.WordTags)
                .HasForeignKey(x => x.WordId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(x => x.Tag)
                .WithMany(t => t.WordTags)
                .HasForeignKey(x => x.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MediaAsset>(b =>
        {
            b.HasIndex(x => x.WordId);
            b.HasIndex(x => x.SenseId);
            b.Property(x => x.LocalPath).HasMaxLength(512).IsRequired();
            b.Property(x => x.SourceUrl).HasMaxLength(1024);
            b.Property(x => x.Locale).HasMaxLength(16);
            b.Property(x => x.Attribution).HasMaxLength(512);

            b.HasOne(x => x.Word)
                .WithMany(w => w.Media)
                .HasForeignKey(x => x.WordId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(x => x.Sense)
                .WithMany(s => s.Media)
                .HasForeignKey(x => x.SenseId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<UserWordProgress>(b =>
        {
            b.HasIndex(x => x.WordId).IsUnique().HasFilter("\"WordId\" IS NOT NULL");
            b.HasIndex(x => x.PhrasalVerbId).IsUnique().HasFilter("\"PhrasalVerbId\" IS NOT NULL");
            b.HasIndex(x => x.CollocationId).IsUnique().HasFilter("\"CollocationId\" IS NOT NULL");
            b.HasIndex(x => x.NextReviewAt);
            b.HasIndex(x => x.State);

            b.HasOne(x => x.Word)
                .WithMany()
                .HasForeignKey(x => x.WordId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(x => x.PhrasalVerb)
                .WithMany()
                .HasForeignKey(x => x.PhrasalVerbId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(x => x.Collocation)
                .WithMany()
                .HasForeignKey(x => x.CollocationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReviewLog>(b =>
        {
            b.HasIndex(x => x.UserWordProgressId);
            b.HasIndex(x => x.ReviewedAt);

            b.HasOne(x => x.UserWordProgress)
                .WithMany(p => p.ReviewLogs)
                .HasForeignKey(x => x.UserWordProgressId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PronunciationAttempt>(b =>
        {
            b.HasIndex(x => x.WordId);
            b.HasIndex(x => x.RecordedAt);
            b.Property(x => x.TargetText).HasMaxLength(256).IsRequired();
            b.Property(x => x.RecognizedText).HasMaxLength(512).IsRequired();

            b.HasOne(x => x.Word)
                .WithMany()
                .HasForeignKey(x => x.WordId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PhrasalVerb>(b =>
        {
            b.HasIndex(x => x.Headword);
            b.HasIndex(x => x.Lemma);
            b.HasIndex(x => x.BaseWordId);
            b.HasIndex(x => x.CefrLevel);
            b.HasIndex(x => x.Source);
            b.HasIndex(x => new { x.BaseWordId, x.Particle }).IsUnique();

            b.Property(x => x.Headword).HasMaxLength(128).IsRequired();
            b.Property(x => x.Lemma).HasMaxLength(128).IsRequired();
            b.Property(x => x.Particle).HasMaxLength(32).IsRequired();

            b.HasOne(x => x.BaseWord)
                .WithMany()
                .HasForeignKey(x => x.BaseWordId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Collocation>(b =>
        {
            b.HasIndex(x => x.HeadWordId);
            b.HasIndex(x => x.Headword);
            b.HasIndex(x => x.LinkedText);
            b.HasIndex(x => x.Pattern);
            b.HasIndex(x => new { x.LinkedText, x.Pattern }).IsUnique();

            b.Property(x => x.Headword).HasMaxLength(128).IsRequired();
            b.Property(x => x.LinkedText).HasMaxLength(256).IsRequired();
            b.Property(x => x.DefinitionEn).HasMaxLength(512);
            b.Property(x => x.TranslationRu).HasMaxLength(256);
            b.Property(x => x.ExampleEn).HasMaxLength(512);

            b.HasOne(x => x.HeadWord)
                .WithMany()
                .HasForeignKey(x => x.HeadWordId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
