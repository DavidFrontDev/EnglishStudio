using EnglishStudio.Modules.Ai;
using EnglishStudio.Modules.Dictionary.Data;
using EnglishStudio.Modules.Dictionary.Entities;
using EnglishStudio.Modules.Dictionary.Srs;
using EnglishStudio.Modules.Reading.Data;
using EnglishStudio.Modules.Reading.Entities;
using EnglishStudio.Modules.Reading.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EnglishStudio.Integration.Tests.Reading;

/// <summary>In-memory ReadingDbContext over one shared SQLite connection (factory like prod).</summary>
public sealed class InMemoryReadingDb : IDisposable
{
    private readonly SqliteConnection _conn;
    public IDbContextFactory<ReadingDbContext> Factory { get; }

    public InMemoryReadingDb()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        var options = new DbContextOptionsBuilder<ReadingDbContext>().UseSqlite(_conn).Options;
        using (var db = new ReadingDbContext(options)) db.Database.EnsureCreated();
        Factory = new Fac(options);
    }

    public ReadingDbContext New() => Factory.CreateDbContext();
    public void Dispose() => _conn.Dispose();

    private sealed class Fac(DbContextOptions<ReadingDbContext> o) : IDbContextFactory<ReadingDbContext>
    {
        public ReadingDbContext CreateDbContext() => new(o);
    }
}

/// <summary>In-memory DictionaryDbContext exposed via an IServiceScopeFactory (PreTeach reads it scoped).</summary>
public sealed class InMemoryDictionaryDb : IDisposable
{
    private readonly SqliteConnection _conn;
    public ServiceProvider Provider { get; }

    public InMemoryDictionaryDb()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        var sc = new ServiceCollection();
        sc.AddDbContext<DictionaryDbContext>(o => o.UseSqlite(_conn));
        Provider = sc.BuildServiceProvider();
        using var scope = Provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<DictionaryDbContext>().Database.EnsureCreated();
    }

    public IServiceScopeFactory ScopeFactory => Provider.GetRequiredService<IServiceScopeFactory>();

    public void Seed(Action<DictionaryDbContext> seed)
    {
        using var scope = Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DictionaryDbContext>();
        seed(db);
        db.SaveChanges();
    }

    public void Dispose() { Provider.Dispose(); _conn.Dispose(); }
}

/// <summary>Returns a fixed body for a single text id; the rest of the library API is unused here.</summary>
public sealed class FakeTextLibraryService(int textId, string body) : ITextLibraryService
{
    public Task<ReadingTextDetail?> GetAsync(int id, CancellationToken ct = default) =>
        Task.FromResult<ReadingTextDetail?>(id == textId
            ? new ReadingTextDetail(textId, "Test", body, 0, CefrLevel.Unknown)
            : null);

    public Task<IReadOnlyList<ReadingTextListItem>> ListAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<int> AddAsync(string title, string body, ReadingSource source = ReadingSource.User, CancellationToken ct = default) => throw new NotImplementedException();
    public Task RenameAsync(int id, string newTitle, CancellationToken ct = default) => throw new NotImplementedException();
    public Task DeleteAsync(int id, CancellationToken ct = default) => throw new NotImplementedException();
    public Task SetHiddenAsync(int id, bool hidden, CancellationToken ct = default) => throw new NotImplementedException();
    public Task TouchOpenedAsync(int id, CancellationToken ct = default) => throw new NotImplementedException();
}

/// <summary>Configurable Claude CLI double. <see cref="Responder"/> returns canned model text.</summary>
public sealed class FakeClaudeCliClient : IClaudeCliClient
{
    public bool IsAvailable { get; set; } = true;
    public Func<string, string>? Responder { get; set; }
    public int CallCount { get; private set; }

    public string? ExecutablePath => "claude";
    public string? Version => "test";
    public Task<bool> RefreshAsync(CancellationToken ct = default) => Task.FromResult(IsAvailable);

    public Task<ClaudeCliResponse> RunAsync(
        string prompt, ClaudeOutputFormat outputFormat = ClaudeOutputFormat.Json, string? resumeSessionId = null,
        TimeSpan? timeout = null, IReadOnlyList<string>? imagePaths = null, CancellationToken ct = default)
    {
        CallCount++;
        var text = Responder?.Invoke(prompt) ?? string.Empty;
        return Task.FromResult(new ClaudeCliResponse(text, null, null, 0, IsError: false));
    }
}

/// <summary>Records SRS additions and a preset "already in training" set; scheduler API unused.</summary>
public sealed class FakeSrsService : ISrsService
{
    public List<int> AddedWordIds { get; } = new();
    public HashSet<int> AlreadyTraining { get; } = new();

    public Task<UserWordProgress?> AddWordAsync(int wordId, CancellationToken ct = default)
    {
        AddedWordIds.Add(wordId);
        AlreadyTraining.Add(wordId);
        return Task.FromResult<UserWordProgress?>(new UserWordProgress { Id = wordId, WordId = wordId });
    }

    public Task<bool> IsInTrainingForWordAsync(int wordId, CancellationToken ct = default) =>
        Task.FromResult(AlreadyTraining.Contains(wordId));

    public Task<UserWordProgress?> AddPhrasalVerbAsync(int phrasalVerbId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<UserWordProgress?> AddCollocationAsync(int collocationId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<List<UserWordProgress>> BuildSessionAsync(int maxNew, int maxReview, DateTime now, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<List<UserWordProgress>> BuildSessionForWordIdsAsync(IReadOnlyCollection<int> wordIds, DateTime now, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<UserWordProgress> RateAsync(int progressId, SrsRating rating, DateTime now, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<SrsStats> GetStatsAsync(DateTime now, CancellationToken ct = default) => throw new NotImplementedException();
}

/// <summary>Hands out fresh word ids for unknown lemmas; records what it was asked to enrich.</summary>
public sealed class FakeEnrichmentService : IDictionaryEnrichmentService
{
    public bool IsAvailable { get; set; } = true;
    public List<string> Enriched { get; } = new();
    private int _nextId = 1000;

    public Task<int?> FetchAndPersistWordAsync(string lemma, string? contextSentence, CancellationToken ct = default)
    {
        if (!IsAvailable) return Task.FromResult<int?>(null);
        Enriched.Add(lemma);
        return Task.FromResult<int?>(_nextId++);
    }

    public Task<string?> TranslatePhraseAsync(string phrase, string? contextSentence, CancellationToken ct = default) => throw new NotImplementedException();
}
