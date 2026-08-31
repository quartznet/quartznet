using Npgsql;

using Quartz.Extensibility;
using Quartz.Listeners;
using Quartz.Tests.Integration.TestHelpers;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The persistent store's error notification, which the in-memory tests cannot cover: it is raised after
/// the transaction commits rather than from inside the branch that writes the state, so the only way to
/// know it survives the commit is to let a real transaction run (#3214).
/// </summary>
[Category("db-postgres")]
[NonParallelizable]
public sealed class TriggerInErrorNotificationAdoTest
{
    private const string SchedulerName = "TriggerInErrorAdoTest";

    [TearDown]
    public async Task CleanUpDatabaseState()
    {
        using NpgsqlConnection connection = new NpgsqlConnection(TestConstants.PostgresConnectionString);
        await connection.OpenAsync();
        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM qrtz_fired_triggers WHERE sched_name = @schedulerName;" +
            "DELETE FROM qrtz_simple_triggers WHERE sched_name = @schedulerName;" +
            "DELETE FROM qrtz_triggers WHERE sched_name = @schedulerName;" +
            "DELETE FROM qrtz_job_details WHERE sched_name = @schedulerName;" +
            "DELETE FROM qrtz_scheduler_state WHERE sched_name = @schedulerName;";
        command.Parameters.AddWithValue("schedulerName", SchedulerName);
        await command.ExecuteNonQueryAsync();
    }

    [Test]
    public async Task JobThatCannotBeInstantiated_NotifiesTriggersInError_AndLeavesErrorStateCommitted()
    {
        ErrorStateListener listener = new ErrorStateListener();

        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(q => q
                .ConfigureScheduler(options =>
                {
                    options.InstanceName = SchedulerName;
                    options.GenerateInstanceId = true;
                })
                .UseDefaultThreadPool()
                .UseJobFactory(new ThrowingJobFactory())
                .UsePersistentStore(store =>
                {
                    store.UsePostgres(TestConstants.PostgresConnectionString);
                    store.UseNewtonsoftJsonSerializer();
                    store.ConfigureStore(options => options.TablePrefix = SchedulerHelper.TablePrefix);
                }))
            .BuildScheduler();

        TriggerKey triggerKey = new TriggerKey("trigger1", "errorstate");

        try
        {
            scheduler.ListenerManager.AddSchedulerListener(listener);

            IJobDetail job = JobBuilder.Create<NeverRunsJob>()
                .WithIdentity("job1", "errorstate")
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .ForJob(job)
                .StartNow()
                .Build();

            await scheduler.ScheduleJob(job, trigger);
            await scheduler.Start();

            JobKey reported = await listener.JobTriggers.WaitAsync(TimeSpan.FromSeconds(60));
            reported.Should().Be(new JobKey("job1", "errorstate"));

            // Read back rather than trusting the notification: the point of raising it outside the
            // transaction is that what it announces has actually been committed.
            (await scheduler.GetTriggerState(triggerKey)).Should().Be(TriggerState.Error);
        }
        finally
        {
            await scheduler.Shutdown(true);
        }
    }

    private sealed class ThrowingJobFactory : IJobFactory
    {
        public ValueTask<JobScope> CreateJob(TriggerFiredBundle bundle, IScheduler scheduler, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Unable to resolve service for type 'ITaskTracker'");
        }

        public ValueTask ReturnJob(JobScope scope, CancellationToken cancellationToken = default) => default;
    }

    private sealed class ErrorStateListener : ISchedulerListener
    {
        private readonly TaskCompletionSource<JobKey> jobTriggers = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<JobKey> JobTriggers => jobTriggers.Task;

        public ValueTask TriggersInError(IScheduler scheduler, JobKey jobKey, CancellationToken cancellationToken = default)
        {
            jobTriggers.TrySetResult(jobKey);
            return default;
        }
    }

    public sealed class NeverRunsJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("must never be constructed");
        }
    }
}
