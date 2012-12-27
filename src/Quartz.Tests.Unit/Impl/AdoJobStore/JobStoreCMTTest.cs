using System.Data;

using NUnit.Framework;

using Quartz.Impl.AdoJobStore;
using Quartz.Util;

using Rhino.Mocks;

namespace Quartz.Tests.Unit.Impl.AdoJobStore
{
    [TestFixture]
    public class JobStoreCMTTest
    {
        private TestJobStoreCMT jobStore;
        private IDbConnectionManager connectionManager;

        [SetUp]
        public void SetUp()
        {
            jobStore = new TestJobStoreCMT();
            connectionManager = MockRepository.GenerateMock<IDbConnectionManager>();
            jobStore.ConnectionManager = connectionManager;
        }

        private class TestJobStoreCMT : JobStoreCMT
        {
            public void ExecuteGetNonManagedConnection()
            {
                GetNonManagedTXConnection();
            }
        }

        [Test]
        public void ShouldNotAutomaticallyOpenConnection()
        {
            var mock = MockRepository.GenerateMock<IDbConnection>();
            connectionManager.Stub(x => x.GetConnection(Arg<string>.Is.Anything)).Return(mock);

            jobStore.ExecuteGetNonManagedConnection();

            mock.AssertWasNotCalled(x => x.Open());
        }

        [Test]
        public void ShouldOpenConnectionIfRequested()
        {
            jobStore.OpenConnection = true;
            var mock = MockRepository.GenerateMock<IDbConnection>();
            connectionManager.Stub(x => x.GetConnection(Arg<string>.Is.Anything)).Return(mock);

            jobStore.ExecuteGetNonManagedConnection();

            mock.AssertWasCalled(x => x.Open());
        }
    }
}