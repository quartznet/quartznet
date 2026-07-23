
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
    public void GetSelectNextMisfiredTriggersInStateToAcquireSql_ShouldContainForceIndexHint()
    {
        var del = new TestMySQLDelegate();

        var sql = del.GetSelectNextMisfiredTriggersInStateToAcquireSqlPublic(20);

        sql.Should().Contain("FORCE INDEX (IDX_{1}T_NFT_ST_MISFIRE)");
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

        sql.Should().Contain("FORCE INDEX (IDX_{1}T_NFT_ST_MISFIRE)");
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
    public void GetSelectNextMisfiredTriggersInStateToAcquireSql_WithSchemaQualifiedPrefix_ProducesValidIndexHint()
    {
        var del = new TestMySQLDelegate();

        var template = del.GetSelectNextMisfiredTriggersInStateToAcquireSqlPublic(20);
        var sql = AdoJobStoreUtil.ReplaceTablePrefix(template, "common.QRTZ_");

        sql.Should().Contain("FROM common.QRTZ_TRIGGERS FORCE INDEX (IDX_QRTZ_T_NFT_ST_MISFIRE)");
        sql.Should().NotContain("IDX_common.");
    }

    [Test]
    public void GetCountMisfiredTriggersInStateSql_WithSchemaQualifiedPrefix_ProducesValidIndexHint()
    {
        var del = new TestMySQLDelegate();

        var template = del.GetCountMisfiredTriggersInStateSqlPublic();
        var sql = AdoJobStoreUtil.ReplaceTablePrefix(template, "common.QRTZ_");

        sql.Should().Contain("FROM common.QRTZ_TRIGGERS FORCE INDEX (IDX_QRTZ_T_NFT_ST_MISFIRE)");
        sql.Should().NotContain("IDX_common.");
    }

    [Test]
    public void GetSelectMisfiredTriggersToRecoverSql_ShouldContainForceIndexHint()
    {
        var del = new TestMySQLDelegate();

        var sql = del.GetSelectMisfiredTriggersToRecoverSqlPublic(20);

        // This statement joins the type tables onto TRIGGERS, so the hint has to attach to the alias
        // rather than to a bare "TRIGGERS WHERE".
        sql.Should().Contain("{0}TRIGGERS t FORCE INDEX (IDX_{1}T_NFT_ST_MISFIRE)");
        sql.Should().Contain("LIMIT 20");
    }

    [Test]
    public void GetSelectMisfiredTriggersToRecoverSql_WithMinusOne_ShouldNotContainLimit()
    {
        var del = new TestMySQLDelegate();

        var sql = del.GetSelectMisfiredTriggersToRecoverSqlPublic(-1);

        sql.Should().NotContain("LIMIT");
        sql.Should().NotContain("FORCE INDEX");
    }

    [Test]
    public void GetSelectMisfiredTriggersToRecoverSql_WithSchemaQualifiedPrefix_ProducesValidIndexHint()
    {
        var del = new TestMySQLDelegate();

        var template = del.GetSelectMisfiredTriggersToRecoverSqlPublic(20);
        var sql = AdoJobStoreUtil.ReplaceTablePrefix(template, "common.QRTZ_");

        sql.Should().Contain("common.QRTZ_TRIGGERS t FORCE INDEX (IDX_QRTZ_T_NFT_ST_MISFIRE)");
        // The joined type tables must not have picked up the hint
        sql.Should().Contain("common.QRTZ_SIMPLE_TRIGGERS st ON");
        sql.Should().NotContain("IDX_common.");
    }

    private sealed class TestMySQLDelegate : MySQLDelegate
    {
        public string GetSelectNextTriggerToAcquireSqlPublic(int maxCount)
            => GetSelectNextTriggerToAcquireSql(maxCount);

        public string GetSelectNextMisfiredTriggersInStateToAcquireSqlPublic(int count)
            => GetSelectNextMisfiredTriggersInStateToAcquireSql(count);

        public string GetCountMisfiredTriggersInStateSqlPublic()
            => GetCountMisfiredTriggersInStateSql();

        public string GetSelectMisfiredTriggersToRecoverSqlPublic(int count)
            => GetSelectMisfiredTriggersToRecoverSql(count);
    }
}
