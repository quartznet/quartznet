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

#nullable enable

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Configuration;
using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// <see cref="QuartzBuilderExtensions.RunAtStartup" />: a job run once each time its scheduler starts,
/// on both shipped stores (#4019).
/// </summary>
[NonParallelizable]
public sealed class RunAtStartupTest
{
    private static readonly JobKey warmKey = new("warm-cache", "startup-tests");
    private static readonly TimeSpan waitLimit = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Long enough for a second run to have happened had anything scheduled one: a startup trigger is due
    /// at once, and the scheduler thread is woken when it is stored.
    /// </summary>
    private static readonly TimeSpan settle = TimeSpan.FromMilliseconds(1500);

    private readonly List<IScheduler> schedulers = [];
    private SqliteTestDatabase? database;

    public enum Store
    {
        InMemory,
        Sqlite
    }

    [SetUp]
    public void SetUp()
    {
        CountingJob.Reset();
        SerialJob.Reset();
    }

    [TearDown]
    public async Task TearDown()
    {
        foreach (IScheduler scheduler in schedulers)
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        schedulers.Clear();
        database?.Dispose();
        database = null;
    }

    [TestCase(Store.InMemory)]
    [TestCase(Store.Sqlite)]
    public async Task AJobRunsOnceWhenTheSchedulerStarts(Store store)
    {
        IScheduler scheduler = await Build(store, q => q
            .AddJob<CountingJob>(j => j.WithIdentity(warmKey).StoreDurably())
            .RunAtStartup(warmKey));

        await scheduler.Start();
        await CountingJob.FirstRun.Task.WaitAsync(waitLimit);
        await Task.Delay(settle);

        CountingJob.Runs.Should().Be(1, "one start is one run");
        CountingJob.TriggerGroups.Should().Equal([SchedulerConstants.StartupGroup],
            "the run is a trigger of its own, in the group a job or a listing can tell it by");
        (await StartupTriggers(scheduler)).Should().BeEmpty("a one-shot trigger is deleted once it has fired");
        (await scheduler.Exists(warmKey)).Should().BeTrue("the job is the application's, and outlives its startup run");
    }

    [TestCase(Store.InMemory)]
    [TestCase(Store.Sqlite)]
    public async Task ASchedulerBuiltInStandbyRunsItWhenItIsStarted(Store store)
    {
        IScheduler scheduler = await Build(store, q => q
            .AddJob<CountingJob>(j => j.WithIdentity(warmKey).StoreDurably())
            .RunAtStartup(warmKey));

        await Task.Delay(settle);
        CountingJob.Runs.Should().Be(0, "building a scheduler is not starting it");
        (await StartupTriggers(scheduler)).Should().BeEmpty("nothing is scheduled until the scheduler starts");

        await scheduler.Start();
        await CountingJob.FirstRun.Task.WaitAsync(waitLimit);

        CountingJob.Runs.Should().Be(1);
    }

    [TestCase(Store.InMemory)]
    [TestCase(Store.Sqlite)]
    public async Task LeavingStandbyIsNotAStart(Store store)
    {
        IScheduler scheduler = await Build(store, q => q
            .AddJob<CountingJob>(j => j.WithIdentity(warmKey).StoreDurably())
            .RunAtStartup(warmKey));

        await scheduler.Start();
        await CountingJob.FirstRun.Task.WaitAsync(waitLimit);

        await scheduler.Standby();
        await scheduler.Start();
        await Task.Delay(settle);

        CountingJob.Runs.Should().Be(1, "standby pauses firing; it does not stop the scheduler, so resuming starts nothing");
    }

    [Test]
    public async Task ARestartRunsItAgain()
    {
        database = new SqliteTestDatabase("run-at-startup-restart");
        string instanceName = "run-at-startup-" + Guid.NewGuid().ToString("N");

        IScheduler first = await Build(Store.Sqlite, Registration, instanceName);
        await first.Start();
        await CountingJob.FirstRun.Task.WaitAsync(waitLimit);
        await first.Shutdown(waitForJobsToComplete: true);
        schedulers.Remove(first);

        CountingJob.Reset();
        IScheduler second = await Build(Store.Sqlite, Registration, instanceName);
        await second.Start();
        await CountingJob.FirstRun.Task.WaitAsync(waitLimit);
        await Task.Delay(settle);

        CountingJob.Runs.Should().Be(1,
            "the second start of a scheduler over the same database runs the job again, and only its own run: the "
            + "first start's trigger was deleted when it fired");

        static void Registration(IQuartzBuilder q) => q
            .AddJob<CountingJob>(j => j.WithIdentity(warmKey).StoreDurably())
            .RunAtStartup(warmKey);
    }

    [TestCase(Store.InMemory)]
    [TestCase(Store.Sqlite)]
    public async Task ItRunsBesideTheJobsOwnTrigger(Store store)
    {
        IScheduler scheduler = await Build(store, q => q
            .ScheduleJob<CountingJob>(
                trigger => trigger.WithIdentity("nightly", "startup-tests").WithCronSchedule("0 0 2 * * ?"),
                job => job.WithIdentity(warmKey))
            .RunAtStartup(warmKey));

        await scheduler.Start();
        await CountingJob.FirstRun.Task.WaitAsync(waitLimit);
        await Task.Delay(settle);

        CountingJob.Runs.Should().Be(1);
        (await scheduler.GetTriggersOfJob(warmKey)).Select(x => x.Key).Should().Equal([new TriggerKey("nightly", "startup-tests")],
            "the cron trigger is untouched, and the startup run left nothing behind");
    }

    [Test]
    public async Task AJobThatDisallowsConcurrentExecutionRunsItsStartupRunInTurn()
    {
        JobKey serial = new("serial", "startup-tests");
        IScheduler scheduler = await Build(Store.InMemory, q => q
            .ScheduleJob<SerialJob>(
                trigger => trigger.WithIdentity("now", "startup-tests").StartNow(),
                job => job.WithIdentity(serial).StoreDurably())
            .RunAtStartup(serial));

        await scheduler.Start();
        await SerialJob.SecondRun.Task.WaitAsync(waitLimit);

        SerialJob.MostAtOnce.Should().Be(1, "the startup run is an ordinary trigger of a serial job, so it waits its turn");
    }

    [Test]
    public async Task AStartFailsWhenTheJobIsNotStored()
    {
        IScheduler scheduler = await Build(Store.InMemory, q => q.RunAtStartup(new JobKey("missing", "startup-tests")));

        Func<Task> start = async () => await scheduler.Start();

        await start.Should().ThrowAsync<SchedulerException>().WithMessage("*RunAtStartup*'startup-tests.missing'*not stored*",
            "a startup run that silently never happens is the failure this registration exists to prevent");
    }

    [TestCase(Store.InMemory)]
    [TestCase(Store.Sqlite)]
    public async Task ALeftoverFromAnEarlierStartIsReplacedRatherThanAddedTo(Store store)
    {
        IScheduler scheduler = await Build(store, q => q
            .AddJob<CountingJob>(j => j.WithIdentity(warmKey).StoreDurably())
            .RunAtStartup(warmKey));

        // What a crash before the run fired leaves: this node's startup trigger, still stored, still due.
        ITrigger leftover = StartupRunPlugin.CreateTrigger(warmKey, scheduler.SchedulerInstanceId, clustered: false, TimeProvider.System);
        await scheduler.ScheduleJob(leftover);

        await scheduler.Start();
        await CountingJob.FirstRun.Task.WaitAsync(waitLimit);
        await Task.Delay(settle);

        CountingJob.Runs.Should().Be(1, "the start's run replaces the run the earlier start never made, rather than adding to it");
        (await scheduler.Exists(leftover.Key)).Should().BeFalse("the leftover was unscheduled before it could fire");
        (await StartupTriggers(scheduler)).Should().BeEmpty();
    }

    [Test]
    public async Task ALeftoverAlreadyReservedIsLeftToRun()
    {
        RAMJobStore? ram = null;
        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(q => q
                .UseJobStore(provider => ram = ActivatorUtilities.CreateInstance<RAMJobStore>(provider))
                .AddJob<CountingJob>(j => j.WithIdentity(warmKey).StoreDurably())
                .RunAtStartup(warmKey))
            .BuildScheduler();
        schedulers.Add(scheduler);

        ITrigger leftover = StartupRunPlugin.CreateTrigger(warmKey, scheduler.SchedulerInstanceId, clustered: false, TimeProvider.System);
        await scheduler.ScheduleJob(leftover);

        // Reserved, as a node about to fire it would have it.
        List<IOperableTrigger> reserved = await ram!.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = DateTimeOffset.UtcNow.AddMinutes(1),
            MaxCount = 10,
            TimeWindow = TimeSpan.Zero
        });
        reserved.Select(x => x.Key).Should().Equal([leftover.Key], "the precondition: the leftover is reserved");

        await scheduler.Start();
        await CountingJob.FirstRun.Task.WaitAsync(waitLimit);

        (await scheduler.Exists(leftover.Key)).Should().BeTrue("a run already reserved or running is happening, so it is left alone");
        CountingJob.Runs.Should().Be(1, "this start's own run fired; the reserved one is left to whoever reserved it");
    }

    [Test]
    public async Task NamingTheJobTwiceIsOneRunAtEachStart()
    {
        IScheduler scheduler = await Build(Store.InMemory, q => q
            .AddJob<CountingJob>(j => j.WithIdentity(warmKey).StoreDurably())
            .RunAtStartup(warmKey)
            .RunAtStartup(new JobKey(warmKey.Name, warmKey.Group)));

        await scheduler.Start();
        await CountingJob.FirstRun.Task.WaitAsync(waitLimit);
        await Task.Delay(settle);

        CountingJob.Runs.Should().Be(1,
            "two modules that both want the job warm register one run, not one each");
    }

    [Test]
    public void ANodeRecognisesItsOwnLeftoversAndNoOtherNodes()
    {
        TriggerKey nodeA = StartupRunPlugin.CreateTrigger(warmKey, "node", clustered: true, TimeProvider.System).Key;
        TriggerKey nodeDotA = StartupRunPlugin.CreateTrigger(warmKey, "node.a", clustered: true, TimeProvider.System).Key;

        StartupRunPlugin.IsLeftover(nodeA, "node", clustered: true).Should().BeTrue();
        StartupRunPlugin.IsLeftover(nodeDotA, "node", clustered: true).Should().BeFalse(
            "a node whose id extends this one's is another node, and its leftover is its own to replace");
        StartupRunPlugin.IsLeftover(nodeA, "node.a", clustered: true).Should().BeFalse();
        StartupRunPlugin.IsLeftover(nodeDotA, "node", clustered: false).Should().BeTrue(
            "a store shared with nobody has only this node, so every leftover is its own");
        StartupRunPlugin.IsLeftover(new TriggerKey(nodeA.Name, "elsewhere"), "node", clustered: true).Should().BeFalse(
            "only the startup group holds startup runs");

        string longId = new('n', 200);
        TriggerKey longKey = StartupRunPlugin.CreateTrigger(warmKey, longId, clustered: true, TimeProvider.System).Key;
        longKey.Name.Length.Should().BeLessThanOrEqualTo(150, "the narrowest shipped TRIGGER_NAME is 150");
        StartupRunPlugin.IsLeftover(longKey, longId, clustered: true).Should().BeTrue("a long id is carried as its hash, and still recognised");
        StartupRunPlugin.IsLeftover(longKey, longId + "x", clustered: true).Should().BeFalse();
    }

    [Test]
    public void NullIsRefused()
    {
        Action noBuilder = () => QuartzBuilderExtensions.RunAtStartup(null!, warmKey);
        Action noKey = () => QuartzSchedulerBuilder.Create(q => q.RunAtStartup(null!));

        noBuilder.Should().Throw<ArgumentNullException>();
        noKey.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void TheTriggerIsPinnedToTheNodeThatStartedOnlyInACluster()
    {
        FakeClock clock = new(new DateTimeOffset(2031, 6, 17, 10, 0, 0, TimeSpan.Zero));

        ITrigger clustered = StartupRunPlugin.CreateTrigger(warmKey, "node-a", clustered: true, clock);
        ITrigger alone = StartupRunPlugin.CreateTrigger(warmKey, "NON_CLUSTERED", clustered: false, clock);

        clustered.PreferredNode.Should().Be(PreferredNode.For("node-a"),
            "in a cluster each node runs its own start's run, so it must not be acquired by another node while it is alive");
        alone.PreferredNode.Should().Be(PreferredNode.None);

        clustered.Key.Group.Should().Be(SchedulerConstants.StartupGroup);
        clustered.Key.Name.Should().NotBe(alone.Key.Name, "a name new to each start keeps one start's run from replacing another's");
        clustered.JobKey.Should().Be(warmKey);
        clustered.StartTimeUtc.Should().Be(clock.GetUtcNow(), "it is due as the scheduler starts");
        SimpleTriggerImpl simple = clustered.Should().BeOfType<SimpleTriggerImpl>().Subject;
        simple.RepeatCount.Should().Be(0, "it fires once");
        simple.MisfireInstruction.Should().Be(SimpleTriggerMisfireInstruction.FireNow,
            "a start that was slow is still a start, so a late run fires rather than being dropped");
    }

    private async Task<IScheduler> Build(Store store, Action<IQuartzBuilder> configure, string? instanceName = null)
    {
        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(q =>
            {
                q.ConfigureScheduler(options =>
                {
                    options.InstanceName = instanceName ?? "run-at-startup-" + Guid.NewGuid().ToString("N");
                    options.IdleWaitTime = TimeSpan.FromSeconds(1);
                });

                if (store == Store.Sqlite)
                {
                    database ??= new SqliteTestDatabase("run-at-startup");
                    string connectionString = database.ConnectionString;
                    q.UsePersistentStore(persistent =>
                    {
                        persistent.UseSqlite(SqliteFactory.Instance, connectionString);
                        persistent.ProvisionSchema();
                    });
                }
                else
                {
                    q.UseInMemoryStore();
                }

                configure(q);
            })
            .BuildScheduler();

        schedulers.Add(scheduler);
        return scheduler;
    }

    private static async Task<List<TriggerKey>> StartupTriggers(IScheduler scheduler)
    {
        PagedResult<TriggerHeader> page = await scheduler.QueryTriggers(new TriggerQuery
        {
            Group = GroupMatcher<TriggerKey>.GroupEquals(SchedulerConstants.StartupGroup)
        });

        return [.. page.Items.Select(x => x.Key)];
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    public sealed class CountingJob : IJob
    {
        private static int runs;
        private static readonly Lock gate = new();
        private static readonly List<string> triggerGroups = [];

        public static int Runs => Volatile.Read(ref runs);

        public static List<string> TriggerGroups
        {
            get
            {
                lock (gate)
                {
                    return [.. triggerGroups];
                }
            }
        }

        public static TaskCompletionSource FirstRun { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static void Reset()
        {
            Volatile.Write(ref runs, 0);
            lock (gate)
            {
                triggerGroups.Clear();
            }

            FirstRun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                triggerGroups.Add(context.Trigger.Key.Group);
            }

            Interlocked.Increment(ref runs);
            FirstRun.TrySetResult();
            return default;
        }
    }

    [DisallowConcurrentExecution]
    public sealed class SerialJob : IJob
    {
        private static int running;
        private static int mostAtOnce;
        private static int runs;

        public static int MostAtOnce => Volatile.Read(ref mostAtOnce);

        public static TaskCompletionSource SecondRun { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static void Reset()
        {
            Volatile.Write(ref running, 0);
            Volatile.Write(ref mostAtOnce, 0);
            Volatile.Write(ref runs, 0);
            SecondRun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            int now = Interlocked.Increment(ref running);
            int seen;
            do
            {
                seen = Volatile.Read(ref mostAtOnce);
            }
            while (now > seen && Interlocked.CompareExchange(ref mostAtOnce, now, seen) != seen);

            // Long enough for a second firing to overlap this one, were it allowed to.
            await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
            Interlocked.Decrement(ref running);

            if (Interlocked.Increment(ref runs) == 2)
            {
                SecondRun.TrySetResult();
            }
        }
    }
}
