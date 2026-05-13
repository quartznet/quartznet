using System.Globalization;

using FluentAssertions;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

public class MySQLDelegateTest
{
    [Test]
    public void GetSelectNextTriggerToAcquireSql_WhenUsingSchemaQualifiedTablePrefix_ShouldNotProduceMalformedSql()
    {
        const string tablePrefix = "common.QRTZ_";

        TestableMySqlDelegate driverDelegate = new();

        string sqlTemplate = driverDelegate.GetSelectNextTriggerToAcquireSqlTemplate(maxCount: 1);
        string sql = string.Format(CultureInfo.InvariantCulture, sqlTemplate, tablePrefix);

        sql.Should().Contain("common.QRTZ_TRIGGERS t");
        sql.Should().Contain("JOIN");
        sql.Should().Contain("common.QRTZ_JOB_DETAILS jd");
        sql.Should().NotContain("IDX_common.");
    }

    private sealed class TestableMySqlDelegate : MySQLDelegate
    {
        public string GetSelectNextTriggerToAcquireSqlTemplate(int maxCount)
        {
            return GetSelectNextTriggerToAcquireSql(maxCount);
        }
    }
}
