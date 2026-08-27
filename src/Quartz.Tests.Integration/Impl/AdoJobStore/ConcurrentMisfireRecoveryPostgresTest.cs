namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// Concurrent misfire recovery against PostgreSQL, whose nodes serialize on
/// <c>PostgreSqlSelectForUpdateLockHandler</c> — a <c>SELECT ... FOR UPDATE</c> against
/// <c>QRTZ_LOCKS</c>, held for the length of the sweep's own transaction.
/// </summary>
[Category("db-postgres")]
[NonParallelizable]
public sealed class ConcurrentMisfireRecoveryPostgresTest : ConcurrentMisfireRecoveryTestBase
{
    public ConcurrentMisfireRecoveryPostgresTest() : base(TestConstants.PostgresProvider)
    {
    }
}
