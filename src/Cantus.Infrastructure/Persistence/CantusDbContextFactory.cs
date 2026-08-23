using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cantus.Infrastructure.Persistence;

public class CantusDbContextFactory : IDesignTimeDbContextFactory<CantusDbContext>
{
    public CantusDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CantusDbContext>();
        optionsBuilder.UseSqlite("Data Source=cantus.db");

        return new CantusDbContext(optionsBuilder.Options);
    }
}
