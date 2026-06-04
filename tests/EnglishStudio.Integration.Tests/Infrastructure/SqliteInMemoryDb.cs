using EnglishStudio.Modules.Ielts.Core.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EnglishStudio.Integration.Tests.Infrastructure;

/// <summary>
/// Держит ОТКРЫТОЕ соединение SQLite <c>:memory:</c> на время жизни объекта (закрытие соединения =
/// потеря БД) и раздаёт контексты через <see cref="IDbContextFactory{TContext}"/> поверх него —
/// именно так сервисы (<c>MockSessionService</c> и др.) получают БД в проде.
///
/// FK-энфорсмент выключен (<c>PRAGMA foreign_keys = OFF</c>): mock-тесты линкуют ФЕЙКОВЫЕ
/// childAttemptId, которых нет в дочерних таблицах (TestAttempt/WritingAttempt/SpeakingAttempt) —
/// проверяется логика оркестратора, а не реляционная целостность дочерних модулей.
///
/// Схема поднимается через <c>EnsureCreated()</c> (быстро и провайдеро-независимо); каждый тест
/// создаёт свой экземпляр → полная изоляция.
/// </summary>
public sealed class SqliteInMemoryDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public IDbContextFactory<IeltsDbContext> Factory { get; }

    public SqliteInMemoryDb()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA foreign_keys = OFF;";
            cmd.ExecuteNonQuery();
        }

        var options = new DbContextOptionsBuilder<IeltsDbContext>()
            .UseSqlite(_connection)
            .Options;

        using (var db = new IeltsDbContext(options))
            db.Database.EnsureCreated();

        Factory = new SharedConnectionFactory(options);
    }

    /// <summary>Удобный доступ к контексту в самих ассертах теста.</summary>
    public IeltsDbContext NewContext() => Factory.CreateDbContext();

    public void Dispose() => _connection.Dispose();

    /// <summary>Раздаёт контексты на ОДНОМ внешнем (уже открытом) соединении — EF его не закрывает.</summary>
    private sealed class SharedConnectionFactory : IDbContextFactory<IeltsDbContext>
    {
        private readonly DbContextOptions<IeltsDbContext> _options;
        public SharedConnectionFactory(DbContextOptions<IeltsDbContext> options) => _options = options;
        public IeltsDbContext CreateDbContext() => new(_options);
    }
}
