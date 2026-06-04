using EnglishStudio.Modules.Dictionary.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EnglishStudio.Modules.Reading.Data;

/// <summary>Design-time factory so <c>dotnet ef migrations</c> can build the context.</summary>
public class ReadingDbContextFactory : IDesignTimeDbContextFactory<ReadingDbContext>
{
    public ReadingDbContext CreateDbContext(string[] args)
    {
        DictionaryPaths.EnsureDirectoriesExist();

        var options = new DbContextOptionsBuilder<ReadingDbContext>()
            .UseSqlite(
                DictionaryPaths.SqliteConnectionString,
                b => b.MigrationsHistoryTable("__EFMigrationsHistory_Reading"))
            .Options;

        return new ReadingDbContext(options);
    }
}
