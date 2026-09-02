namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The clustered soak against PostgreSQL, whose store locks through
/// <c>PostgreSqlSelectForUpdateLockHandler</c>.
/// </summary>
/// <remarks>
/// <c>LongRunning</c> keeps it out of every integration leg — see <c>build/Build.cs</c>'s
/// <c>GetTestFilter</c>, and <see cref="ClusteredSoakTestBase" /> for how to run it and why it is a
/// release gate rather than a leg.
/// </remarks>
[Category("db-postgres")]
[Category("LongRunning")]
[NonParallelizable]
public sealed class ClusteredSoakPostgresTest : ClusteredSoakTestBase
{
    public ClusteredSoakPostgresTest() : base(TestConstants.PostgresProvider)
    {
    }
}
