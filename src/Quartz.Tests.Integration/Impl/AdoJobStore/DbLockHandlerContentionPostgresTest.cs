using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// Lock contention against PostgreSQL, whose store locks with <c>SELECT … FOR UPDATE</c> through
/// <see cref="PostgreSqlSelectForUpdateLockHandler" />.
/// </summary>
[Category("db-postgres")]
public sealed class DbLockHandlerContentionPostgresTest : DbLockHandlerContentionTestBase
{
    public DbLockHandlerContentionPostgresTest() : base(TestConstants.PostgresProvider)
    {
    }

    protected override Type ExpectedLockHandler => typeof(PostgreSqlSelectForUpdateLockHandler);
}
