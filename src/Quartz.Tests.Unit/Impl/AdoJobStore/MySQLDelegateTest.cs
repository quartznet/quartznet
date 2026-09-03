using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

public class MySQLDelegateTest
{
    [Test]
    public void GetSelectNextTriggerToAcquireSql_ShouldContainForceIndexHint()
    {
        var del = new TestMySQLDelegate();

        var sql = del.GetSelectNextTriggerToAcquireSqlPublic(10);

        sql.Should().Contain("FORCE INDEX (IDX_{1}T_NFT_ST)");
        sql.Should().Contain("LIMIT 10");
    }

    [Test]
    public void GetSelectNextTriggerToAcquireWithExecutionGroupSql_ShouldContainForceIndexHint()
    {
        var del = new TestMySQLDelegate();

        var sql = del.GetSelectNextTriggerToAcquireWithExecutionGroupSqlPublic(10);

        sql.Should().Contain("FORCE INDEX (IDX_{1}T_NFT_ST)");
        sql.Should().Contain("LIMIT 10");
    }

    [Test]
    public void GetSelectNextMisfiredTriggersInStateToAcquireSql_ShouldContainForceIndexHint()
    {
        var del = new TestMySQLDelegate();

        var sql = del.GetSelectNextMisfiredTriggersInStateToAcquireSqlPublic(20);

        sql.Should().Contain("FORCE INDEX (IDX_{1}T_NFT_ST)",
            "the sweep filters SCHED_NAME and TRIGGER_STATE by equality and ranges on NEXT_FIRE_TIME, which is "
            + "the acquisition index's shape; the misfire index's second column is compared with <> and stops the seek (#3608)");
        sql.Should().NotContain("IDX_{1}T_NFT_ST_MISFIRE");
        sql.Should().Contain("LIMIT 20");
    }

    [Test]
    public void GetSelectNextMisfiredTriggersInStateToAcquireSql_WithMinusOne_ShouldNotContainLimit()
    {
        var del = new TestMySQLDelegate();

        var sql = del.GetSelectNextMisfiredTriggersInStateToAcquireSqlPublic(-1);

        sql.Should().NotContain("LIMIT");
        sql.Should().Contain("FORCE INDEX (IDX_{1}T_NFT_ST)",
            "the index a statement should read is not a function of how many rows it returns, and the unlimited sweep reads the most");
    }

    [Test]
    public void GetCountMisfiredTriggersInStateSql_ShouldContainForceIndexHint()
    {
        var del = new TestMySQLDelegate();

        var sql = del.GetCountMisfiredTriggersInStateSqlPublic();

        sql.Should().Contain("FORCE INDEX (IDX_{1}T_NFT_ST)",
            "the count is the sweep's predicate without the ORDER BY, and it is the peek every misfire-handler pass starts with (#3608)");
        sql.Should().NotContain("IDX_{1}T_NFT_ST_MISFIRE");
    }

    [Test]
    public void GetSelectNextTriggerToAcquireSql_WithSchemaQualifiedPrefix_ProducesValidIndexHint()
    {
        var del = new TestMySQLDelegate();

        var template = del.GetSelectNextTriggerToAcquireSqlPublic(10);
        var sql = AdoJobStoreUtil.ReplaceTablePrefix(template, "common.QRTZ_");

        sql.Should().Contain("common.QRTZ_TRIGGERS t FORCE INDEX (IDX_QRTZ_T_NFT_ST)");
        sql.Should().Contain("common.QRTZ_JOB_DETAILS jd");
        sql.Should().NotContain("IDX_common.");
    }

    [Test]
    public void GetSelectNextTriggerToAcquireWithExecutionGroupSql_WithSchemaQualifiedPrefix_ProducesValidIndexHint()
    {
        var del = new TestMySQLDelegate();

        var template = del.GetSelectNextTriggerToAcquireWithExecutionGroupSqlPublic(10);
        var sql = AdoJobStoreUtil.ReplaceTablePrefix(template, "common.QRTZ_");

        sql.Should().Contain("common.QRTZ_TRIGGERS t FORCE INDEX (IDX_QRTZ_T_NFT_ST)");
        sql.Should().Contain("common.QRTZ_JOB_DETAILS jd");
        sql.Should().NotContain("IDX_common.");
    }

    [Test]
    public void GetSelectNextMisfiredTriggersInStateToAcquireSql_WithSchemaQualifiedPrefix_ProducesValidIndexHint()
    {
        var del = new TestMySQLDelegate();

        var template = del.GetSelectNextMisfiredTriggersInStateToAcquireSqlPublic(20);
        var sql = AdoJobStoreUtil.ReplaceTablePrefix(template, "common.QRTZ_");

        sql.Should().Contain("FROM common.QRTZ_TRIGGERS FORCE INDEX (IDX_QRTZ_T_NFT_ST)");
        sql.Should().NotContain("IDX_common.");
    }

    [Test]
    public void GetCountMisfiredTriggersInStateSql_WithSchemaQualifiedPrefix_ProducesValidIndexHint()
    {
        var del = new TestMySQLDelegate();

        var template = del.GetCountMisfiredTriggersInStateSqlPublic();
        var sql = AdoJobStoreUtil.ReplaceTablePrefix(template, "common.QRTZ_");

        sql.Should().Contain("FROM common.QRTZ_TRIGGERS FORCE INDEX (IDX_QRTZ_T_NFT_ST)");
        sql.Should().NotContain("IDX_common.");
    }

    private class TestMySQLDelegate : MySQLDelegate
    {
        public string GetSelectNextTriggerToAcquireSqlPublic(int maxCount)
            => GetSelectNextTriggerToAcquireSql(maxCount);

        public string GetSelectNextTriggerToAcquireWithExecutionGroupSqlPublic(int maxCount)
            => GetSelectNextTriggerToAcquireWithExecutionGroupSql(maxCount);

        public string GetSelectNextMisfiredTriggersInStateToAcquireSqlPublic(int count)
            => GetSelectNextMisfiredTriggersInStateToAcquireSql(count);

        public string GetCountMisfiredTriggersInStateSqlPublic()
            => GetCountMisfiredTriggersInStateSql();
    }
}
