using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// Lock contention against SQL Server, whose store locks through
/// <see cref="SelectForUpdateLockHandler" /> carrying the <c>(UPDLOCK,ROWLOCK)</c> statement the store
/// defaults it to — a different statement from PostgreSQL's <c>SELECT … FOR UPDATE</c>, and the reason
/// the fixture runs on both.
/// </summary>
[Category("db-sqlserver")]
public sealed class DbLockHandlerContentionSqlServerTest : DbLockHandlerContentionTestBase
{
    public DbLockHandlerContentionSqlServerTest() : base(TestConstants.DefaultSqlServerProvider)
    {
    }

    protected override Type ExpectedLockHandler => typeof(SelectForUpdateLockHandler);
}
