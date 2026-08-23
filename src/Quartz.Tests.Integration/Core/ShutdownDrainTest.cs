using System.Data.Common;
using System.Diagnostics;

using Quartz.Tests.Integration.TestHelpers;
using Quartz.Tests.Integration.Utils;

namespace Quartz.Tests.Integration.Core;

/// <summary>
/// What a shutdown that waited for its jobs guarantees about the database, and what a deadline costs.
/// </summary>
/// <remarks>
/// The job store update that ends a firing is the last act of the execution the thread pool was handed, so
/// a shutdown that drained the pool has waited for it. Against a real database that is the difference
/// between the row in <c>QRTZ_FIRED_TRIGGERS</c> being gone and the store being torn down mid-write, and it
/// is the reason the barrier is not built on the count of executing jobs — that count reads zero while the
/// update is still in flight.
/// </remarks>
[TestFixture(TestConstants.DefaultSqlServerProvider, Category = "db-sqlserver")]
[TestFixture(TestConstants.PostgresProvider, Category = "db-postgres")]
[NonParallelizable]
public class ShutdownDrainTest
{
    private readonly string provider;

    public ShutdownDrainTest(string provider)
    {
        this.provider = provider;
    }

    [SetUp]
    public void ResetJob()
    {
        DrainTestJob.Reset();
    }

    [Test]
    public async Task AShutdownThatWaitedForItsJobsWaitedForTheirStoreUpdatesToo()
    {
        DrainTestJob.RunTime = TimeSpan.FromSeconds(1);

        IScheduler scheduler = await CreateScheduler("ShutdownDrainWaits");
        await ScheduleAndStart(scheduler);

        await DrainTestJob.Started.WaitAsync(TimeSpan.FromSeconds(30));

        using CancellationTokenSource generous = new(TimeSpan.FromMinutes(1));
        await scheduler.Shutdown(waitForJobsToComplete: true, generous.Token);

        DrainTestJob.Finished.IsCompleted.Should().BeTrue("the shutdown was told to wait for the running job");

        (await CountRows(scheduler.SchedulerName, "QRTZ_FIRED_TRIGGERS")).Should().Be(0,
            "the drain covers the job's TriggeredJobComplete, which is what deletes the fired-trigger row - a barrier that "
            + "resumed when the job left the executing-jobs count would have shut the store down while that delete was in flight");
        (await CountRows(scheduler.SchedulerName, "QRTZ_TRIGGERS")).Should().Be(0,
            "the same update removes a one-shot trigger that has fired for the last time");
    }

    [Test]
    public async Task AnExpiredDeadlineEndsTheWaitAndLeavesTheJobRunning()
    {
        DrainTestJob.RunTime = TimeSpan.FromSeconds(10);

        IScheduler scheduler = await CreateScheduler("ShutdownDrainGivesUp");
        await ScheduleAndStart(scheduler);

        await DrainTestJob.Started.WaitAsync(TimeSpan.FromSeconds(30));

        using CancellationTokenSource deadline = new(TimeSpan.FromMilliseconds(500));
        long startTimestamp = Stopwatch.GetTimestamp();

        await scheduler.Shutdown(waitForJobsToComplete: true, deadline.Token);

        Stopwatch.GetElapsedTime(startTimestamp).Should().BeLessThan(TimeSpan.FromSeconds(5),
            "the deadline bounds the wait, so the shutdown cannot have waited out a job with nine seconds still to run");
        DrainTestJob.Finished.IsCompleted.Should().BeFalse("the job the shutdown stopped waiting for is still running");

        (await CountRows(scheduler.SchedulerName, "QRTZ_FIRED_TRIGGERS")).Should().Be(1,
            "this is what giving up costs: the job's row is still there, and its store update will find the store shut down");

        // Let the job finish before the fixture moves on, and take its rows with us: nothing completes them
        // now that the store is down.
        await DrainTestJob.Finished.WaitAsync(TimeSpan.FromSeconds(30));
        await DeleteRows(scheduler.SchedulerName);
    }

    private ValueTask<IScheduler> CreateScheduler(string name)
    {
        return SchedulerHelper.CreateScheduler(
            provider,
            options =>
            {
                options.InstanceName = SchedulerHelper.GetSchedulerName(provider, name);
                options.GenerateInstanceId = true;
            });
    }

    private static async Task ScheduleAndStart(IScheduler scheduler)
    {
        await scheduler.Clear();

        await scheduler.ScheduleJob(
            JobBuilder.Create<DrainTestJob>().WithIdentity("drain").Build(),
            TriggerBuilder.Create().WithIdentity("drain").StartNow().Build());

        await scheduler.Start();
    }

    private async Task<int> CountRows(string schedulerName, string table)
    {
        using DbConnection connection = DatabaseHelper.CreateConnection(provider);
        await connection.OpenAsync();

        using DbCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM {table} WHERE SCHED_NAME = @schedulerName";
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = "@schedulerName";
        parameter.Value = schedulerName;
        command.Parameters.Add(parameter);

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task DeleteRows(string schedulerName)
    {
        using DbConnection connection = DatabaseHelper.CreateConnection(provider);
        await connection.OpenAsync();

        foreach (string table in new[] { "QRTZ_FIRED_TRIGGERS", "QRTZ_TRIGGERS", "QRTZ_JOB_DETAILS" })
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = $"DELETE FROM {table} WHERE SCHED_NAME = @schedulerName";
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = "@schedulerName";
            parameter.Value = schedulerName;
            command.Parameters.Add(parameter);

            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// A job that runs for as long as the test says, and says when it started and stopped.
    /// </summary>
    [DisallowConcurrentExecution]
    private sealed class DrainTestJob : IJob
    {
        private static TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private static TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static TimeSpan RunTime { get; set; } = TimeSpan.FromSeconds(1);

        public static Task Started => started.Task;

        public static Task Finished => finished.Task;

        public static void Reset()
        {
            started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            started.TrySetResult();

            // Deliberately not the job's own token: the point of these tests is that a shutdown deadline
            // ends the waiting rather than the job, so the job must not notice it.
            await Task.Delay(RunTime, CancellationToken.None).ConfigureAwait(false);

            finished.TrySetResult();
        }
    }
}
