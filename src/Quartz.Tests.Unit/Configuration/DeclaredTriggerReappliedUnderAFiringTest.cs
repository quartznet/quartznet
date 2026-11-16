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

using FakeItEasy;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Xml;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// A start re-applies each declared trigger: it reads the stored trigger, works the replacement's fire
/// times out from what it read, and replaces the trigger. A firing between the read and the replace is
/// not undone by it, and an unchanged declaration leaves the trigger's run state as it is, an
/// acquisition of it included (#4324).
/// </summary>
/// <remarks>
/// <para>
/// The firing in between is made by a scheduler the processor is handed, which fires the trigger through
/// the store right after the processor's read, as another node of a cluster would. The processor is the
/// one <c>AddQuartz</c> declarations go through: <see cref="XmlSchedulingDataProcessor" />, with its
/// declarations given in code.
/// </para>
/// <para>
/// Relative scheduling is on (<see cref="SchedulingOptions.ScheduleTriggerRelativeToReplacedTrigger" />),
/// the setting that keeps the stored schedule across a start, so that the replacement's fire times are the
/// ones it read. Clustered, on PostgreSQL: <c>ReplacedTriggerClusteredPostgresTest</c>.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class DeclaredTriggerReappliedUnderAFiringTest
{
    public enum StoreKind
    {
        InMemory,
        Sqlite,
    }

    private static readonly DateTimeOffset epoch = new(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan interval = TimeSpan.FromHours(1);
    private static readonly JobKey jobKey = new("declared", "reapplied");
    private static readonly TriggerKey triggerKey = new("t-declared", "reapplied");

    private FakeTimeProvider clock = null!;
    private RAMJobStore? memoryStore;
    private SqliteTestDatabase? database;

    [SetUp]
    public void StartTheClock()
    {
        clock = new FakeTimeProvider(epoch);
    }

    [TearDown]
    public void DeleteDatabase()
    {
        database?.Dispose();
        database = null;
        memoryStore = null;
    }

    /// <summary>
    /// The issue's case: the trigger fires between the processor's read and its replace, and the replace
    /// does not put back the fire time that has just run.
    /// </summary>
    [TestCase(StoreKind.InMemory)]
    [TestCase(StoreKind.Sqlite)]
    public async Task AFiringBetweenTheReadAndTheReplaceIsNotUndone(StoreKind kind)
    {
        await using ServiceProvider node = await StoreFiredOnce(kind);
        IJobStore store = StoreOf(node);

        await Reapply(node, Every(interval), fireAfterRead: store);

        ITrigger stored = (await store.GetTrigger(triggerKey))!;
        stored.PreviousFireTimeUtc.Should().Be(epoch.Add(interval), "the firing in between is the trigger's last firing");
        stored.NextFireTimeUtc.Should().Be(epoch.Add(2 * interval),
            "the fire time that ran in between is not due again: the replace works from the row as it is when it is made");
        ((ISimpleTrigger) stored).TimesTriggered.Should().Be(2, "both firings are counted");

        clock.Advance(TimeSpan.FromMinutes(1));
        (await AcquireDue(store)).Should().BeEmpty($"{epoch.Add(interval):O} has run, and runs once");
    }

    /// <summary>
    /// A changed declaration starts its schedule from the trigger's last firing, and that is the firing in
    /// between, not the one the processor read.
    /// </summary>
    [TestCase(StoreKind.InMemory)]
    [TestCase(StoreKind.Sqlite)]
    public async Task AChangedDeclarationIsScheduledFromTheFiringInBetween(StoreKind kind)
    {
        await using ServiceProvider node = await StoreFiredOnce(kind);
        IJobStore store = StoreOf(node);

        await Reapply(node, Every(TimeSpan.FromMinutes(30)), fireAfterRead: store);

        ITrigger stored = (await store.GetTrigger(triggerKey))!;
        ((ISimpleTrigger) stored).RepeatInterval.Should().Be(TimeSpan.FromMinutes(30), "the changed declaration is applied");
        stored.PreviousFireTimeUtc.Should().Be(epoch.Add(interval));
        stored.NextFireTimeUtc.Should().Be(epoch.AddMinutes(90),
            "the next fire time is the new schedule's first after the trigger's last firing, which is the one in between; "
            + "from the firing the processor read it would be half an hour ago");
    }

    /// <summary>
    /// An unchanged declaration re-applied while the trigger is acquired leaves the acquisition firable:
    /// the trigger's fire times, its count and its state are what they were.
    /// </summary>
    [TestCase(StoreKind.InMemory)]
    [TestCase(StoreKind.Sqlite)]
    public async Task AnUnchangedDeclarationLeavesAnAcquisitionOfItFirable(StoreKind kind)
    {
        await using ServiceProvider node = await StoreFiredOnce(kind);
        IJobStore store = StoreOf(node);

        clock.Advance(TimeSpan.FromMinutes(30) - TimeSpan.FromSeconds(10));
        List<IOperableTrigger> held = await AcquireDue(store);
        held.Should().ContainSingle("the premise: the trigger is acquired for its next fire time");

        await Reapply(node, Every(interval), fireAfterRead: null);

        (await (await SchedulerOf(node)).GetTriggerState(triggerKey)).Should().Be(TriggerState.Normal);
        ITrigger stored = (await store.GetTrigger(triggerKey))!;
        stored.NextFireTimeUtc.Should().Be(epoch.Add(interval));
        ((ISimpleTrigger) stored).TimesTriggered.Should().Be(1);

        clock.Advance(TimeSpan.FromSeconds(10));
        List<TriggerFiredResult> fired = await store.TriggersFired(held);
        fired.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull(
            "re-applying the same declaration is not a new trigger, and the acquisition of the stored one still holds");
    }

    /// <summary>
    /// The control for the one above: what keeping a row leaves out. A trigger in <c>ERROR</c> is still
    /// stored afresh by a start, which is how a redeploy that fixes its job brings it back.
    /// </summary>
    [Test]
    public async Task AnUnchangedDeclarationStillBringsBackATriggerInError()
    {
        await using ServiceProvider node = await StoreFiredOnce(StoreKind.Sqlite);
        await using (SqliteConnection connection = new(database!.ConnectionString))
        {
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "UPDATE QRTZ_TRIGGERS SET TRIGGER_STATE = 'ERROR'";
            (await command.ExecuteNonQueryAsync()).Should().Be(1);
        }

        await Reapply(node, Every(interval), fireAfterRead: null);

        (await (await SchedulerOf(node)).GetTriggerState(triggerKey)).Should().Be(TriggerState.Normal);
    }

    private static Action<TriggerBuilder<IJob>> Every(TimeSpan every)
    {
        return t => t.WithSimpleSchedule(s => s.WithInterval(every).RepeatForever());
    }

    /// <summary>
    /// A node whose store holds the declared job and trigger, the trigger fired once at <see cref="epoch" />,
    /// and the clock half an hour on.
    /// </summary>
    private async Task<ServiceProvider> StoreFiredOnce(StoreKind kind)
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "declared-trigger-reapplied-under-a-firing";
                options.InstanceId = "one";
            });
            q.UseTimeProvider(clock);
            UseStore(q, kind);
        });

        ServiceProvider provider = services.BuildServiceProvider();
        IScheduler scheduler = await SchedulerOf(provider);
        await scheduler.ScheduleJob(
            JobBuilder.Create<NoOpJob>().WithIdentity(jobKey).Build(),
            TriggerBuilder.Create().WithIdentity(triggerKey).ForJob(jobKey).StartAt(epoch)
                .WithSimpleSchedule(s => s.WithInterval(interval).RepeatForever()).Build());

        await FireAndComplete(StoreOf(provider));
        clock.Advance(TimeSpan.FromMinutes(30));
        return provider;
    }

    /// <summary>
    /// What a start does with a declared trigger the store holds, through the scheduler of
    /// <paramref name="node" />. With <paramref name="fireAfterRead" />, the trigger's next fire time comes
    /// and it is fired and completed through that store right after the processor's first read of it.
    /// </summary>
    private async Task Reapply(ServiceProvider node, Action<TriggerBuilder<IJob>> schedule, IJobStore? fireAfterRead)
    {
        TriggerBuilder<IJob> declared = TriggerBuilder.Create(clock).WithIdentity(triggerKey).ForJob(jobKey).StartNow();
        schedule(declared);

        DeclaringProcessor processor = new(clock, JobBuilder.Create<NoOpJob>().WithIdentity(jobKey).Build(), declared.Build());
        IScheduler scheduler = await SchedulerOf(node);

        await processor.ScheduleJobs(fireAfterRead is null
            ? scheduler
            : new FiringAfterReadScheduler(scheduler, async () =>
            {
                clock.Advance(TimeSpan.FromMinutes(30));
                await FireAndComplete(fireAfterRead);
            }));
    }

    private void UseStore(IQuartzBuilder q, StoreKind kind)
    {
        if (kind == StoreKind.InMemory)
        {
            memoryStore ??= new RAMJobStore(NullLoggerFactory.Instance, A.Fake<ISchedulerSignaler>(), clock);
            q.UseJobStore(memoryStore);
            return;
        }

        database ??= new SqliteTestDatabase("declared-trigger-reapplied");
        string connectionString = database.ConnectionString;
        q.UsePersistentStore(store =>
        {
            store.UseSqlite(SqliteFactory.Instance, connectionString);
            store.ProvisionSchema();
        });
    }

    private static ValueTask<IScheduler> SchedulerOf(ServiceProvider provider)
    {
        return provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
    }

    private IJobStore StoreOf(ServiceProvider provider)
    {
        return memoryStore ?? provider.GetRequiredService<IJobStore>();
    }

    private ValueTask<List<IOperableTrigger>> AcquireDue(IJobStore store)
    {
        return store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = clock.GetUtcNow().AddMinutes(1),
            MaxCount = 1,
            TimeWindow = TimeSpan.Zero,
        });
    }

    private async Task FireAndComplete(IJobStore store)
    {
        List<IOperableTrigger> acquired = await AcquireDue(store);
        acquired.Should().ContainSingle("the premise: the trigger is due");

        TriggerFiredBundle bundle = (await store.TriggersFired(acquired)).Should().ContainSingle().Subject.TriggerFiredBundle!;
        bundle.Should().NotBeNull("the premise: nothing stands in the trigger's way");

        SchedulerInstruction instruction = bundle.Trigger.ExecutionComplete(A.Fake<IJobExecutionContext>(), null);
        await store.TriggeredJobComplete(bundle.Trigger, bundle.JobDetail, instruction);
    }

    /// <summary>
    /// The processor a start runs declarations through, given its declarations in code, with relative
    /// scheduling on.
    /// </summary>
    private sealed class DeclaringProcessor : XmlSchedulingDataProcessor
    {
        private readonly IJobDetail job;
        private readonly ITrigger trigger;

        public DeclaringProcessor(TimeProvider timeProvider, IJobDetail job, ITrigger trigger)
            : base(NullLogger<XmlSchedulingDataProcessor>.Instance, new SimpleTypeLoader(), timeProvider)
        {
            this.job = job;
            this.trigger = trigger;
            ScheduleTriggerRelativeToReplacedTrigger = true;
        }

        protected override List<IJobDetail> LoadedJobs => [job];

        protected override List<ITrigger> LoadedTriggers => [trigger];
    }

    /// <summary>
    /// Runs <c>afterRead</c> once, right after the first read of the declared trigger, before the read is
    /// handed back: the processor then works from a trigger that has fired since.
    /// </summary>
    private sealed class FiringAfterReadScheduler : DelegatingScheduler
    {
        private Func<Task>? afterRead;

        public FiringAfterReadScheduler(IScheduler scheduler, Func<Task> afterRead) : base(scheduler)
        {
            this.afterRead = afterRead;
        }

        public override async ValueTask<ITrigger?> GetTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            ITrigger? read = await base.GetTrigger(triggerKey, cancellationToken);
            if (read is not null && Interlocked.Exchange(ref afterRead, null) is { } fire)
            {
                await fire();
            }

            return read;
        }
    }

    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
