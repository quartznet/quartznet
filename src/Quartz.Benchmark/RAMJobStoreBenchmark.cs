using Quartz.Tests;
using BenchmarkDotNet.Attributes;
using Quartz.Impl.Triggers;
using Quartz.Impl;
using Quartz.Extensibility;

namespace Quartz.Benchmark;

[MemoryDiagnoser]
public class RAMJobStoreBenchmark
{
    private RAMJobStore? _ramJobStore1;
    private RAMJobStore? _ramJobStore2;
    private RAMJobStore? _ramJobStore3;
    private RAMJobStore? _ramJobStore4;
    private RAMJobStore? _ramJobStore5;
    private RAMJobStore? _ramJobStore6;
    private RAMJobStore? _ramJobStore7;
    private RAMJobStore? _ramJobStore8;
    private RAMJobStore? _ramJobStore9;
    private RAMJobStore? _ramJobStore10;
    private IOperableTrigger? _trigger1;
    private IOperableTrigger? _trigger2;
    private IJobDetail? _noOpJob;
    private IJobDetail? _noOpJobNoConcurrent1;
    private IJobDetail? _noOpJobNoConcurrent2;
    private TriggerBuilder<IJob>? _triggerBuilder;
    private IOperableTrigger? _triggerForRamJobStore9;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _noOpJob = JobBuilder.Create<NoOpJob>().WithIdentity(nameof(_noOpJob), "Group1").Build();
        _noOpJobNoConcurrent1 = JobBuilder.Create<NoOpJobDisallowConcurrent>().WithIdentity(nameof(_noOpJobNoConcurrent1), "Group2").Build();
        _noOpJobNoConcurrent2 = JobBuilder.Create<NoOpJobDisallowConcurrent>().WithIdentity(nameof(_noOpJobNoConcurrent2), "Group2").Build();

        _triggerBuilder = TriggerBuilder.Create();
        _trigger1 = (IOperableTrigger) _triggerBuilder.ForJob(_noOpJob).WithSimpleSchedule().StartNow().Build();
        _trigger2 = (IOperableTrigger) _triggerBuilder.ForJob(_noOpJob).WithSimpleSchedule().StartNow().Build();

        // A RAMJobStore that is empty
        _ramJobStore1 = TestJobStores.Ram();
        await _ramJobStore1.Initialize(TestJobStores.Identity());

        // A RAMJobStore with:
        // * a no-op job that allows concurrent execution
        // * no triggers
        _ramJobStore2 = TestJobStores.Ram();
        await _ramJobStore2.Initialize(TestJobStores.Identity());
        await _ramJobStore2.AddJob(_noOpJob);

        // A RAMJobStore with:
        // * a no-op job that disallows concurrent execution
        // * no triggers
        _ramJobStore3 = TestJobStores.Ram();
        await _ramJobStore3.Initialize(TestJobStores.Identity());
        await _ramJobStore3.AddJob(_noOpJobNoConcurrent1);

        // A RAMJobStore with:
        // * a no-op job that allows concurrent execution
        //   triggers:
        //   - 10 triggers with the IgnoreMisfirePolicy misfire instructions, and a computed next fire time
        //   - 1 trigger with the IgnoreMisfirePolicy misfire instructions, and DateTimeOffset.UtcNow plus one day as next fire time
        _ramJobStore4 = TestJobStores.Ram();
        await _ramJobStore4.Initialize(TestJobStores.Identity());
        await _ramJobStore4.AddJob(_noOpJob);
        await _ramJobStore4.AddTrigger(CreateTrigger(new TriggerKey("1"), _noOpJob, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy));
        await _ramJobStore4.AddTrigger(CreateTrigger(new TriggerKey("2"), _noOpJob, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy));
        await _ramJobStore4.AddTrigger(CreateTrigger(new TriggerKey("3"), _noOpJob, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy));
        await _ramJobStore4.AddTrigger(CreateTrigger(new TriggerKey("4"), _noOpJob, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy));
        await _ramJobStore4.AddTrigger(CreateTrigger(new TriggerKey("5"), _noOpJob, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy));
        await _ramJobStore4.AddTrigger(CreateTrigger(new TriggerKey("6"), _noOpJob, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy));
        await _ramJobStore4.AddTrigger(CreateTrigger(new TriggerKey("7"), _noOpJob, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy));
        await _ramJobStore4.AddTrigger(CreateTrigger(new TriggerKey("8"), _noOpJob, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy));
        await _ramJobStore4.AddTrigger(CreateTrigger(new TriggerKey("9"), _noOpJob, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy));
        await _ramJobStore4.AddTrigger(CreateTrigger(new TriggerKey("10"), _noOpJob, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy));
        await _ramJobStore4.AddTrigger(CreateTrigger(new TriggerKey("11"), _noOpJob, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy, DateTimeOffset.UtcNow.AddDays(1)));

        // A RAMJobStore with:
        // * a no-op job that allows concurrent execution
        //   triggers:
        //   - 1 trigger with the IgnoreMisfirePolicy misfire instructions, and a computed next fire time
        _ramJobStore5 = TestJobStores.Ram();
        await _ramJobStore5.Initialize(TestJobStores.Identity());
        await _ramJobStore5.AddJob(_noOpJob);
        await _ramJobStore5.AddTrigger(CreateTrigger(new TriggerKey("1"), _noOpJob, TimeSpan.FromSeconds(1), MisfireInstruction.IgnoreMisfirePolicy));

        // A RAMJobStore with:
        // * a no-op job that disallows concurrent execution
        //   triggers:
        //   - 3 trigger with the IgnoreMisfirePolicy misfire instructions, and a computed next fire time
        // * a no-op job that disallows concurrent execution
        //   triggers:
        //   - 3 triggers with the IgnoreMisfirePolicy misfire instructions, and a computed next fire time
        _ramJobStore6 = TestJobStores.Ram();
        await _ramJobStore6.Initialize(TestJobStores.Identity());
        await _ramJobStore6.AddJob(_noOpJobNoConcurrent1);
        await _ramJobStore6.AddJob(_noOpJobNoConcurrent2);
        await _ramJobStore6.AddTrigger(CreateTrigger(new TriggerKey("1a"), _noOpJobNoConcurrent1, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy));
        await _ramJobStore6.AddTrigger(CreateTrigger(new TriggerKey("1b"), _noOpJobNoConcurrent1, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy));
        await _ramJobStore6.AddTrigger(CreateTrigger(new TriggerKey("1c"), _noOpJobNoConcurrent1, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy));
        await _ramJobStore6.AddTrigger(CreateTrigger(new TriggerKey("2a"), _noOpJobNoConcurrent2, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy));
        await _ramJobStore6.AddTrigger(CreateTrigger(new TriggerKey("2b"), _noOpJobNoConcurrent2, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy));
        await _ramJobStore6.AddTrigger(CreateTrigger(new TriggerKey("2c"), _noOpJobNoConcurrent2, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy));

        // A RAMJobStore with:
        // * a no-op job that disallows concurrent execution
        //   triggers:
        //   - 1 trigger with the IgnoreMisfirePolicy misfire instructions, and a computed next fire time
        // * a no-op job that allows concurrent execution
        //   triggers:
        //   - 1 trigger with the IgnoreMisfirePolicy misfire instructions, and a computed next fire time
        _ramJobStore7 = TestJobStores.Ram();
        await _ramJobStore7.Initialize(TestJobStores.Identity());
        await _ramJobStore7.AddJob(_noOpJobNoConcurrent1);
        await _ramJobStore7.AddJob(_noOpJob);
        await _ramJobStore7.AddTrigger(CreateTrigger(new TriggerKey("1"), _noOpJobNoConcurrent1, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy));
        await _ramJobStore7.AddTrigger(CreateTrigger(new TriggerKey("2"), _noOpJob, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy));

        // A RAMJobStore with:
        // * a no-op job that allows concurrent execution
        //   triggers:
        //   - 7 trigger with the FireNow misfire instructions, and DateTimeOffset.MinValue as next fire time
        //   - 1 trigger with the IgnoreMisfirePolicy misfire instructions, and DateTimeOffset.MinValue as next fire time
        //
        // Important:
        // The triggers use a specialized trigger type that allows misfires to be applied repeatedly while keeping the
        // order of the time triggers stable.
        _ramJobStore8 = TestJobStores.Ram();
        _ramJobStore8.MisfireThreshold = TimeSpan.FromMilliseconds(1);
        await _ramJobStore8.Initialize(TestJobStores.Identity());
        await _ramJobStore8.AddJob(_noOpJob);
        await _ramJobStore8.AddTrigger(CreateTrigger<MisfireTrigger>(new TriggerKey("1"), _noOpJob, TimeSpan.FromTicks(1), MisfireInstruction.SimpleTrigger.FireNow, DateTimeOffset.MinValue));
        await _ramJobStore8.AddTrigger(CreateTrigger<MisfireTrigger>(new TriggerKey("2"), _noOpJob, TimeSpan.FromTicks(1), MisfireInstruction.SimpleTrigger.FireNow, DateTimeOffset.MinValue));
        await _ramJobStore8.AddTrigger(CreateTrigger<MisfireTrigger>(new TriggerKey("3"), _noOpJob, TimeSpan.FromTicks(1), MisfireInstruction.SimpleTrigger.FireNow, DateTimeOffset.MinValue));
        await _ramJobStore8.AddTrigger(CreateTrigger<MisfireTrigger>(new TriggerKey("4"), _noOpJob, TimeSpan.FromTicks(1), MisfireInstruction.SimpleTrigger.FireNow, DateTimeOffset.MinValue));
        await _ramJobStore8.AddTrigger(CreateTrigger<MisfireTrigger>(new TriggerKey("5"), _noOpJob, TimeSpan.FromTicks(1), MisfireInstruction.SimpleTrigger.FireNow, DateTimeOffset.MinValue));
        await _ramJobStore8.AddTrigger(CreateTrigger<MisfireTrigger>(new TriggerKey("6"), _noOpJob, TimeSpan.FromTicks(1), MisfireInstruction.SimpleTrigger.FireNow, DateTimeOffset.MinValue));
        await _ramJobStore8.AddTrigger(CreateTrigger<MisfireTrigger>(new TriggerKey("7"), _noOpJob, TimeSpan.FromTicks(1), MisfireInstruction.SimpleTrigger.FireNow, DateTimeOffset.MinValue));
        await _ramJobStore8.AddTrigger(CreateTrigger<MisfireTrigger>(new TriggerKey("8"), _noOpJob, TimeSpan.FromTicks(1), MisfireInstruction.IgnoreMisfirePolicy, DateTimeOffset.MinValue));

        // A RAMJobStore with:
        // * a no-op job that disallows concurrent execution
        //   triggers:
        //   - 3 triggers with the IgnoreMisfirePolicy misfire instructions, a repeat interval of TimeSpan.MaxValue and a computed next fire time
        _ramJobStore9 = TestJobStores.Ram();
        await _ramJobStore9.Initialize(TestJobStores.Identity());
        await _ramJobStore9.AddJob(_noOpJobNoConcurrent1);
        _triggerForRamJobStore9 = CreateTrigger(new TriggerKey("1"), _noOpJobNoConcurrent1, TimeSpan.MaxValue, MisfireInstruction.IgnoreMisfirePolicy);
        await _ramJobStore9.AddTrigger(_triggerForRamJobStore9);
        await _ramJobStore9.AddTrigger(CreateTrigger(new TriggerKey("2"), _noOpJobNoConcurrent1, TimeSpan.MaxValue, MisfireInstruction.IgnoreMisfirePolicy));
        await _ramJobStore9.AddTrigger(CreateTrigger(new TriggerKey("3"), _noOpJobNoConcurrent1, TimeSpan.MaxValue, MisfireInstruction.IgnoreMisfirePolicy));

        // A RAMJobStore with:
        // * a no-op job that disallows concurrent execution
        // * a no-op job that allows concurrent execution
        // * no triggers for either job
        _ramJobStore10 = TestJobStores.Ram();
        await _ramJobStore10.Initialize(TestJobStores.Identity());
        await _ramJobStore10.AddJob(_noOpJobNoConcurrent1);
        await _ramJobStore10.AddJob(_noOpJob);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _ramJobStore1?.Shutdown();
        _ramJobStore2?.Shutdown();
        _ramJobStore3?.Shutdown();
        _ramJobStore4?.Shutdown();
        _ramJobStore5?.Shutdown();
        _ramJobStore6?.Shutdown();
        _ramJobStore7?.Shutdown();
        _ramJobStore8?.Shutdown();
        _ramJobStore9?.Shutdown();
        _ramJobStore10?.Shutdown();
    }

    [Benchmark]
    public async Task StoreTrigger_ReplaceExisting_SingleThreaded()
    {
       await _ramJobStore2!.AddTrigger(_trigger1!, AddTriggerOptions.Replacing);
    }

    [Benchmark(OperationsPerInvoke = 200_000)]
    public void StoreTrigger_ReplaceExisting_MultiThreaded()
    {
        ManualResetEvent start = new ManualResetEvent(false);

        var tasks = Enumerable.Range(0, 20).Select(i =>
        {
            return Task.Run(async () =>
            {
                start.WaitOne();

                for (var j = 0; j < 10_000; j++)
                {
                    await _ramJobStore2!.AddTrigger(_trigger1!, AddTriggerOptions.Replacing);
                }
            });
        }).ToArray();

        start.Set();

        Task.WaitAll(tasks);
    }

    [Benchmark(OperationsPerInvoke = 100_000)]
    public async Task StoreAndRemoveJob_NoTriggersExistForJob()
    {
        var job = _noOpJob!;

        for (var i = 0; i < 100_000; i++)
        {
            await _ramJobStore1!.AddJob(job, AddJobOptions.Replacing);
            await _ramJobStore1.DeleteJob(job.Key);
        }
    }

    [Benchmark(OperationsPerInvoke = 100_000)]
    public async Task StoreAndRemoveJob_TriggersExistForJob()
    {
        for (var i = 0; i < 100_000; i++)
        {
            await _ramJobStore1!.AddJob(_noOpJob!, AddJobOptions.Replacing);
            await _ramJobStore1.AddTrigger(_trigger1!, AddTriggerOptions.Replacing);
            await _ramJobStore1.AddTrigger(_trigger2!, AddTriggerOptions.Replacing);
            await _ramJobStore1.DeleteJob(_noOpJob!.Key);
        }
    }

    [Benchmark(OperationsPerInvoke = 100_000)]
    public async Task ResumeJobs_EqualsMatch_NoMatchingPausedGroupsAndNoMatchingPausedTriggers()
    {
        var matcher = GroupMatcher<JobKey>.GroupEquals(_noOpJob!.Key.Group);

        for (var i = 0; i < 100_000; i++)
        {
            await _ramJobStore2!.ResumeJobs(matcher);
        }
    }

    [Benchmark(OperationsPerInvoke = 100_000)]
    public async Task ResumeJobs_StartsWithMatch_NoMatchingPausedGroupsAndNoMatchingPausedTriggers()
    {
        var jobStore = _ramJobStore2!;
        var job = _noOpJob!;
        var matcher = GroupMatcher<JobKey>.GroupStartsWith(job.Key.Group);

        for (var i = 0; i < 100_000; i++)
        {
            await jobStore.ResumeJobs(matcher);
        }
    }

    [Benchmark(OperationsPerInvoke = 300_000)]
    public async Task TriggeredJobComplete_ConcurrentExecutionDisallowed_TriggersForJob()
    {
        var jobStore = _ramJobStore9!;
        var job = _noOpJobNoConcurrent1!;

        for (var i = 0; i < 300_000; i++)
        {
            await jobStore.TriggeredJobComplete(_triggerForRamJobStore9!, job, SchedulerInstruction.NoInstruction);
        }
    }

    [Benchmark(OperationsPerInvoke = 300_000)]
    public async Task TriggeredJobComplete_ConcurrentExecutionDisallowed_NoTriggersForJob()
    {
        var jobStore = _ramJobStore3!;
        var job = _noOpJobNoConcurrent1!;

        var trigger = CreateTrigger(new TriggerKey("1"), job, TimeSpan.FromSeconds(1), MisfireInstruction.IgnoreMisfirePolicy);

        for (var i = 0; i < 300_000; i++)
        {
            await jobStore.TriggeredJobComplete(trigger, job, SchedulerInstruction.NoInstruction);
        }
    }

    [Benchmark(OperationsPerInvoke = 300_000)]
    public async Task AcquireNextTriggers_NoTimeTriggersAvailable()
    {
        var jobStore = _ramJobStore1!;

        for (var i = 0; i < 300_000; i++)
        {
            var triggers = await jobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = DateTimeOffset.MaxValue, MaxCount = 1, TimeWindow = TimeSpan.Zero });

            if (triggers.Count != 0)
            {
                throw new Exception($"Expected to acquire zero triggers, but was {triggers.Count}.");
            }
        }
    }

    [Benchmark(OperationsPerInvoke = 300_000)]
    public async Task AcquireNextTriggers_MaxCountIsOneAndAtLeastOneMatchingTimerTriggerAvailable()
    {
        var batchTimeWindow = TimeSpan.FromTicks(100_000);
        var jobStore = _ramJobStore5!;

        for (var i = 0; i < 300_000; i++)
        {
            var triggers = await jobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = DateTimeOffset.MaxValue, MaxCount = 1, TimeWindow = batchTimeWindow });

            foreach (var trigger in triggers)
            {
                await jobStore.ReleaseAcquiredTrigger(trigger);
            }

            if (triggers.Count != 1)
            {
                throw new Exception($"Expected to acquire 1 trigger, but was {triggers.Count}.");
            }
        }
    }

    [Benchmark(OperationsPerInvoke = 300_000)]
    public async Task AcquireNextTriggers_MultipleTimeTriggersForJobThatAllowsConcurrentExecution()
    {
        var batchTimeWindow = TimeSpan.FromTicks(100_000);
        var jobStore = _ramJobStore4!;

        for (var i = 0; i < 300_000; i++)
        {
            var triggers = await jobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = DateTimeOffset.MaxValue, MaxCount = 15, TimeWindow = batchTimeWindow });

            foreach (var trigger in triggers)
            {
                await jobStore.ReleaseAcquiredTrigger(trigger);
            }

            if (triggers.Count != 10)
            {
                throw new Exception($"Expected to acquire 10 triggers, but was {triggers.Count}.");
            }
        }
    }

    [Benchmark(OperationsPerInvoke = 300_000)]
    public async Task AcquireAndRelease_MultipleTriggersForJobThatDisallowsConcurrentExecution()
    {
        var batchTimeWindow = TimeSpan.FromTicks(100_000);
        var jobStore = _ramJobStore6!;

        for (var i = 0; i < 300_000; i++)
        {
            var triggers = await jobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = DateTimeOffset.MaxValue, MaxCount = 2, TimeWindow = batchTimeWindow });

            foreach (var trigger in triggers)
            {
                await jobStore.ReleaseAcquiredTrigger(trigger);
            }

            if (triggers.Count != 2)
            {
                throw new Exception($"Expected to acquire 2 triggers, but was {triggers.Count}.");
            }
        }
    }

    [Benchmark(OperationsPerInvoke = 300_000)]
    public async Task AcquireAndFire_Misfires()
    {
        var batchTimeWindow = TimeSpan.FromTicks(100_000);
        var jobStore = _ramJobStore8!;

        for (var i = 0; i < 300_000; i++)
        {
            var triggers = await jobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = DateTimeOffset.UtcNow.AddDays(1), MaxCount = 1, TimeWindow = batchTimeWindow });

            if (triggers.Count != 1)
            {
                throw new Exception($"Expected to acquire 1 triggers, but was {triggers.Count}.");
            }

            IOperableTrigger operableTrigger = triggers.First();
            await jobStore.TriggersFired([operableTrigger]);

            if (operableTrigger.MisfireInstructionCode != MisfireInstruction.IgnoreMisfirePolicy)
            {
                throw new Exception($"Expected acquired triggers to have {MisfireInstruction.IgnoreMisfirePolicy} as misfire instruction, but was {operableTrigger.MisfireInstructionCode} for trigger ${operableTrigger.Key}.");
            }
        }
    }

    [Benchmark(OperationsPerInvoke = 300_000)]
    public async Task AcquireAndRelease_OneTriggerForJobsThatDisallowConcurrentExecutionAndOneTriggerForJobThatAllowsConcurrentExecution()
    {
        var batchTimeWindow = TimeSpan.FromTicks(100_000);
        var jobStore = _ramJobStore7!;

        for (var i = 0; i < 300_000; i++)
        {
            var triggers = await jobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = DateTimeOffset.MaxValue, MaxCount = 2, TimeWindow = batchTimeWindow });
            if (triggers.Count != 2)
            {
                throw new Exception($"Expected to acquire 2 triggers, but was {triggers.Count}.");
            }

            foreach (var trigger in triggers)
            {
                await jobStore.ReleaseAcquiredTrigger(trigger);
            }
        }
    }

    [Benchmark(OperationsPerInvoke = 300_000)]
    public async Task AcquireAndFireAndComplete_MultipleTriggersForJobsThatDisallowConcurrentExecution()
    {
        var batchTimeWindow = TimeSpan.FromTicks(100_000);
        var jobStore = _ramJobStore6!;

        for (var i = 0; i < 300_000; i++)
        {
            var triggers = await jobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = DateTimeOffset.MaxValue, MaxCount = 3, TimeWindow = batchTimeWindow });
            if (triggers.Count != 2)
            {
                throw new Exception($"Expected to acquire 2 triggers, but was {triggers.Count}.");
            }

            await jobStore.TriggersFired(triggers);

            foreach (var trigger in triggers)
            {
                var job = await jobStore.GetJob(trigger.JobKey);
                await jobStore.TriggeredJobComplete(trigger, job!, SchedulerInstruction.NoInstruction);
            }
        }
    }

    private static IOperableTrigger CreateTrigger(
        TriggerKey triggerKey,
        IJobDetail job,
        TimeSpan repeatInterval,
        int misFirePolicy,
        DateTimeOffset? nextFireTimeUtc = null)
    {
        return Configure(new SimpleTriggerImpl(), triggerKey, job, repeatInterval, misFirePolicy, nextFireTimeUtc);
    }

    private static IOperableTrigger CreateTrigger<T>(
        TriggerKey triggerKey,
        IJobDetail job,
        TimeSpan repeatInterval,
        int misFirePolicy,
        DateTimeOffset? nextFireTimeUtc = null)
        where T : SimpleTriggerImpl, new()
    {
        return Configure(new T(), triggerKey, job, repeatInterval, misFirePolicy, nextFireTimeUtc);
    }

    private static IOperableTrigger Configure(
        SimpleTriggerImpl trigger,
        TriggerKey triggerKey,
        IJobDetail job,
        TimeSpan repeatInterval,
        int misFirePolicy,
        DateTimeOffset? nextFireTimeUtc)
    {
        trigger.Key = triggerKey;
        trigger.JobKey = job.Key;
        trigger.StartTimeUtc = DateTimeOffset.UtcNow;
        trigger.MisfireInstructionCode = misFirePolicy;
        trigger.RepeatInterval = repeatInterval;
        trigger.RepeatCount = SimpleTriggerImpl.RepeatIndefinitely;

        if (nextFireTimeUtc is not null)
        {
            trigger.NextFireTimeUtc = nextFireTimeUtc;
        }
        else
        {
            trigger.ComputeFirstFireTimeUtc(null);
        }

        return trigger;
    }

    [DisallowConcurrentExecution]
    private sealed class NoOpJobDisallowConcurrent : IJob
    {
        /// <summary>
        /// Do nothing.
        /// </summary>
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

    private sealed class NoOpJob : IJob
    {
        /// <summary>
        /// Do nothing.
        /// </summary>
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

    public class NoOpSignaler : ISchedulerSignaler
    {
        public ValueTask NotifySchedulerListenersError(SchedulerErrorContext errorContext, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask NotifySchedulerListenersFinalized(ITrigger trigger, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask NotifySchedulerListenersJobDeleted(JobKey jobKey, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask NotifyTriggerListenersMisfired(ITrigger trigger, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask SignalSchedulingChange(DateTimeOffset? candidateNewNextFireTimeUtc, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

    private sealed class NullJobTypeLoader : ITypeLoader
    {
        public Type? LoadType(string name)
        {
            throw new NotSupportedException($"The benchmark never resolves a job type through this helper, but '{name}' was requested.");
        }
    }

    private sealed class MisfireTrigger : SimpleTriggerImpl
    {
        public MisfireTrigger()
        {
        }

        public override void UpdateAfterMisfire(ICalendar? calendar)
        {
            base.NextFireTimeUtc = base.NextFireTimeUtc.GetValueOrDefault().AddSeconds(1);
        }

        public override void Triggered(ICalendar? calendar)
        {
            base.NextFireTimeUtc = base.NextFireTimeUtc.GetValueOrDefault().AddSeconds(2);
        }
    }
}