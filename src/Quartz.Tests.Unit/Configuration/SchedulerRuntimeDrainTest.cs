#nullable enable

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.Extensibility;
using Quartz.Tests.Unit.Plugin.History;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// Why a restart drains before it starts the next generation, over a store where the answer is
/// observable.
/// </summary>
/// <remarks>
/// <para>
/// A persistent scheduler's first act on <c>Start</c> is <c>RecoverJobs</c>: every acquired and blocked
/// trigger back to waiting, every misfire handled, and — the one that matters here —
/// <c>DeleteFiredTriggers</c> with an empty query, which deletes <em>every</em> fired-trigger row under
/// the scheduler's name. It is crash recovery, so it is unfiltered by instance id on purpose. Start a
/// second generation beside a job the first one is still running and that sweep deletes the running
/// job's row and unblocks its <c>[DisallowConcurrentExecution]</c> siblings while it runs.
/// </para>
/// <para>
/// A SQLite file, because none of that is visible over an in-memory store: <c>RAMJobStore</c> goes away
/// with its generation, so there is nothing for a second one to sweep. The count the sweep reports is
/// the assertion — zero when the restart waited, one when it did not.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class SchedulerRuntimeDrainTest
{
    /// <summary>
    /// The four counters <c>RecoverJobs</c> reports, which together say what the sweep found to recover.
    /// </summary>
    /// <remarks>
    /// All four, because the sweep reaches a running job's bookkeeping by more than one road: it deletes
    /// every fired-trigger row, and it also deletes triggers left in <c>COMPLETE</c> — which a one-shot
    /// trigger is from the moment it fires until the job that is running it finishes — and deleting a
    /// trigger takes its fired-trigger row with it. Watching only the last counter would have shown zero
    /// while the row was being deleted one step earlier.
    /// </remarks>
    private static readonly int[] recoveryCounters = [3018, 3019, 3021, 3022];

    private string databaseFile = null!;
    private string connectionString = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-runtime-drain-{Guid.NewGuid():N}.db");
        connectionString = $"Data Source={databaseFile}";
        GatedJob.Reset();
    }

    [TearDown]
    public void DeleteDatabase()
    {
        GatedJob.Release();
        SqliteConnection.ClearAllPools();

        if (File.Exists(databaseFile))
        {
            try
            {
                File.Delete(databaseFile);
            }
            catch (IOException)
            {
                // A connection pool that has not finished closing. The file is in the temp directory and
                // the test has already said what it had to say.
            }
        }
    }

    /// <summary>
    /// The next generation is created and started only once the previous one's jobs have finished, so
    /// its recovery sweep finds nothing to recover.
    /// </summary>
    /// <remarks>
    /// Written failing-first against a restart that shut the old scheduler down without waiting — the
    /// obvious implementation, and the one the design spike said would corrupt in-flight work. That
    /// version reports <c>Removed 1 stale fired job entries.</c>: the row it deleted belonged to the job
    /// that was still running.
    /// </remarks>
    [Test]
    public async Task RestartWaitsForRunningJobsBeforeStartingTheNextGeneration()
    {
        RecordingLoggerProvider log = new();
        await using ServiceProvider provider = Container(log);
        ISchedulerRuntime runtime = provider.GetRequiredService<ISchedulerRuntime>();

        IScheduler first = await runtime.Add("acme", Recipe);
        await Schedule(first);
        await GatedJob.Started.WaitAsync(TimeSpan.FromSeconds(30));

        int mark = log.Entries.Count;

        Task<IScheduler> restart = runtime
            .Restart("acme", new SchedulerRestartOptions { DrainTimeout = TimeSpan.FromSeconds(30) })
            .AsTask();

        await Task.Delay(500);

        restart.IsCompleted.Should().BeFalse(
            "the job is still running, and the restart's whole job is to wait for it before anything of the "
            + "next generation's touches the store");

        GatedJob.Release();

        IScheduler second = await restart.WaitAsync(TimeSpan.FromSeconds(60));

        second.Should().NotBeSameAs(first);
        GatedJob.Finished.IsCompletedSuccessfully.Should().BeTrue(
            "the restart returned, which it may only do once the work it drained has finished");

        Recovered(log, mark).Should().Equal(
            [
                "Freed 0 triggers from 'acquired' / 'blocked' state.",
                "Recovering 0 jobs that were in-progress at the time of the last shut-down.",
                "Removed 0 'complete' triggers.",
                "Removed 0 stale fired job entries."
            ],
            "the drain finished before the new generation started, so the job had already completed its own "
            + "trigger and deleted its own fired-trigger row - anything the sweep found to recover here would "
            + "be a running job's bookkeeping");
    }

    /// <summary>
    /// A drain that expires fails the restart rather than proceeding, and the next attempt succeeds once
    /// the work has finished.
    /// </summary>
    [Test]
    public async Task RestartFailsWhenTheDrainGivesUpAndSucceedsWhenAskedAgain()
    {
        RecordingLoggerProvider log = new();
        await using ServiceProvider provider = Container(log);
        ISchedulerRuntime runtime = provider.GetRequiredService<ISchedulerRuntime>();

        IScheduler first = await runtime.Add("acme", Recipe);
        await Schedule(first);
        await GatedJob.Started.WaitAsync(TimeSpan.FromSeconds(30));

        int mark = log.Entries.Count;

        Func<Task> restart = async () => await runtime.Restart(
            "acme",
            new SchedulerRestartOptions { DrainTimeout = TimeSpan.FromMilliseconds(200) });

        SchedulerRestartException failure = (await restart.Should().ThrowAsync<SchedulerRestartException>(
                "the job outlived the drain, and building the next generation now would run its recovery sweep "
                + "over that job's own bookkeeping"))
            .Which;

        failure.SchedulerName.Should().Be("acme");
        failure.JobsStillExecuting.Should().Be(1);
        failure.DrainTimeout.Should().Be(TimeSpan.FromMilliseconds(200));

        Recovered(log, mark).Should().BeEmpty(
            "the next generation was never started, so nothing swept the store - which is the whole point of "
            + "failing rather than proceeding");

        first.Status.Should().Be(SchedulerStatus.Shutdown, "the old scheduler is down; only the new one was not built");
        provider.GetRequiredService<ISchedulerRepository>().Lookup("acme").Should().BeNull();

        List<SchedulerRegistration> listing = await provider.GetRequiredService<ISchedulerRegistry>().QuerySchedulers();
        listing.Should().ContainSingle(x => x.Name == "acme",
            "an abandoned restart leaves the name in the listing with nothing running under it, which is how an "
            + "operator sees that it is waiting to be asked again")
            .Which.Status.Should().BeNull();

        GatedJob.Release();
        await GatedJob.Finished.WaitAsync(TimeSpan.FromSeconds(30));

        IScheduler second = await runtime.Restart(
            "acme",
            new SchedulerRestartOptions { DrainTimeout = TimeSpan.FromSeconds(30) });

        second.SchedulerName.Should().Be("acme");
        second.Status.Should().Be(SchedulerStatus.Running,
            "the old one was running when the first attempt shut it down, and asking again is the whole of the "
            + "recovery");
        provider.GetRequiredService<ISchedulerRepository>().Lookup("acme").Should().BeSameAs(second);
    }

    /// <summary>
    /// Asking again while the abandoned generation's jobs are still running is refused, not queued
    /// behind them.
    /// </summary>
    [Test]
    public async Task AskingAgainWhileTheAbandonedGenerationIsStillWorkingIsRefused()
    {
        RecordingLoggerProvider log = new();
        await using ServiceProvider provider = Container(log);
        ISchedulerRuntime runtime = provider.GetRequiredService<ISchedulerRuntime>();

        IScheduler first = await runtime.Add("acme", Recipe);
        await Schedule(first);
        await GatedJob.Started.WaitAsync(TimeSpan.FromSeconds(30));

        SchedulerRestartOptions impatient = new() { DrainTimeout = TimeSpan.FromMilliseconds(200) };

        Func<Task> restart = async () => await runtime.Restart("acme", impatient);

        await restart.Should().ThrowAsync<SchedulerRestartException>();

        (await restart.Should().ThrowAsync<SchedulerRestartException>(
                "the generation the first attempt gave up on is still running the job, and the next generation "
                + "may not be built over it"))
            .Which.JobsStillExecuting.Should().Be(1,
                "the count is read again rather than remembered, so it says what is true now");

        provider.GetRequiredService<ISchedulerRepository>().Lookup("acme").Should().BeNull(
            "nothing was built, so nothing is bound");

        GatedJob.Release();
        await GatedJob.Finished.WaitAsync(TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// The sweeps a generation ran at start, in the order they were logged, from a mark taken earlier.
    /// </summary>
    private static List<string> Recovered(RecordingLoggerProvider log, int mark)
    {
        return log.Entries
            .Skip(mark)
            .Where(entry => recoveryCounters.Contains(entry.EventId.Id))
            .Select(entry => entry.Message)
            .ToList();
    }

    private static async Task Schedule(IScheduler scheduler)
    {
        await scheduler.ScheduleJob(
            JobBuilder.Create<GatedJob>().WithIdentity("gated").Build(),
            TriggerBuilder.Create().WithIdentity("gated").StartNow().Build());
    }

    private void Recipe(IQuartzBuilder builder)
    {
        builder.UsePersistentStore(store =>
        {
            store.UseSqlite(SqliteFactory.Instance, connectionString);
            store.ProvisionSchema();
        });
    }

    private static ServiceProvider Container(RecordingLoggerProvider log)
    {
        ServiceCollection services = new();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging(builder => builder.AddProvider(log).SetMinimumLevel(LogLevel.Information));
        services.AddQuartz("main", _ => { });
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// A job that runs until the test lets it stop, so that a restart has something to wait for.
    /// </summary>
    [DisallowConcurrentExecution]
    private sealed class GatedJob : IJob
    {
        private static TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private static TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private static TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static Task Started => started.Task;

        public static Task Finished => finished.Task;

        public static void Reset()
        {
            started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public static void Release()
        {
            release.TrySetResult();
        }

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            started.TrySetResult();
            await release.Task.ConfigureAwait(false);
            finished.TrySetResult();
        }
    }
}
