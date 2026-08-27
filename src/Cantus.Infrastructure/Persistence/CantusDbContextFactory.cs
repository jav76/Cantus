using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cantus.Infrastructure.Persistence;

public class CantusDbContextFactory : IDesignTimeDbContextFactory<CantusDbContext>
{
    public CantusDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<CantusDbContext> optionsBuilder = new();
        optionsBuilder.UseSqlite("Data Source=cantus.db");

        return new CantusDbContext(optionsBuilder.Options);
    }
}
