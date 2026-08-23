namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// <see cref="ClusteredJobStoreTestBase"/> bound to the assembly-wide PostgreSQL database, for
/// fixtures whose scenario is PostgreSQL-specific or which have no reason to pay for a second engine.
/// A scenario worth running against more than one database instead derives from
/// <see cref="ClusteredJobStoreTestBase"/> once and gets one sealed fixture per engine — see
/// <see cref="ClusteredHardeningTestBase"/>.
/// </summary>
[Category("db-postgres")]
[NonParallelizable]
public abstract class ClusteredPostgresTestBase : ClusteredJobStoreTestBase
{
    protected ClusteredPostgresTestBase() : base(TestConstants.PostgresProvider)
    {
    }
}
