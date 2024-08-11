using System.Data.Common;
using System.Reflection;

using FakeItEasy;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

public class JobStoreSupportTest
{
    private TestJobStoreSupport jobStoreSupport;
    private IDriverDelegate driverDelegate;

    [SetUp]
    public void SetUp()
    {
        jobStoreSupport = new TestJobStoreSupport();
        driverDelegate = A.Fake<IDriverDelegate>();
        jobStoreSupport.DirectDelegate = driverDelegate;
    }

    [Test]
    public async Task TestRecoverMisfiredJobs_ShouldCheckForMisfiredTriggersInStateWaiting()
    {
        await jobStoreSupport.RecoverMisfiredJobs(null, false);

        A.CallTo(() => driverDelegate.HasMisfiredTriggersInState(
            A<ConnectionAndTransactionHolder>.Ignored,
            A<string>.That.IsEqualTo(AdoConstants.StateWaiting),
            A<DateTimeOffset>.Ignored,
            A<int>.Ignored,
            A<IList<TriggerKey>>.Ignored,
            CancellationToken.None)).MustHaveHappened();
    }

    public class TestJobStoreSupport : JobStoreSupport
    {
        protected override ValueTask<ConnectionAndTransactionHolder> GetNonManagedTXConnection()
        {
            return new ValueTask<ConnectionAndTransactionHolder>(new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null));
        }

        protected override ValueTask<T> ExecuteInLock<T>(
            string lockName,
            Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<T>(default(T));
        }

        /// <summary>
        /// sets delegate directly
        /// </summary>
        internal IDriverDelegate DirectDelegate
        {
            set
            {
                FieldInfo fieldInfo = typeof(JobStoreSupport).GetField("driverDelegate", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(fieldInfo, Is.Not.Null);
                fieldInfo.SetValue(this, value);
            }
        }
    }
}