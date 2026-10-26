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

using System.Data.Common;
using System.Globalization;

using Microsoft.Data.SqlClient;

using Quartz.Tests.Integration.TestHelpers;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The manual-firing execution group on SQL Server, whose default collation is case-insensitive: two groups
/// that differ only in case are equal to it, and still two groups to the execution limits (#4174).
/// </summary>
/// <remarks>
/// The shipped delegate reads the job's group column one row a trigger and compares the values in code,
/// never with <c>DISTINCT</c> or <c>GROUP BY</c>, which would fold <c>Reports</c> and <c>reports</c> into one
/// group. A scheduler on the container's database, never started; <see cref="SchedulerName" /> is the
/// isolation within it.
/// </remarks>
[Category("db-sqlserver")]
[NonParallelizable]
public sealed class ManualFiringExecutionGroupSqlServerTest
{
    private const string SchedulerName = "manual-firing-execution-group";
    private const string JobGroup = "manual-firing";
    private const string Reports = "reports";

    private static readonly string[] tables =
    [
        "QRTZ_FIRED_TRIGGERS", "QRTZ_SIMPLE_TRIGGERS", "QRTZ_CRON_TRIGGERS", "QRTZ_SIMPROP_TRIGGERS",
        "QRTZ_BLOB_TRIGGERS", "QRTZ_TRIGGERS", "QRTZ_JOB_DETAILS", "QRTZ_CALENDARS",
        "QRTZ_PAUSED_TRIGGER_GRPS", "QRTZ_SCHEDULER_STATE",
    ];

    private IScheduler scheduler;

    [SetUp]
    public async Task BuildScheduler()
    {
        await DeleteThisSchedulersRows();
        scheduler = await SchedulerHelper.CreateScheduler(TestConstants.DefaultSqlServerProvider, options =>
        {
            options.InstanceName = SchedulerName;
            options.InstanceId = "node";
        });
    }

    [TearDown]
    public async Task ShutDownAndCleanUp()
    {
        await scheduler.Shutdown(waitForJobsToComplete: false);
        await DeleteThisSchedulersRows();
    }

    /// <summary>
    /// The premise that gives the case test its point: this database compares the two names as equal, so a
    /// <c>DISTINCT</c> would fold them into one group.
    /// </summary>
    [Test]
    public async Task TheDatabaseComparesGroupsThatDifferOnlyInCaseAsEqual()
    {
        await using SqlConnection connection = new(TestConstants.SqlServerConnectionString);
        await connection.OpenAsync();
        await using SqlCommand command = new("SELECT CASE WHEN N'Reports' = N'reports' THEN 1 ELSE 0 END", connection);

        Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture).Should().Be(1,
            "the container's database has SQL Server's default, case-insensitive collation");
    }

    [Test]
    public async Task AManualFiringTakesTheGroupOnlyWhenEveryTriggerOfTheJobCarriesItOrdinally()
    {
        JobKey agreeing = await StoreJob("agreeing", Reports, Reports);
        JobKey mixed = await StoreJob("mixed", Reports, "batch");
        JobKey cased = await StoreJob("cased", Reports, "Reports");

        foreach (JobKey job in new[] { agreeing, mixed, cased })
        {
            await scheduler.TriggerJob(job);
        }

        (await GroupOfTheManualFiring(agreeing)).Should().Be(Reports, "every trigger of the job carries reports");
        (await GroupOfTheManualFiring(mixed)).Should().BeNull("two groups is no one group");
        (await GroupOfTheManualFiring(cased)).Should().BeNull(
            "groups compare ordinally, as execution limits count them, even where the database's collation takes the two names as equal");
    }

    private async Task<JobKey> StoreJob(string name, params string[] executionGroups)
    {
        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity(name, JobGroup).StoreDurably().Build();
        DateTimeOffset tomorrow = TimeProvider.System.GetUtcNow().AddDays(1);

        List<ITrigger> triggers = [];
        for (int i = 0; i < executionGroups.Length; i++)
        {
            triggers.Add(TriggerBuilder.Create()
                .WithIdentity(name + "-" + i.ToString(CultureInfo.InvariantCulture), JobGroup)
                .ForJob(job)
                .WithExecutionGroup(executionGroups[i])
                .StartAt(tomorrow)
                .Build());
        }

        await scheduler.ScheduleJob(job, triggers, ScheduleJobOptions.Replacing);
        return job.Key;
    }

    private async Task<string> GroupOfTheManualFiring(JobKey job)
    {
        List<ITrigger> triggers = await scheduler.GetTriggersOfJob(job);
        return triggers.Should().ContainSingle(
                trigger => trigger.JobDataMap.ContainsKey(SchedulerConstants.ManualTrigger),
                "one manual firing of the job was stored")
            .Subject.ExecutionGroup;
    }

    private static async Task DeleteThisSchedulersRows()
    {
        await using SqlConnection connection = new(TestConstants.SqlServerConnectionString);
        await connection.OpenAsync();
        foreach (string table in tables)
        {
            await using DbCommand command = new SqlCommand("DELETE FROM " + table + " WHERE SCHED_NAME = @schedulerName", connection);
            command.Parameters.Add(new SqlParameter("@schedulerName", SchedulerName));
            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>Never executed: the scheduler is never started.</summary>
    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
