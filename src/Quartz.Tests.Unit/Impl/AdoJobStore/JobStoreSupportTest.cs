using System;
using System.Collections.Generic;
using System.Reflection;

using NUnit.Framework;

using Quartz.Impl.AdoJobStore;

using Rhino.Mocks;

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
            driverDelegate = MockRepository.GenerateMock<IDriverDelegate>();
            jobStoreSupport.DirectDelegate = driverDelegate;
        }

        [Test]
        public void TestRecoverMisfiredJobs_ShouldCheckForMisfiredTriggersInStateWaiting()
        {
            jobStoreSupport.RecoverMisfiredJobs(null, false);

            driverDelegate.AssertWasCalled(x => x.HasMisfiredTriggersInState(
                Arg<ConnectionAndTransactionHolder>.Is.Anything,
                Arg<string>.Is.Equal(AdoConstants.StateWaiting),
                Arg<DateTimeOffset>.Is.Anything,
                Arg<int>.Is.Anything,
                Arg<IList<TriggerKey>>.Is.Anything));
        }

        public class TestJobStoreSupport : JobStoreSupport
        {
            protected override ConnectionAndTransactionHolder GetNonManagedTXConnection()
            {
                return new ConnectionAndTransactionHolder(null, null);
            }

            protected override T ExecuteInLock<T>(string lockName, Func<ConnectionAndTransactionHolder, T> txCallback)
            {
                return default(T);
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