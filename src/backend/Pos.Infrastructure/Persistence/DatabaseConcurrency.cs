using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Pos.Infrastructure.Persistence;

public static class DatabaseConcurrency
{
    public static bool IsSerializationFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is DbUpdateConcurrencyException or PostgresException { SqlState: "40001" or "40P01" }) return true;
        return false;
    }
}
