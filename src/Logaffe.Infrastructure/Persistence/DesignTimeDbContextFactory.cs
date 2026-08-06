using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Logaffe.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build the model without starting the Api project.
/// </summary>
/// <remarks>
/// The connection string below is never connected to: adding or scripting a
/// migration reads the model and not the database. Keeping the tooling out of
/// the composition root is what makes a migration something that can be added
/// without a running installation anywhere.
/// </remarks>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<LogaffeDbContext>
{
    public LogaffeDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<LogaffeDbContext>()
            .UseNpgsql("Host=design-time;Database=logaffe;Username=logaffe")
            .Options;

        return new LogaffeDbContext(options);
    }
}
