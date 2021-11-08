using BenchmarkDotNet.Attributes;
using Quartz.Impl.Matchers;
using Quartz.Impl.Triggers;
using Quartz.Job;
using Quartz.Simpl;
using Quartz.Spi;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Quartz.Benchmark
{
    [MemoryDiagnoser]
    public class RAMJobStoreBenchmark
    {
        private RAMJobStore _ramJobStore1;
        private IOperableTrigger _trigger1;
        private IOperableTrigger _trigger2;
        private IJobDetail _noOpJob;
        private IJobDetail _noOpJobNoConcurrent;
        private TriggerBuilder _triggerBuilder;
        private RAMJobStore _ramJobStore2;
        private RAMJobStore _ramJobStore3;
        private RAMJobStore _ramJobStore10;
        private RAMJobStore _ramJobStore5;
        private RAMJobStore _ramJobStore6;
        private RAMJobStore _ramJobStore7;
        private RAMJobStore _ramJobStore4;
        private RAMJobStore _ramJobStore9;
        private IOperableTrigger _triggerForRamJobStore9;
        private readonly RAMJobStore _ramJobStore8;

        public RAMJobStoreBenchmark()
        {
            _noOpJob = JobBuilder.Create<NoOpJob>().WithIdentity("Job1", "Group1").Build();
            _noOpJobNoConcurrent = JobBuilder.Create<NoOpJobDisallowConcurrent>().WithIdentity("Job2", "Group2").Build();

            _triggerBuilder = TriggerBuilder.Create();
            _trigger1 = (IOperableTrigger)_triggerBuilder.ForJob(_noOpJob).WithSimpleSchedule().StartNow().Build();
            _trigger2 = (IOperableTrigger)_triggerBuilder.ForJob(_noOpJob).WithSimpleSchedule().StartNow().Build();

            // A RAMJobStore that is empty
            _ramJobStore1 = new RAMJobStore();
            _ramJobStore1.Initialize(new NullJobTypeLoader(), new NoOpSignaler()).GetAwaiter().GetResult();

            // A RAMJobStore with:
            // * a no-op job that allows concurrent execution
            // * no triggers
            _ramJobStore2 = new RAMJobStore();
            _ramJobStore2.Initialize(new NullJobTypeLoader(), new NoOpSignaler()).GetAwaiter().GetResult();
            _ramJobStore2.StoreJob(_noOpJob, false).GetAwaiter().GetResult();

            // A RAMJobStore with:
            // * a no-op job that disallows concurrent execution
            // * no triggers
            _ramJobStore3 = new RAMJobStore();
            _ramJobStore3.Initialize(new NullJobTypeLoader(), new NoOpSignaler()).GetAwaiter().GetResult();
            _ramJobStore3.StoreJob(_noOpJobNoConcurrent, false).GetAwaiter().GetResult();

            // A RAMJobStore with:
            // * a no-op job that allows concurrent execution
            //   triggers:
            //   - 10 triggers with the IgnoreMisfirePolicy misfire instructions, and a computed next fire time
            //   - 1 trigger with the IgnoreMisfirePolicy misfire instructions, and DateTimeOffset.UtcNow plus one day as next fire time
            _ramJobStore4 = new RAMJobStore();
            _ramJobStore4.Initialize(new NullJobTypeLoader(), new NoOpSignaler()).GetAwaiter().GetResult();
            _ramJobStore4.StoreJob(_noOpJob, false).GetAwaiter().GetResult();
            _ramJobStore4.StoreTrigger(CreateTrigger(new TriggerKey("1"), _noOpJob, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy), false).GetAwaiter().GetResult();
            _ramJobStore4.StoreTrigger(CreateTrigger(new TriggerKey("2"), _noOpJob, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy), false).GetAwaiter().GetResult();
            _ramJobStore4.StoreTrigger(CreateTrigger(new TriggerKey("3"), _noOpJob, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy), false).GetAwaiter().GetResult();
            _ramJobStore4.StoreTrigger(CreateTrigger(new TriggerKey("4"), _noOpJob, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy), false).GetAwaiter().GetResult();
            _ramJobStore4.StoreTrigger(CreateTrigger(new TriggerKey("5"), _noOpJob, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy), false).GetAwaiter().GetResult();
            _ramJobStore4.StoreTrigger(CreateTrigger(new TriggerKey("6"), _noOpJob, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy), false).GetAwaiter().GetResult();
            _ramJobStore4.StoreTrigger(CreateTrigger(new TriggerKey("7"), _noOpJob, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy), false).GetAwaiter().GetResult();
            _ramJobStore4.StoreTrigger(CreateTrigger(new TriggerKey("8"), _noOpJob, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy), false).GetAwaiter().GetResult();
            _ramJobStore4.StoreTrigger(CreateTrigger(new TriggerKey("9"), _noOpJob, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy), false).GetAwaiter().GetResult();
            _ramJobStore4.StoreTrigger(CreateTrigger(new TriggerKey("10"), _noOpJob, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy), false).GetAwaiter().GetResult();
            _ramJobStore4.StoreTrigger(CreateTrigger(new TriggerKey("11"), _noOpJob, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy, DateTimeOffset.UtcNow.AddDays(1)), false).GetAwaiter().GetResult();

            // A RAMJobStore with:
            // * a no-op job that allows concurrent execution
            //   triggers:
            //   - 1 trigger with the IgnoreMisfirePolicy misfire instructions, and a computed next fire time
            _ramJobStore5 = new RAMJobStore();
            _ramJobStore5.Initialize(new NullJobTypeLoader(), new NoOpSignaler()).GetAwaiter().GetResult();
            _ramJobStore5.StoreJob(_noOpJob, false).GetAwaiter().GetResult();
            _ramJobStore5.StoreTrigger(CreateTrigger(new TriggerKey("1"), _noOpJob, TimeSpan.FromSeconds(1), MisfireInstruction.IgnoreMisfirePolicy), false).GetAwaiter().GetResult();

            // A RAMJobStore with:
            // * a no-op job that disallows concurrent execution
            //   triggers:
            //   - 3 trigger with the IgnoreMisfirePolicy misfire instructions, and a computed next fire time
            // * a no-op job that allows concurrent execution
            //   triggers:
            //   - 1 trigger with the IgnoreMisfirePolicy misfire instructions, and a computed next fire time
            _ramJobStore6 = new RAMJobStore();
            _ramJobStore6.Initialize(new NullJobTypeLoader(), new NoOpSignaler()).GetAwaiter().GetResult();
            _ramJobStore6.StoreJob(_noOpJobNoConcurrent, false).GetAwaiter().GetResult();
            _ramJobStore6.StoreJob(_noOpJob, false).GetAwaiter().GetResult();
            _ramJobStore6.StoreTrigger(CreateTrigger(new TriggerKey("1"), _noOpJobNoConcurrent, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy), false).GetAwaiter().GetResult();
            _ramJobStore6.StoreTrigger(CreateTrigger(new TriggerKey("2"), _noOpJobNoConcurrent, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy), false).GetAwaiter().GetResult();
            _ramJobStore6.StoreTrigger(CreateTrigger(new TriggerKey("3"), _noOpJobNoConcurrent, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy), false).GetAwaiter().GetResult();
            _ramJobStore6.StoreTrigger(CreateTrigger(new TriggerKey("4"), _noOpJob, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy), false).GetAwaiter().GetResult();

            // A RAMJobStore with:
            // * a no-op job that disallows concurrent execution
            //   triggers:
            //   - 1 trigger with the IgnoreMisfirePolicy misfire instructions, and a computed next fire time
            // * a no-op job that allows concurrent execution
            //   triggers:
            //   - 1 trigger with the IgnoreMisfirePolicy misfire instructions, and a computed next fire time
            _ramJobStore7 = new RAMJobStore();
            _ramJobStore7.Initialize(new NullJobTypeLoader(), new NoOpSignaler()).GetAwaiter().GetResult();
            _ramJobStore7.StoreJob(_noOpJobNoConcurrent, false).GetAwaiter().GetResult();
            _ramJobStore7.StoreJob(_noOpJob, false).GetAwaiter().GetResult();
            _ramJobStore7.StoreTrigger(CreateTrigger(new TriggerKey("1"), _noOpJobNoConcurrent, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy), false).GetAwaiter().GetResult();
            _ramJobStore7.StoreTrigger(CreateTrigger(new TriggerKey("2"), _noOpJob, TimeSpan.FromTicks(1000), MisfireInstruction.IgnoreMisfirePolicy), false).GetAwaiter().GetResult();

            // A RAMJobStore with:
            // * a no-op job that allows concurrent execution
            //   triggers:
            //   - 7 trigger with the FireNow misfire instructions, and DateTimeOffset.MinValue as next fire time
            //   - 1 trigger with the IgnoreMisfirePolicy misfire instructions, and DateTimeOffset.MinValue as next fire time
            //
            // Important:
            // The triggers use a specialized trigger type that allows misfires to be applied repeatedly while keeping the
            // order of the time triggers stable.
            _ramJobStore8 = new RAMJobStore {MisfireThreshold = TimeSpan.FromMilliseconds(1)};
            _ramJobStore8.Initialize(new NullJobTypeLoader(), new NoOpSignaler()).GetAwaiter().GetResult();
            _ramJobStore8.StoreJob(_noOpJob, false).GetAwaiter().GetResult();
            _ramJobStore8.StoreTrigger(CreateTrigger<MisfireTrigger>(new TriggerKey("1"), _noOpJob, TimeSpan.FromTicks(1), MisfireInstruction.SimpleTrigger.FireNow, DateTimeOffset.MinValue), false).GetAwaiter().GetResult();
            _ramJobStore8.StoreTrigger(CreateTrigger<MisfireTrigger>(new TriggerKey("2"), _noOpJob, TimeSpan.FromTicks(1), MisfireInstruction.SimpleTrigger.FireNow, DateTimeOffset.MinValue), false).GetAwaiter().GetResult();
            _ramJobStore8.StoreTrigger(CreateTrigger<MisfireTrigger>(new TriggerKey("3"), _noOpJob, TimeSpan.FromTicks(1), MisfireInstruction.SimpleTrigger.FireNow, DateTimeOffset.MinValue), false).GetAwaiter().GetResult();
            _ramJobStore8.StoreTrigger(CreateTrigger<MisfireTrigger>(new TriggerKey("4"), _noOpJob, TimeSpan.FromTicks(1), MisfireInstruction.SimpleTrigger.FireNow, DateTimeOffset.MinValue), false).GetAwaiter().GetResult();
            _ramJobStore8.StoreTrigger(CreateTrigger<MisfireTrigger>(new TriggerKey("5"), _noOpJob, TimeSpan.FromTicks(1), MisfireInstruction.SimpleTrigger.FireNow, DateTimeOffset.MinValue), false).GetAwaiter().GetResult();
            _ramJobStore8.StoreTrigger(CreateTrigger<MisfireTrigger>(new TriggerKey("6"), _noOpJob, TimeSpan.FromTicks(1), MisfireInstruction.SimpleTrigger.FireNow, DateTimeOffset.MinValue), false).GetAwaiter().GetResult();
            _ramJobStore8.StoreTrigger(CreateTrigger<MisfireTrigger>(new TriggerKey("7"), _noOpJob, TimeSpan.FromTicks(1), MisfireInstruction.SimpleTrigger.FireNow, DateTimeOffset.MinValue), false).GetAwaiter().GetResult();
            _ramJobStore8.StoreTrigger(CreateTrigger<MisfireTrigger>(new TriggerKey("8"), _noOpJob, TimeSpan.FromTicks(1), MisfireInstruction.IgnoreMisfirePolicy, DateTimeOffset.MinValue), false).GetAwaiter().GetResult();

            // A RAMJobStore with:
            // * a no-op job that disallows concurrent execution
            //   triggers:
            //   - 3 triggers with the IgnoreMisfirePolicy misfire instructions, a repeat interval of TimeSpan.MaxValue and a computed next fire time
            _ramJobStore9 = new RAMJobStore();
            _ramJobStore9.Initialize(new NullJobTypeLoader(), new NoOpSignaler()).GetAwaiter().GetResult();
            _ramJobStore9.StoreJob(_noOpJobNoConcurrent, false).GetAwaiter().GetResult();
            _triggerForRamJobStore9 = CreateTrigger(new TriggerKey("1"), _noOpJobNoConcurrent, TimeSpan.MaxValue, MisfireInstruction.IgnoreMisfirePolicy);
            _ramJobStore9.StoreTrigger(_triggerForRamJobStore9, false).GetAwaiter().GetResult();
            _ramJobStore9.StoreTrigger(CreateTrigger(new TriggerKey("2"), _noOpJobNoConcurrent, TimeSpan.MaxValue, MisfireInstruction.IgnoreMisfirePolicy), false).GetAwaiter().GetResult();
            _ramJobStore9.StoreTrigger(CreateTrigger(new TriggerKey("3"), _noOpJobNoConcurrent, TimeSpan.MaxValue, MisfireInstruction.IgnoreMisfirePolicy), false).GetAwaiter().GetResult();

            // A RAMJobStore with:
            // * a no-op job that disallows concurrent execution
            // * a no-op job that allows concurrent execution
            // * no triggers for either job
            _ramJobStore10 = new RAMJobStore();
            _ramJobStore10.Initialize(new NullJobTypeLoader(), new NoOpSignaler()).GetAwaiter().GetResult();
            _ramJobStore10.StoreJob(_noOpJobNoConcurrent, false).GetAwaiter().GetResult();
            _ramJobStore10.StoreJob(_noOpJob, false).GetAwaiter().GetResult();
        }

        [Benchmark]
        public void StoreTrigger_ReplaceExisting_SingleThreaded()
        {
            _ramJobStore2.StoreTrigger(_trigger1, true);
        }

        [Benchmark(OperationsPerInvoke = 200_000)]
        public void StoreTrigger_ReplaceExisting_MultiThreaded()
        {
            ManualResetEvent start = new ManualResetEvent(false);

            var tasks = Enumerable.Range(0, 20).Select(i =>
            {
                return Task.Run(() =>
                {
                    start.WaitOne();

                    for (var j = 0; j < 10_000; j++)
                    {
                        _ramJobStore2.StoreTrigger(_trigger1, true);
                    }
                });
            }).ToArray();

            start.Set();

            Task.WaitAll(tasks);
        }

        [Benchmark(OperationsPerInvoke = 100_000)]
        public void StoreAndRemoveJob_NoTriggersExistForJob()
        {
            for (var i = 0; i < 100_000; i++)
            {
                _ramJobStore1.StoreJob(_noOpJob, true);
                _ramJobStore1.RemoveJob(_noOpJob.Key);
            }
        }

        [Benchmark(OperationsPerInvoke = 100_000)]
        public void StoreAndRemoveJob_TriggersExistForJob()
        {
            for (var i = 0; i < 100_000; i++)
            {
                _ramJobStore1.StoreJob(_noOpJob, true);
                _ramJobStore1.StoreTrigger(_trigger1, true);
                _ramJobStore1.StoreTrigger(_trigger2, true);
                _ramJobStore1.RemoveJob(_noOpJob.Key);
            }
        }

        [Benchmark(OperationsPerInvoke = 100_000)]
        public void ResumeJobs_EqualsMatch_NoMatchingPausedGroupsAndNoMatchingPausedTriggers()
        {
            var matcher = GroupMatcher<JobKey>.GroupEquals(_noOpJob.Key.Group);

            for (var i = 0; i < 100_000; i++)
            {
                _ramJobStore2.ResumeJobs(matcher);
            }
        }

        [Benchmark(OperationsPerInvoke = 100_000)]
        public void ResumeJobs_StartsWithMatch_NoMatchingPausedGroupsAndNoMatchingPausedTriggers()
        {
            var matcher = GroupMatcher<JobKey>.GroupStartsWith(_noOpJob.Key.Group);

            for (var i = 0; i < 100_000; i++)
            {
                _ramJobStore2.ResumeJobs(matcher);
            }
        }

        [Benchmark(OperationsPerInvoke = 300_000)]
        public void TriggeredJobComplete_ConcurrentExecutionDisallowed_TriggersForJob()
        {
            var jobStore = _ramJobStore9;
            var job = _noOpJobNoConcurrent;

            for (var i = 0; i < 300_000; i++)
            {
                jobStore.TriggeredJobComplete(_triggerForRamJobStore9, _noOpJobNoConcurrent, SchedulerInstruction.NoInstruction);
            }
        }

        [Benchmark(OperationsPerInvoke = 300_000)]
        public void TriggeredJobComplete_ConcurrentExecutionDisallowed_NoTriggersForJob()
        {
            var jobStore = _ramJobStore3;
            var job = _noOpJobNoConcurrent;

            var trigger = CreateTrigger(new TriggerKey("1"), job, TimeSpan.FromSeconds(1), MisfireInstruction.IgnoreMisfirePolicy);

            for (var i = 0; i < 300_000; i++)
            {
                jobStore.TriggeredJobComplete(trigger, job, SchedulerInstruction.NoInstruction);
            }
        }

        [Benchmark(OperationsPerInvoke = 300_000)]
        public void AcquireNextTriggers_NoTimeTriggersAvailable()
        {
            var jobStore = _ramJobStore1;

            for (var i = 0; i < 300_000; i++)
            {
                var triggers = jobStore.AcquireNextTriggers(DateTimeOffset.MaxValue, 1, TimeSpan.MinValue).GetAwaiter().GetResult();

                if (triggers.Count != 0)
                {
                    throw new Exception($"Expected to acquire zero triggers, but was {triggers.Count}.");
                }
            }
        }

        [Benchmark(OperationsPerInvoke = 300_000)]
        public void AcquireNextTriggers_MaxCountIsOneAndAtLeastOneMatchingTimerTriggerAvailable()
        {
            var batchTimeWindow = TimeSpan.FromTicks(100_000);
            var jobStore = _ramJobStore5;

            for (var i = 0; i < 300_000; i++)
            {
                var triggers = jobStore.AcquireNextTriggers(DateTimeOffset.MaxValue, 1, batchTimeWindow).GetAwaiter().GetResult().ToArray();

                foreach (var trigger in triggers)
                {
                    jobStore.ReleaseAcquiredTrigger(trigger).GetAwaiter().GetResult();
                }

                if (triggers.Length != 1)
                {
                    throw new Exception($"Expected to acquire 1 trigger, but was {triggers.Length}.");
                }
            }
        }

        [Benchmark(OperationsPerInvoke = 300_000)]
        public void AcquireNextTriggers_MultipleTimeTriggersForJobThatAllowsConcurrentExecution()
        {
            var batchTimeWindow = TimeSpan.FromTicks(100_000);
            var jobStore = _ramJobStore4;

            for (var i = 0; i < 300_000; i++)
            {
                var triggers = jobStore.AcquireNextTriggers(DateTimeOffset.MaxValue, 15, batchTimeWindow).GetAwaiter().GetResult().ToArray();

                foreach (var trigger in triggers)
                {
                    jobStore.ReleaseAcquiredTrigger(trigger).GetAwaiter().GetResult();
                }

                if (triggers.Length != 10)
                {
                    throw new Exception($"Expected to acquire 10 triggers, but was {triggers.Length}.");
                }
            }
        }

        [Benchmark(OperationsPerInvoke = 300_000)]
        public void AcquireNextTriggers_MultipleTimeTriggersForJobThatDisallowsConcurrentExecution()
        {
            var batchTimeWindow = TimeSpan.FromTicks(100_000);
            var jobStore = _ramJobStore6;

            for (var i = 0; i < 300_000; i++)
            {
                var triggers = jobStore.AcquireNextTriggers(DateTimeOffset.MaxValue, 2, batchTimeWindow).GetAwaiter().GetResult().ToArray();

                foreach (var trigger in triggers)
                {
                    jobStore.ReleaseAcquiredTrigger(trigger).GetAwaiter().GetResult();
                }

                if (triggers.Length != 2)
                {
                    throw new Exception($"Expected to acquire 2 triggers, but was {triggers.Length}.");
                }
            }
        }

        [Benchmark(OperationsPerInvoke = 300_000)]
        public void AcquireNextTriggers_Misfires()
        {
            var batchTimeWindow = TimeSpan.FromTicks(100_000);
            var jobStore = _ramJobStore8;

            for (var i = 0; i < 300_000; i++)
            {
                var triggers = jobStore.AcquireNextTriggers(DateTimeOffset.UtcNow.AddDays(1), 1, batchTimeWindow).GetAwaiter().GetResult().ToArray();

                foreach (var trigger in triggers)
                {
                    jobStore.TriggersFired(new[] { triggers[0] }).GetAwaiter().GetResult();
                }

                if (triggers.Length != 1)
                {
                    throw new Exception($"Expected to acquire 1 triggers, but was {triggers.Length}.");
                }

                if (triggers[0].MisfireInstruction != MisfireInstruction.IgnoreMisfirePolicy)
                {
                    throw new Exception($"Expected acquired triggers to have {MisfireInstruction.IgnoreMisfirePolicy} as misfire instruction, but was {triggers[0].MisfireInstruction} for trigger ${triggers[0].Key}.");
                }
            }
        }

        [Benchmark(OperationsPerInvoke = 300_000)]
        public void AcquireNextTriggers_SingleTimeTriggersForJobThatDisallowsConcurrentExecution()
        {
            var batchTimeWindow = TimeSpan.FromTicks(100_000);
            var jobStore = _ramJobStore7;

            for (var i = 0; i < 300_000; i++)
            {
                var triggers = jobStore.AcquireNextTriggers(DateTimeOffset.MaxValue, 2, batchTimeWindow).GetAwaiter().GetResult().ToArray();
                if (triggers.Length != 2)
                {
                    throw new Exception($"Expected to acquire 2 triggers, but was {triggers.Length}.");
                }

                foreach (var trigger in triggers)
                {
                    jobStore.ReleaseAcquiredTrigger(trigger).GetAwaiter().GetResult();
                }
            }
        }

        private IOperableTrigger CreateTrigger(TriggerKey triggerKey,
                                               IJobDetail job,
                                               TimeSpan repeatInterval,
                                               int misFirePolicy,
                                               DateTimeOffset? nextFireTimeUtc = null)
        {
            return CreateTrigger<SimpleTriggerImpl>(triggerKey, job, repeatInterval, misFirePolicy, nextFireTimeUtc);
        }

        private IOperableTrigger CreateTrigger<T>(TriggerKey triggerKey,
                                                IJobDetail job,
                                                TimeSpan repeatInterval,
                                                int misFirePolicy,
                                                DateTimeOffset? nextFireTimeUtc = null)
            where T: SimpleTriggerImpl, new()
        { 
            var trigger = (IOperableTrigger) new T()
                {
                    Key = triggerKey,
                    JobKey = job.Key,
                    StartTimeUtc = DateTimeOffset.UtcNow,
                    MisfireInstruction = misFirePolicy,
                    RepeatInterval = repeatInterval,
                    RepeatCount = SimpleTriggerImpl.RepeatIndefinitely
                };

            if (nextFireTimeUtc is not null)
            {
                trigger.SetNextFireTimeUtc(nextFireTimeUtc);
            }
            else
            {
                trigger.ComputeFirstFireTimeUtc(null);
            }

            return trigger;
        }

        [DisallowConcurrentExecution]
        private class NoOpJobDisallowConcurrent : IJob
        {
            /// <summary>
            /// Do nothing.
            /// </summary>
            public Task Execute(IJobExecutionContext context)
            {
                return Task.FromResult(true);
            }
        }

        private class NoOpJob : IJob
        {
            /// <summary>
            /// Do nothing.
            /// </summary>
            public Task Execute(IJobExecutionContext context)
            {
                return Task.FromResult(true);
            }
        }

        public class NoOpSignaler : ISchedulerSignaler
        {
            public Task NotifySchedulerListenersError(string message, SchedulerException jpe, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task NotifySchedulerListenersFinalized(ITrigger trigger, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task NotifySchedulerListenersJobDeleted(JobKey jobKey, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task NotifyTriggerListenersMisfired(ITrigger trigger, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public void SignalSchedulingChange(DateTimeOffset? candidateNewNextFireTimeUtc, CancellationToken cancellationToken = default)
            {
            }
        }

        private class NullJobTypeLoader : ITypeLoadHelper
        {
            public void Initialize()
            {
            }

            public Type? LoadType(string? name)
            {
                return null;
            }
        }

        private class MisfireTrigger : SimpleTriggerImpl
        {
            public MisfireTrigger()
            {
            }

            public override void UpdateAfterMisfire(ICalendar? cal)
            {
                base.SetNextFireTimeUtc(base.GetNextFireTimeUtc().GetValueOrDefault().AddSeconds(1));
            }

            public override void Triggered(ICalendar? cal)
            {
                base.SetNextFireTimeUtc(base.GetNextFireTimeUtc().GetValueOrDefault().AddSeconds(2));
            }
        }
    }
}
