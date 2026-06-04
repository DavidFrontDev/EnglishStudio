using EnglishStudio.Modules.Reading.Entities;
using Microsoft.EntityFrameworkCore;

namespace EnglishStudio.Modules.Reading.Data;

/// <summary>
/// Owns the study "Чтение" module's tables. Shares the dictionary.db SQLite file
/// with the other modules but keeps its own migrations history table
/// (<c>__EFMigrationsHistory_Reading</c>) — same convention as IeltsDbContext.
/// </summary>
public class ReadingDbContext : DbContext
{
    public ReadingDbContext(DbContextOptions<ReadingDbContext> options) : base(options)
    {
    }

    public DbSet<ReadingText> ReadingTexts => Set<ReadingText>();
    public DbSet<ReadingSession> ReadingSessions => Set<ReadingSession>();
    public DbSet<ReadingWordStat> ReadingWordStats => Set<ReadingWordStat>();
    public DbSet<ComprehensionQuestion> ComprehensionQuestions => Set<ComprehensionQuestion>();
    public DbSet<TextNote> TextNotes => Set<TextNote>();
    public DbSet<TextBookmark> TextBookmarks => Set<TextBookmark>();
    public DbSet<TextHighlight> TextHighlights => Set<TextHighlight>();
    public DbSet<ReadingPracticeItem> ReadingPracticeItems => Set<ReadingPracticeItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ReadingText>(b =>
        {
            b.HasIndex(x => x.CreatedAt);
            b.HasIndex(x => x.Source);
            b.Property(x => x.Title).HasMaxLength(256).IsRequired();
            b.Property(x => x.BodyText).IsRequired();
            b.Property(x => x.Tags).HasMaxLength(512);
        });

        modelBuilder.Entity<ReadingSession>(b =>
        {
            b.HasIndex(x => x.ReadingTextId);
            b.HasIndex(x => x.StartedAt);
            b.Property(x => x.AudioPath).HasMaxLength(512);

            b.HasOne(x => x.ReadingText)
                .WithMany(t => t.Sessions)
                .HasForeignKey(x => x.ReadingTextId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReadingWordStat>(b =>
        {
            b.HasIndex(x => x.ReadingSessionId);

            b.HasOne(x => x.ReadingSession)
                .WithMany(s => s.WordStats)
                .HasForeignKey(x => x.ReadingSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ComprehensionQuestion>(b =>
        {
            b.HasIndex(x => x.ReadingTextId);
            b.Property(x => x.Prompt).IsRequired();
            b.Property(x => x.OptionsJson).HasMaxLength(4000);
            b.Property(x => x.ModelAnswer).HasMaxLength(4000);

            // No inverse navigation on ReadingText (keeps that entity untouched); cascade on delete.
            b.HasOne<ReadingText>()
                .WithMany()
                .HasForeignKey(x => x.ReadingTextId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TextNote>(b =>
        {
            b.HasIndex(x => x.ReadingTextId);
            b.Property(x => x.Quote).HasMaxLength(2000);
            b.Property(x => x.NoteText).HasMaxLength(4000);
            b.Property(x => x.Color).HasMaxLength(32);

            b.HasOne<ReadingText>()
                .WithMany()
                .HasForeignKey(x => x.ReadingTextId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TextBookmark>(b =>
        {
            // One bookmark per text.
            b.HasIndex(x => x.ReadingTextId).IsUnique();

            b.HasOne<ReadingText>()
                .WithMany()
                .HasForeignKey(x => x.ReadingTextId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TextHighlight>(b =>
        {
            b.HasIndex(x => x.ReadingTextId);
            b.Property(x => x.Quote).HasMaxLength(2000);
            b.Property(x => x.Color).HasMaxLength(32);

            b.HasOne<ReadingText>()
                .WithMany()
                .HasForeignKey(x => x.ReadingTextId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReadingPracticeItem>(b =>
        {
            // At most one pool entry per (text, word).
            b.HasIndex(x => new { x.ReadingTextId, x.WordId }).IsUnique();
            b.Property(x => x.Headword).HasMaxLength(256);

            // WordId references a dictionary Word in DictionaryDbContext (same file, no FK here).
            b.HasOne<ReadingText>()
                .WithMany()
                .HasForeignKey(x => x.ReadingTextId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
