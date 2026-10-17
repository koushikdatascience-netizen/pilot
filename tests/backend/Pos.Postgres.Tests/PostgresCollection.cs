using Xunit;

namespace Pos.Postgres.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresCollection
{
    public const string Name = "postgres";
}
