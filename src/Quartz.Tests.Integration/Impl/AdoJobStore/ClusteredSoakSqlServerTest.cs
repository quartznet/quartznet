namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The clustered soak against SQL Server, whose store locks through <c>SelectForUpdateLockHandler</c>
/// with the <c>(UPDLOCK,ROWLOCK)</c> hint.
/// </summary>
/// <remarks>
/// <para>
/// The second configuration rather than a duplicate of the first: the interesting code under a soak is
/// the SQL, and the row locking, the recovery statements and the misfire scan differ per engine. A run
/// on one engine says nothing about the other.
/// </para>
/// <para>
/// <c>LongRunning</c> keeps it out of every integration leg — see <c>build/Build.cs</c>'s
/// <c>GetTestFilter</c>, and <see cref="ClusteredSoakTestBase" /> for how to run it.
/// </para>
/// </remarks>
[Category("db-sqlserver")]
[Category("LongRunning")]
[NonParallelizable]
public sealed class ClusteredSoakSqlServerTest : ClusteredSoakTestBase
{
    public ClusteredSoakSqlServerTest() : base(TestConstants.DefaultSqlServerProvider)
    {
    }
}
