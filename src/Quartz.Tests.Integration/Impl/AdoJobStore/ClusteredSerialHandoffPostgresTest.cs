namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The serial job's hand-off between two nodes against PostgreSQL.
/// </summary>
[Category("db-postgres")]
[NonParallelizable]
public sealed class ClusteredSerialHandoffPostgresTest : ClusteredSerialHandoffTestBase
{
    public ClusteredSerialHandoffPostgresTest() : base(TestConstants.PostgresProvider)
    {
    }
}
