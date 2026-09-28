namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The one-at-a-time acquisition behind a full execution group, against PostgreSQL.
/// </summary>
[Category("db-postgres")]
[NonParallelizable]
public sealed class AcquisitionBehindExecutionLimitPostgresTest : AcquisitionBehindExecutionLimitTestBase
{
    public AcquisitionBehindExecutionLimitPostgresTest() : base(TestConstants.PostgresProvider)
    {
    }
}
