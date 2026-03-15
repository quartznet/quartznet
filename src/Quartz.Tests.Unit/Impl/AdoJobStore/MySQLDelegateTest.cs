using FluentAssertions;

using NUnit.Framework;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

public class MySQLDelegateTest
{
    [Test]
    public void GetSelectNextTriggerToAcquireSql_ShouldContainForceIndexHint()
    {
        var del = new TestMySQLDelegate();

        var sql = del.GetSelectNextTriggerToAcquireSqlPublic(10);

        sql.Should().Contain("FORCE INDEX (IDX_{0}T_NFT_ST)");
        sql.Should().Contain("LIMIT 10");
    }

    [Test]
    public void GetSelectNextMisfiredTriggersInStateToAcquireSql_ShouldContainForceIndexHint()
    {
        var del = new TestMySQLDelegate();

        var sql = del.GetSelectNextMisfiredTriggersInStateToAcquireSqlPublic(20);

        sql.Should().Contain("FORCE INDEX (IDX_{0}T_NFT_ST_MISFIRE)");
        sql.Should().Contain("LIMIT 20");
    }

    [Test]
    public void GetSelectNextMisfiredTriggersInStateToAcquireSql_WithMinusOne_ShouldNotContainLimit()
    {
        var del = new TestMySQLDelegate();

        var sql = del.GetSelectNextMisfiredTriggersInStateToAcquireSqlPublic(-1);

        sql.Should().NotContain("LIMIT");
        sql.Should().NotContain("FORCE INDEX");
    }

    [Test]
    public void GetCountMisfiredTriggersInStateSql_ShouldContainForceIndexHint()
    {
        var del = new TestMySQLDelegate();

        var sql = del.GetCountMisfiredTriggersInStateSqlPublic();

        sql.Should().Contain("FORCE INDEX (IDX_{0}T_NFT_ST_MISFIRE)");
    }

    private class TestMySQLDelegate : MySQLDelegate
    {
        public string GetSelectNextTriggerToAcquireSqlPublic(int maxCount)
            => GetSelectNextTriggerToAcquireSql(maxCount);

        public string GetSelectNextMisfiredTriggersInStateToAcquireSqlPublic(int count)
            => GetSelectNextMisfiredTriggersInStateToAcquireSql(count);

        public string GetCountMisfiredTriggersInStateSqlPublic()
            => GetCountMisfiredTriggersInStateSql();
    }
}
