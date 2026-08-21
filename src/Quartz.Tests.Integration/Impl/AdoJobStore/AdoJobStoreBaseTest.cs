using Quartz.Impl.AdoJobStore.Common;
using Quartz.Util;
using Quartz.Extensibility;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Npgsql;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

public class AdoJobStoreBaseTest
{

    [Test]
    public void CanDetectTransientException()
    {
        var jobStoreSupport = new TestAdoJobStoreBase(TestJobStores.Signaler(), TestJobStores.TypeLoader(), TimeProvider.System, TestJobStores.SchedulerOptions(), TestJobStores.StoreOptions(), TestJobStores.ClusteringOptions(), TestJobStores.Serializer(), TestJobStores.ConnectionManager(), TestJobStores.DbProvider(), TestJobStores.DriverDelegate(), TestJobStores.LockHandler());
        var npgsqlException = new NpgsqlException("timeout", new TimeoutException());
        Assert.That(jobStoreSupport.IsTransientPublic(npgsqlException), Is.True);

        var sqlException = new SqlExceptionSimulator();
        Assert.That(jobStoreSupport.IsTransientPublic(sqlException), Is.True);
    }

    private sealed class SqlExceptionSimulator : Exception
    {
        public IEnumerable<SqlErrorSimulator> Errors => new List<SqlErrorSimulator>
        {
            new SqlErrorSimulator()
        };

        public class SqlErrorSimulator
        {
            public int Number => 49920;
        }
    }

    private sealed class TestAdoJobStoreBase : AdoJobStoreBase
    {
        public TestAdoJobStoreBase(
            ISchedulerSignaler schedulerSignaler,
            ITypeLoader typeLoader,
            TimeProvider timeProvider,
            IOptions<QuartzSchedulerOptions> schedulerOptions,
        IOptions<AdoJobStoreOptions> storeOptions,
        IOptions<ClusteringOptions> clusteringOptions,
        IObjectSerializer objectSerializer,
        IDbConnectionManager connectionManager,
        IDbProvider dbProvider,
        IDriverDelegate driverDelegate,
        ISemaphore lockHandler)
            : base(schedulerSignaler, typeLoader, timeProvider, schedulerOptions, storeOptions, clusteringOptions, objectSerializer, connectionManager, dbProvider, driverDelegate, lockHandler)
        {
        }

        protected override ValueTask<ConnectionAndTransactionHolder> GetLocalTransactionConnection(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        protected override ValueTask<T> ExecuteInLock<T>(SchedulerLock? lockKind, Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public bool IsTransientPublic(Exception ex) => IsTransient(ex);
    }
}