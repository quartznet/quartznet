using System.Data.Common;

using FakeItEasy;

using Quartz.Impl.AdoJobStore;
using Quartz.Util;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

[TestFixture]
public class JobStoreCMTTest
{
    private TestJobStoreCMT jobStore;
    private IDbConnectionManager connectionManager;

    [SetUp]
    public void SetUp()
    {
        jobStore = new TestJobStoreCMT();
        connectionManager = A.Fake<IDbConnectionManager>();
        jobStore.ConnectionManager = connectionManager;
    }

    private class TestJobStoreCMT : JobStoreCMT
    {
        public void ExecuteGetNonManagedConnection()
        {
            GetNonManagedTXConnection().GetAwaiter().GetResult();
        }
    }

    [Test]
    public void ShouldNotAutomaticallyOpenConnection()
    {
        var mock = A.Fake<DbConnection>();
        A.CallTo(() => connectionManager.GetConnection(A<string>.Ignored)).Returns(mock);

        jobStore.ExecuteGetNonManagedConnection();

        A.CallTo(() => mock.OpenAsync(CancellationToken.None)).MustNotHaveHappened();
    }

    [Test]
    public void ShouldOpenConnectionIfRequested()
    {
        jobStore.OpenConnection = true;
        var mock = A.Fake<DbConnection>();
        A.CallTo(() => connectionManager.GetConnection(A<string>.Ignored)).Returns(mock);

        jobStore.ExecuteGetNonManagedConnection();

        A.CallTo(() => mock.OpenAsync(CancellationToken.None)).MustHaveHappened();
    }
}