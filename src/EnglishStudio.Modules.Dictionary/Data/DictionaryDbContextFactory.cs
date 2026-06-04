using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EnglishStudio.Modules.Dictionary.Data;

public class DictionaryDbContextFactory : IDesignTimeDbContextFactory<DictionaryDbContext>
{
    public DictionaryDbContext CreateDbContext(string[] args)
    {
        DictionaryPaths.EnsureDirectoriesExist();

        var options = new DbContextOptionsBuilder<DictionaryDbContext>()
            .UseSqlite(DictionaryPaths.SqliteConnectionString)
            .Options;

        return new DictionaryDbContext(options);
    }
}
