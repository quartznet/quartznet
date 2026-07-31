using System.Data.Common;

using FakeItEasy;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

public class ExternalTransactionJobStoreTest
{
    private TestExternalTransactionJobStore jobStore;
    private IDbProvider dbProvider;

    [SetUp]
    public void SetUp()
    {
        // The store opens connections through the provider it was constructed with, rather than looking
        // one up by name in the process-wide connection manager — two schedulers whose data sources
        // share a name would otherwise reach each other's database.
        dbProvider = A.Fake<IDbProvider>();
        jobStore = new TestExternalTransactionJobStore(dbProvider);
    }

    private sealed class TestExternalTransactionJobStore : ExternalTransactionJobStore
    {
        public TestExternalTransactionJobStore(IDbProvider dbProvider)
            : base(TestJobStores.Signaler(), TestJobStores.TypeLoader(), TimeProvider.System, TestJobStores.SchedulerOptions(), TestJobStores.StoreOptions(), TestJobStores.Serializer(), TestJobStores.ConnectionManager(), dbProvider, TestJobStores.DriverDelegate(), TestJobStores.LockHandler())
        {
        }

        public void ExecuteGetLocalTransactionConnection()
        {
            GetLocalTransactionConnection().GetAwaiter().GetResult();
        }
    }

    [Test]
    public void ShouldNotAutomaticallyOpenConnection()
    {
        var mock = A.Fake<DbConnection>();
        A.CallTo(() => dbProvider.CreateConnection()).Returns(mock);

        jobStore.ExecuteGetLocalTransactionConnection();

        A.CallTo(() => mock.OpenAsync(CancellationToken.None)).MustNotHaveHappened();
    }

    [Test]
    public void ShouldOpenConnectionIfRequested()
    {
        jobStore.OpenConnection = true;
        var mock = A.Fake<DbConnection>();
        A.CallTo(() => dbProvider.CreateConnection()).Returns(mock);

        jobStore.ExecuteGetLocalTransactionConnection();

        A.CallTo(() => mock.OpenAsync(CancellationToken.None)).MustHaveHappened();
    }
}
