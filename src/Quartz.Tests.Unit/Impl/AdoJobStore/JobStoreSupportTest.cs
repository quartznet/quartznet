using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

using FakeItEasy;

using NUnit.Framework;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore
{
    [TestFixture]
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
            await jobStoreSupport.RecoverMisfiredJobsAsync(null, false);

            A.CallTo(() => driverDelegate.HasMisfiredTriggersInStateAsync(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<string>.That.IsEqualTo(AdoConstants.StateWaiting),
                A<DateTimeOffset>.Ignored,
                A<int>.Ignored,
                A<IList<TriggerKey>>.Ignored)).MustHaveHappened();
        }

        public class TestJobStoreSupport : JobStoreSupport
        {
            protected override ConnectionAndTransactionHolder GetNonManagedTXConnection()
            {
                return new ConnectionAndTransactionHolder(null, null);
            }

            protected override Task<T> ExecuteInLockAsync<T>(string lockName, Func<ConnectionAndTransactionHolder, Task<T>> txCallback)
            {
                return Task.FromResult(default(T));
            }

            /// <summary>
            /// sets delegate directly
            /// </summary>
            internal IDriverDelegate DirectDelegate
            {
                set
                {
                    FieldInfo fieldInfo = typeof (JobStoreSupport).GetField("driverDelegate", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.IsNotNull(fieldInfo);
                    fieldInfo.SetValue(this, value);
                }
            }
        }
    }
}