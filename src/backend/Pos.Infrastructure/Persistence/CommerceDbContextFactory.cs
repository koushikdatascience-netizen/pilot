using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Pos.Infrastructure.Persistence;

public sealed class CommerceDbContextFactory : IDesignTimeDbContextFactory<CommerceDbContext>
{
    public CommerceDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Set ConnectionStrings__Postgres or POSTGRES_CONNECTION_STRING explicitly for EF tools.");

        var options = new DbContextOptionsBuilder<CommerceDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new CommerceDbContext(options);
    }
}
