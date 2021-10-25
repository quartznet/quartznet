using BenchmarkDotNet.Attributes;
using Quartz.Impl.Matchers;
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
        private RAMJobStore _ramJobStore;
        private IOperableTrigger _trigger1;
        private IOperableTrigger _trigger2;
        private IJobDetail _job;
        private TriggerBuilder _triggerBuilder;

        public RAMJobStoreBenchmark()
        {
            _job = JobBuilder.Create<NoOpJob>().WithIdentity("Job1", "Group1").Build();

            _triggerBuilder = TriggerBuilder.Create();
            _trigger1 = (IOperableTrigger)_triggerBuilder.ForJob(_job).WithSimpleSchedule().StartNow().Build();
            _trigger2 = (IOperableTrigger)_triggerBuilder.ForJob(_job).WithSimpleSchedule().StartNow().Build();

            _ramJobStore = new RAMJobStore();
            _ramJobStore.StoreJob(_job, false);
        }

        [Benchmark]
        public void StoreTrigger_ReplaceExisting_SingleThreaded()
        {
            _ramJobStore.StoreTrigger(_trigger1, true);
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

                    for (var i = 0; i < 10_000; i++)
                    {
                        _ramJobStore.StoreTrigger(_trigger1, true);
                    }
                });
            }).ToArray();

            start.Set();

            Task.WaitAll(tasks);
        }

        [Benchmark(OperationsPerInvoke = 100_000)]
        public void StoreAndRemoveJob_NoTriggersExistForJob()
        {
            var ramJobStore = new RAMJobStore();

            for (var i = 0; i < 100_000; i++)
            {
                ramJobStore.StoreJob(_job, true);
                ramJobStore.RemoveJob(_job.Key);
            }
        }

        [Benchmark(OperationsPerInvoke = 100_000)]
        public void StoreAndRemoveJob_TriggersExistForJob()
        {
            var ramJobStore = new RAMJobStore();

            for (var i = 0; i < 100_000; i++)
            {
                ramJobStore.StoreJob(_job, true);
                ramJobStore.StoreTrigger(_trigger1, true);
                ramJobStore.StoreTrigger(_trigger2, true);
                ramJobStore.RemoveJob(_job.Key);
            }
        }

        [Benchmark(OperationsPerInvoke = 100_000)]
        public void ResumeJobs_EqualsMatch_NoMatchingPausedGroupsAndNoMatchingPausedTriggers()
        {
            var ramJobStore = new RAMJobStore();
            ramJobStore.StoreJob(_job, true);

            var matcher = GroupMatcher<JobKey>.GroupEquals(_job.Key.Group);

            for (var i = 0; i < 100_000; i++)
            {
                ramJobStore.ResumeJobs(matcher);
            }
        }

        [Benchmark(OperationsPerInvoke = 100_000)]
        public void ResumeJobs_StartsWithMatch_NoMatchingPausedGroupsAndNoMatchingPausedTriggers()
        {
            var ramJobStore = new RAMJobStore();
            ramJobStore.StoreJob(_job, true);

            var matcher = GroupMatcher<JobKey>.GroupStartsWith(_job.Key.Group);

            for (var i = 0; i < 100_000; i++)
            {
                ramJobStore.ResumeJobs(matcher);
            }
        }

        [Benchmark(OperationsPerInvoke = 300_000)]
        public void TriggeredJobComplete_ConcurrentExecutionDisallowed_TriggersForJob()
        {
            var ramJobStore = new RAMJobStore();
            ramJobStore.Initialize(new NullJobTypeLoader(), new NoOpSignaler());

            var job = JobBuilder.Create<NoOpJobDisallowConcurrent>().WithIdentity("Job1", "Group1").Build();
            ramJobStore.StoreJob(job, true);

            var trigger1 = (IOperableTrigger)_triggerBuilder.WithIdentity("1").ForJob(job).WithSimpleSchedule().StartNow().Build();
            ramJobStore.StoreTrigger(trigger1, false);
            var trigger2 = (IOperableTrigger)_triggerBuilder.WithIdentity("2").ForJob(job).WithSimpleSchedule().StartNow().Build();
            ramJobStore.StoreTrigger(trigger2, false);
            var trigger3 = (IOperableTrigger)_triggerBuilder.WithIdentity("3").ForJob(job).WithSimpleSchedule().StartNow().Build();
            ramJobStore.StoreTrigger(trigger3, false);

            for (var i = 0; i < 300_000; i++)
            {
                ramJobStore.TriggeredJobComplete(trigger1, job, SchedulerInstruction.NoInstruction);
            }
        }

        [Benchmark(OperationsPerInvoke = 300_000)]
        public void TriggeredJobComplete_ConcurrentExecutionDisallowed_NoTriggersForJob()
        {
            var ramJobStore = new RAMJobStore();
            ramJobStore.Initialize(new NullJobTypeLoader(), new NoOpSignaler());

            var job = JobBuilder.Create<NoOpJobDisallowConcurrent>().WithIdentity("Job1", "Group1").Build();
            ramJobStore.StoreJob(job, true);

            var trigger = (IOperableTrigger)_triggerBuilder.ForJob(job).WithSimpleSchedule().StartNow().Build();

            for (var i = 0; i < 300_000; i++)
            {
                ramJobStore.TriggeredJobComplete(trigger, job, SchedulerInstruction.NoInstruction);
            }
        }

        [DisallowConcurrentExecution]
        public class NoOpJobDisallowConcurrent : IJob
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
    }
}
