namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The one-at-a-time acquisition behind a full execution group, against SQL Server.
/// </summary>
[Category("db-sqlserver")]
[NonParallelizable]
public sealed class AcquisitionBehindExecutionLimitSqlServerTest : AcquisitionBehindExecutionLimitTestBase
{
    public AcquisitionBehindExecutionLimitSqlServerTest() : base(TestConstants.DefaultSqlServerProvider)
    {
    }
}
