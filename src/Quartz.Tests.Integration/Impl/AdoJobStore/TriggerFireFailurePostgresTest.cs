namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The failed fire against PostgreSQL, the engine on which a failed statement aborts the transaction:
/// every fire after it failed too, and the commit rolled back the fires reported before it.
/// </summary>
[Category("db-postgres")]
[NonParallelizable]
public sealed class TriggerFireFailurePostgresTest : TriggerFireFailureTestBase
{
    public TriggerFireFailurePostgresTest() : base(TestConstants.PostgresProvider, typeof(FaultingPostgreSQLDelegate))
    {
    }
}
