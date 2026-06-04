using EnglishStudio.Modules.Ielts.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace EnglishStudio.Modules.Ielts.Core.Data;

public class IeltsDbContext : DbContext
{
    public IeltsDbContext(DbContextOptions<IeltsDbContext> options) : base(options)
    {
    }

    public DbSet<TestSet> TestSets => Set<TestSet>();
    public DbSet<TestPart> TestParts => Set<TestPart>();
    public DbSet<TestQuestionGroup> TestQuestionGroups => Set<TestQuestionGroup>();
    public DbSet<TestQuestion> TestQuestions => Set<TestQuestion>();
    public DbSet<TestAttempt> TestAttempts => Set<TestAttempt>();
    public DbSet<TestAnswer> TestAnswers => Set<TestAnswer>();

    public DbSet<WritingTask> WritingTasks => Set<WritingTask>();
    public DbSet<WritingModelAnswer> WritingModelAnswers => Set<WritingModelAnswer>();
    public DbSet<WritingAttempt> WritingAttempts => Set<WritingAttempt>();

    public DbSet<SpeakingQuestionBank> SpeakingQuestionBanks => Set<SpeakingQuestionBank>();
    public DbSet<SpeakingQuestion> SpeakingQuestions => Set<SpeakingQuestion>();
    public DbSet<SpeakingAttempt> SpeakingAttempts => Set<SpeakingAttempt>();
    public DbSet<SpeakingResponse> SpeakingResponses => Set<SpeakingResponse>();

    public DbSet<MockAttempt> MockAttempts => Set<MockAttempt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TestSet>(b =>
        {
            b.HasIndex(x => x.Code).IsUnique();
            b.HasIndex(x => x.Section);
            b.Property(x => x.Code).HasMaxLength(64).IsRequired();
            b.Property(x => x.Title).HasMaxLength(256).IsRequired();
            b.Property(x => x.AuthorAttribution).HasMaxLength(256);
        });

        modelBuilder.Entity<TestPart>(b =>
        {
            b.HasIndex(x => x.TestSetId);
            b.HasIndex(x => new { x.TestSetId, x.OrderInTest }).IsUnique();
            b.Property(x => x.Title).HasMaxLength(256).IsRequired();
            b.Property(x => x.AudioPath).HasMaxLength(512);
            b.Property(x => x.ImagePath).HasMaxLength(512);

            b.HasOne(x => x.TestSet)
                .WithMany(s => s.Parts)
                .HasForeignKey(x => x.TestSetId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TestQuestionGroup>(b =>
        {
            b.HasIndex(x => x.TestPartId);
            b.HasIndex(x => new { x.TestPartId, x.OrderInPart }).IsUnique();
            b.Property(x => x.InstructionText).HasMaxLength(2000);
            b.Property(x => x.SharedListTitle).HasMaxLength(128);
            b.Property(x => x.ImagePath).HasMaxLength(512);
            b.Property(x => x.ExampleStem).HasMaxLength(512);
            b.Property(x => x.ExampleAnswer).HasMaxLength(256);

            b.HasOne(x => x.TestPart)
                .WithMany(p => p.Groups)
                .HasForeignKey(x => x.TestPartId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TestQuestion>(b =>
        {
            b.HasIndex(x => x.TestPartId);
            b.HasIndex(x => new { x.TestPartId, x.OrderInPart }).IsUnique();
            b.HasIndex(x => x.GroupId);
            b.Property(x => x.Stem).HasMaxLength(2000).IsRequired();
            b.Property(x => x.AnswerKeyJson).IsRequired();

            b.HasOne(x => x.TestPart)
                .WithMany(p => p.Questions)
                .HasForeignKey(x => x.TestPartId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(x => x.Group)
                .WithMany(g => g.Questions)
                .HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TestAttempt>(b =>
        {
            b.HasIndex(x => x.TestSetId);
            b.HasIndex(x => x.StartedAt);

            b.HasOne(x => x.TestSet)
                .WithMany()
                .HasForeignKey(x => x.TestSetId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TestAnswer>(b =>
        {
            b.HasIndex(x => x.TestAttemptId);
            b.HasIndex(x => x.TestQuestionId);
            // One answer per (attempt, question). Backstop for the in-process semaphore;
            // also blocks any future code path that tries to insert a duplicate.
            b.HasIndex(x => new { x.TestAttemptId, x.TestQuestionId }).IsUnique();

            b.HasOne(x => x.TestAttempt)
                .WithMany(a => a.Answers)
                .HasForeignKey(x => x.TestAttemptId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(x => x.TestQuestion)
                .WithMany()
                .HasForeignKey(x => x.TestQuestionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WritingTask>(b =>
        {
            b.HasIndex(x => x.Code).IsUnique();
            b.HasIndex(x => x.Kind);
            b.HasIndex(x => x.TestSetId);
            b.HasIndex(x => new { x.TestSetId, x.OrderInSet });
            b.Property(x => x.Code).HasMaxLength(64).IsRequired();
            b.Property(x => x.PromptText).IsRequired();
            b.Property(x => x.ImagePath).HasMaxLength(512);
            b.Property(x => x.TopicCategory).HasMaxLength(64);

            b.HasOne(x => x.TestSet)
                .WithMany()
                .HasForeignKey(x => x.TestSetId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<WritingModelAnswer>(b =>
        {
            b.HasIndex(x => x.WritingTaskId);
            b.HasIndex(x => new { x.WritingTaskId, x.BandLevel }).IsUnique();
            b.Property(x => x.AnswerText).IsRequired();

            b.HasOne(x => x.WritingTask)
                .WithMany(t => t.ModelAnswers)
                .HasForeignKey(x => x.WritingTaskId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WritingAttempt>(b =>
        {
            b.HasIndex(x => x.WritingTaskId);
            b.HasIndex(x => x.StartedAt);
            b.Property(x => x.UserText).IsRequired();

            b.HasOne(x => x.WritingTask)
                .WithMany()
                .HasForeignKey(x => x.WritingTaskId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SpeakingQuestionBank>(b =>
        {
            b.HasIndex(x => x.TopicCode).IsUnique();
            b.HasIndex(x => x.Part);
            b.Property(x => x.TopicCode).HasMaxLength(64).IsRequired();
            b.Property(x => x.TopicLabel).HasMaxLength(256).IsRequired();
            b.Property(x => x.CueCardPrompt).HasMaxLength(2000);

            // Part 3 → Part 2 self-link; never cascade — deleting a Part 2 bank should not
            // wipe its Part 3 follow-ups silently.
            b.HasOne(x => x.LinkedPart2Bank)
                .WithMany()
                .HasForeignKey(x => x.LinkedPart2BankId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SpeakingQuestion>(b =>
        {
            b.HasIndex(x => x.BankId);
            b.HasIndex(x => new { x.BankId, x.OrderInBank }).IsUnique();
            b.Property(x => x.Text).HasMaxLength(2000).IsRequired();

            b.HasOne(x => x.Bank)
                .WithMany(bk => bk.Questions)
                .HasForeignKey(x => x.BankId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(x => x.FollowUpToQuestion)
                .WithMany()
                .HasForeignKey(x => x.FollowUpToQuestionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SpeakingAttempt>(b =>
        {
            b.HasIndex(x => x.StartedAt);
            b.HasIndex(x => x.TopicBankId);

            b.HasOne(x => x.TopicBank)
                .WithMany()
                .HasForeignKey(x => x.TopicBankId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SpeakingResponse>(b =>
        {
            b.HasIndex(x => x.SpeakingAttemptId);
            b.HasIndex(x => x.SpeakingQuestionId);
            b.HasIndex(x => new { x.SpeakingAttemptId, x.OrderInAttempt }).IsUnique();
            b.Property(x => x.AudioPath).HasMaxLength(512).IsRequired();

            b.HasOne(x => x.Attempt)
                .WithMany(a => a.Responses)
                .HasForeignKey(x => x.SpeakingAttemptId)
                .OnDelete(DeleteBehavior.Cascade);

            // Restrict to break the Attempt→Responses→Question→Bank cycle and protect
            // historical attempts from question-bank cleanup.
            b.HasOne(x => x.Question)
                .WithMany()
                .HasForeignKey(x => x.SpeakingQuestionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MockAttempt>(b =>
        {
            b.HasIndex(x => x.StartedAt);
            b.Property(x => x.ModeCode).HasMaxLength(32).IsRequired();
            b.Property(x => x.SectionsJson).IsRequired();

            // FK к дочерним attempt'ам. Mock — зависимая сторона, поэтому удаление mock никогда
            // не каскадит на дочерние; SetNull обнуляет ссылку, если сам дочерний attempt удалят
            // из своего модуля. Reading и Listening оба → TestAttempt (две разных навигации).
            b.HasOne(x => x.ListeningAttempt)
                .WithMany()
                .HasForeignKey(x => x.ListeningAttemptId)
                .OnDelete(DeleteBehavior.SetNull);

            b.HasOne(x => x.ReadingAttempt)
                .WithMany()
                .HasForeignKey(x => x.ReadingAttemptId)
                .OnDelete(DeleteBehavior.SetNull);

            b.HasOne(x => x.WritingAttempt)
                .WithMany()
                .HasForeignKey(x => x.WritingAttemptId)
                .OnDelete(DeleteBehavior.SetNull);

            b.HasOne(x => x.SpeakingAttempt)
                .WithMany()
                .HasForeignKey(x => x.SpeakingAttemptId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
