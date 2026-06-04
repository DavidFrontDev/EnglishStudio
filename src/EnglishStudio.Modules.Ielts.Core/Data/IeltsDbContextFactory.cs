using EnglishStudio.Modules.Dictionary.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EnglishStudio.Modules.Ielts.Core.Data;

public class IeltsDbContextFactory : IDesignTimeDbContextFactory<IeltsDbContext>
{
    public IeltsDbContext CreateDbContext(string[] args)
    {
        DictionaryPaths.EnsureDirectoriesExist();

        var options = new DbContextOptionsBuilder<IeltsDbContext>()
            .UseSqlite(
                DictionaryPaths.SqliteConnectionString,
                b => b.MigrationsHistoryTable("__EFMigrationsHistory_Ielts"))
            .Options;

        return new IeltsDbContext(options);
    }
}
