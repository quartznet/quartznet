#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

using System;
using System.Data.Common;
using System.Globalization;
using System.Threading.Tasks;

using Microsoft.Data.SqlClient;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The manual-firing execution group on SQL Server, the database #4167 was reported against, whose
/// default collation is case-insensitive: two groups that differ only in case are equal to it, and still
/// two groups to the execution limits.
/// </summary>
[NonParallelizable]
[Category("db-sqlserver")]
public sealed class ManualFiringExecutionGroupSqlServerTest : ManualFiringExecutionGroupTestBase
{
    private static readonly string[] Tables =
    {
        "QRTZ_FIRED_TRIGGERS", "QRTZ_SIMPLE_TRIGGERS", "QRTZ_CRON_TRIGGERS", "QRTZ_SIMPROP_TRIGGERS",
        "QRTZ_BLOB_TRIGGERS", "QRTZ_TRIGGERS", "QRTZ_JOB_DETAILS", "QRTZ_CALENDARS",
        "QRTZ_PAUSED_TRIGGER_GRPS", "QRTZ_SCHEDULER_STATE",
    };

    protected override string Provider => TestConstants.DefaultSqlServerProvider;

    protected override string ConnectionString => TestConstants.SqlServerConnectionString;

    protected override Type DriverDelegate => typeof(SqlServerDelegate);

    protected override DbConnection CreateConnection() => new SqlConnection(ConnectionString);

    protected override Task PrepareDatabase() => DeleteThisSchedulersRows();

    protected override Task CleanUpDatabase() => DeleteThisSchedulersRows();

    /// <summary>
    /// The premise that gives the base fixture's case test its point here: this database compares the two
    /// names as equal, so a <c>DISTINCT</c> or a <c>GROUP BY</c> would fold them into one group.
    /// </summary>
    [Test]
    public async Task TheDatabaseComparesGroupsThatDifferOnlyInCaseAsEqual()
    {
        using DbConnection connection = CreateConnection();
        await connection.OpenAsync();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN N'Reports' = N'reports' THEN 1 ELSE 0 END";

        Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture).Should().Be(1,
            "the container's database has SQL Server's default, case-insensitive collation");
    }

    private async Task DeleteThisSchedulersRows()
    {
        using DbConnection connection = CreateConnection();
        await connection.OpenAsync();
        foreach (string table in Tables)
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM " + table + " WHERE SCHED_NAME = @schedulerName";
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = "schedulerName";
            parameter.Value = SchedulerName;
            command.Parameters.Add(parameter);
            await command.ExecuteNonQueryAsync();
        }
    }
}
