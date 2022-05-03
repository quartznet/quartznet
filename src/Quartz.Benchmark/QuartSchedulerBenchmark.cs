using BenchmarkDotNet.Attributes;
using Quartz.Core;
using Quartz.Impl;
using Quartz.Impl.Matchers;
using Quartz.Simpl;
using Quartz.Spi;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Quartz.Benchmark
{
    /// <summary>
    /// | scheduler |              internal             |              global               |
    /// |           |    job    | scheduler |  trigger  |    job    | scheduler |  trigger  |
    /// | --------- | --------- | --------- | --------- | --------- | --------- | --------- |
    /// |     1     |     1     |     0     |     0     |     0     |     0     |     0     |
    /// |     2     |     1     |     1     |     1     |     1     |     0     |     0     |
    /// |     3     |     1     |     0     |     0     |     1     |     1     |     1     |
    /// |     4     |     1     |     1     |     1     |     2     |     1     |     1     |
    /// 
    /// Note:
    /// -----
    /// There's always one internal job listener, which is Quartz.Core.ExecutingJobsManager.
    /// 
    /// </summary>
    [MemoryDiagnoser]
    public class QuartSchedulerBenchmark
    {
        private QuartzScheduler _quartzScheduler1;
        private QuartzScheduler _quartzScheduler2;
        private QuartzScheduler _quartzScheduler3;
        private QuartzScheduler _quartzScheduler4;
        private StdScheduler _basicScheduler;
        private IOperableTrigger _trigger;
        private JobExecutionContextImpl _jobExecutionContext;

        public QuartSchedulerBenchmark()
        {
            _quartzScheduler1 = CreateQuartzScheduler("#1", "#1", 5);

            _quartzScheduler2 = CreateQuartzScheduler("#2", "#2", 5);
            _quartzScheduler2.ListenerManager.AddJobListener(new NoOpListener("GlobalJob1"));
            _quartzScheduler2.AddInternalSchedulerListener(new NoOpListener("InternalScheduler1"));
            _quartzScheduler2.AddInternalTriggerListener(new NoOpListener("InternalTrigger1"));

            _quartzScheduler3 = CreateQuartzScheduler("#3", "#3", 5);
            _quartzScheduler3.ListenerManager.AddJobListener(new NoOpListener("GlobalJob1"), EverythingMatcher<JobKey>.AllJobs());
            _quartzScheduler3.ListenerManager.AddSchedulerListener(new NoOpListener("GlobalScheduler1"));
            _quartzScheduler3.ListenerManager.AddTriggerListener(new NoOpListener("GlobalTrigger1"));

            _quartzScheduler4 = CreateQuartzScheduler("#4", "#4", 5);
            _quartzScheduler4.AddInternalSchedulerListener(new NoOpListener("InternalScheduler1"));
            _quartzScheduler4.AddInternalTriggerListener(new NoOpListener("InternalTrigger1"));
            _quartzScheduler4.ListenerManager.AddJobListener(new NoOpListener("GlobalJob1"));
            _quartzScheduler4.ListenerManager.AddJobListener(new NoOpListener("GlobalJob2"), EverythingMatcher<JobKey>.AllJobs());
            _quartzScheduler4.ListenerManager.AddSchedulerListener(new NoOpListener("GlobalScheduler1"));
            _quartzScheduler4.ListenerManager.AddTriggerListener(new NoOpListener("GlobalTrigger1"));

            _basicScheduler = new StdScheduler(_quartzScheduler1);

            _trigger = (IOperableTrigger)CreateTrigger(TimeSpan.Zero);
            _trigger.FireInstanceId = Guid.NewGuid().ToString();

            _jobExecutionContext = CreateJobExecutionContext(_basicScheduler, _trigger);
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            _quartzScheduler1.Shutdown(true).GetAwaiter().GetResult();
            _quartzScheduler2.Shutdown(true).GetAwaiter().GetResult();
            _quartzScheduler3.Shutdown(true).GetAwaiter().GetResult();
            _quartzScheduler4.Shutdown(true).GetAwaiter().GetResult();
        }

        [Benchmark]
        public void NotifyTriggerListenersFired_QuartScheduler1_SingleThreaded()
        {
            _quartzScheduler1.NotifyTriggerListenersFired(_jobExecutionContext).GetAwaiter().GetResult();
        }

        [Benchmark(OperationsPerInvoke = 200_000)]
        public void NotifyTriggerListenersFired_QuartScheduler1_MultiThreaded()
        {
            Execute(_quartzScheduler1, 20, 10_000, (scheduler) =>
                {
                    scheduler.NotifyTriggerListenersFired(_jobExecutionContext).GetAwaiter().GetResult();
                });
        }

        [Benchmark]
        public void NotifyTriggerListenersFired_QuartScheduler2_SingleThreaded()
        {
            _quartzScheduler2.NotifyTriggerListenersFired(_jobExecutionContext).GetAwaiter().GetResult();
        }

        [Benchmark(OperationsPerInvoke = 200_000)]
        public void NotifyTriggerListenersFired_QuartScheduler2_MultiThreaded()
        {
            Execute(_quartzScheduler2, 20, 10_000, (scheduler) =>
                {
                    scheduler.NotifyTriggerListenersFired(_jobExecutionContext).GetAwaiter().GetResult();
                });
        }

        [Benchmark]
        public void NotifyTriggerListenersFired_QuartScheduler3_SingleThreaded()
        {
            _quartzScheduler3.NotifyTriggerListenersFired(_jobExecutionContext).GetAwaiter().GetResult();
        }

        [Benchmark(OperationsPerInvoke = 200_000)]
        public void NotifyTriggerListenersFired_QuartScheduler3_MultiThreaded()
        {
            Execute(_quartzScheduler3, 20, 10_000, (scheduler) =>
                {
                    scheduler.NotifyTriggerListenersFired(_jobExecutionContext).GetAwaiter().GetResult();
                });
        }

        [Benchmark]
        public void NotifyTriggerListenersFired_QuartScheduler4_SingleThreaded()
        {
            _quartzScheduler4.NotifyTriggerListenersFired(_jobExecutionContext).GetAwaiter().GetResult();
        }

        [Benchmark(OperationsPerInvoke = 200_000)]
        public void NotifyTriggerListenersFired_QuartScheduler4_MultiThreaded()
        {
            Execute(_quartzScheduler4, 20, 10_000, (scheduler) =>
                {
                    scheduler.NotifyTriggerListenersFired(_jobExecutionContext).GetAwaiter().GetResult();
                });
        }

        [Benchmark]
        public void NotifyTriggerListenersMisfired_QuartzScheduler1_SingleThreaded()
        {
            _quartzScheduler1.NotifyTriggerListenersMisfired(_trigger).GetAwaiter().GetResult();
        }

        [Benchmark(OperationsPerInvoke = 200_000)]
        public void NotifyTriggerListenersMisfired_QuartzScheduler1_MultiThreaded()
        {
            Execute(_quartzScheduler1, 20, 10_000, (scheduler) =>
                {
                    scheduler.NotifyTriggerListenersMisfired(_trigger).GetAwaiter().GetResult();
                });
        }

        [Benchmark]
        public void NotifyTriggerListenersMisfired_QuartzScheduler2_SingleThreaded()
        {
            _quartzScheduler2.NotifyTriggerListenersMisfired(_trigger).GetAwaiter().GetResult();
        }

        [Benchmark(OperationsPerInvoke = 200_000)]
        public void NotifyTriggerListenersMisfired_QuartzScheduler2_MultiThreaded()
        {
            Execute(_quartzScheduler2, 20, 10_000, (scheduler) =>
                {
                    scheduler.NotifyTriggerListenersMisfired(_trigger).GetAwaiter().GetResult();
                });
        }

        [Benchmark]
        public void NotifyTriggerListenersMisfired_QuartzScheduler3_SingleThreaded()
        {
            _quartzScheduler3.NotifyTriggerListenersMisfired(_trigger).GetAwaiter().GetResult();
        }

        [Benchmark(OperationsPerInvoke = 200_000)]
        public void NotifyTriggerListenersMisfired_QuartzScheduler3_MultiThreaded()
        {
            Execute(_quartzScheduler3, 20, 10_000, (scheduler) =>
                {
                    scheduler.NotifyTriggerListenersMisfired(_trigger).GetAwaiter().GetResult();
                });
        }

        [Benchmark]
        public void NotifyTriggerListenersMisfired_QuartzScheduler4_SingleThreaded()
        {
            _quartzScheduler4.NotifyTriggerListenersMisfired(_trigger).GetAwaiter().GetResult();
        }

        [Benchmark(OperationsPerInvoke = 200_000)]
        public void NotifyTriggerListenersMisfired_QuartzScheduler4_MultiThreaded()
        {
            Execute(_quartzScheduler4, 20, 10_000, (scheduler) =>
                {
                    scheduler.NotifyTriggerListenersMisfired(_trigger).GetAwaiter().GetResult();
                });
        }

        [Benchmark]
        public void NotifyTriggerListenersComplete_QuartzScheduler1_SingleThreaded()
        {
            _quartzScheduler1.NotifyTriggerListenersComplete(_jobExecutionContext, SchedulerInstruction.NoInstruction)
                             .GetAwaiter()
                             .GetResult();
        }

        [Benchmark(OperationsPerInvoke = 200_000)]
        public void NotifyTriggerListenersComplete_QuartzScheduler1_MultiThreaded()
        {
            Execute(_quartzScheduler1, 20, 10_000, (scheduler) =>
                {
                    scheduler.NotifyTriggerListenersComplete(_jobExecutionContext, SchedulerInstruction.NoInstruction)
                             .GetAwaiter()
                             .GetResult();
                });
        }

        [Benchmark]
        public void NotifyTriggerListenersComplete_QuartzScheduler2_SingleThreaded()
        {
            _quartzScheduler2.NotifyTriggerListenersComplete(_jobExecutionContext, SchedulerInstruction.NoInstruction)
                             .GetAwaiter()
                             .GetResult();
        }

        [Benchmark(OperationsPerInvoke = 200_000)]
        public void NotifyTriggerListenersComplete_QuartzScheduler2_MultiThreaded()
        {
            Execute(_quartzScheduler2, 20, 10_000, (scheduler) =>
                {
                    scheduler.NotifyTriggerListenersComplete(_jobExecutionContext, SchedulerInstruction.NoInstruction)
                             .GetAwaiter()
                             .GetResult();
                });
        }

        [Benchmark]
        public void NotifyTriggerListenersComplete_QuartzScheduler3_SingleThreaded()
        {
            _quartzScheduler3.NotifyTriggerListenersComplete(_jobExecutionContext, SchedulerInstruction.NoInstruction)
                             .GetAwaiter()
                             .GetResult();
        }

        [Benchmark(OperationsPerInvoke = 200_000)]
        public void NotifyTriggerListenersComplete_QuartzScheduler3_MultiThreaded()
        {
            Execute(_quartzScheduler3, 20, 10_000, (scheduler) =>
                {
                    scheduler.NotifyTriggerListenersComplete(_jobExecutionContext, SchedulerInstruction.NoInstruction)
                             .GetAwaiter()
                             .GetResult();
                });
        }

        [Benchmark]
        public void NotifyTriggerListenersComplete_QuartzScheduler4_SingleThreaded()
        {
            _quartzScheduler4.NotifyTriggerListenersComplete(_jobExecutionContext, SchedulerInstruction.NoInstruction)
                             .GetAwaiter()
                             .GetResult();
        }

        [Benchmark(OperationsPerInvoke = 200_000)]
        public void NotifyTriggerListenersComplete_QuartzScheduler4_MultiThreaded()
        {
            Execute(_quartzScheduler4, 20, 10_000, (scheduler) =>
                {
                    scheduler.NotifyTriggerListenersComplete(_jobExecutionContext, SchedulerInstruction.NoInstruction)
                             .GetAwaiter()
                             .GetResult();
                });
        }

        [Benchmark]
        public void NotifySchedulerListenersStarted_QuartzScheduler1_SingleThreaded()
        {
            _quartzScheduler1.NotifySchedulerListenersStarted().GetAwaiter().GetResult();
        }

        [Benchmark(OperationsPerInvoke = 200_000)]
        public void NotifySchedulerListenersStarted_QuartzScheduler1_MultiThreaded()
        {
            Execute(_quartzScheduler1, 20, 10_000, (scheduler) =>
                {
                    scheduler.NotifySchedulerListenersStarted().GetAwaiter().GetResult();
                });
        }

        [Benchmark]
        public void NotifySchedulerListenersStarted_QuartzScheduler2_SingleThreaded()
        {
            _quartzScheduler2.NotifySchedulerListenersStarted().GetAwaiter().GetResult();
        }

        [Benchmark(OperationsPerInvoke = 200_000)]
        public void NotifySchedulerListenersStarted_QuartzScheduler2_MultiThreaded()
        {
            Execute(_quartzScheduler2, 20, 10_000, (scheduler) =>
                {
                    scheduler.NotifySchedulerListenersStarted().GetAwaiter().GetResult();
                });
        }

        [Benchmark]
        public void NotifySchedulerListenersStarted_QuartzScheduler3_SingleThreaded()
        {
            _quartzScheduler3.NotifySchedulerListenersStarted().GetAwaiter().GetResult();
        }

        [Benchmark(OperationsPerInvoke = 200_000)]
        public void NotifySchedulerListenersStarted_QuartzScheduler3_MultiThreaded()
        {
            Execute(_quartzScheduler3, 20, 10_000, (scheduler) =>
                {
                    scheduler.NotifySchedulerListenersStarted().GetAwaiter().GetResult();
                });
        }

        [Benchmark]
        public void NotifySchedulerListenersStarted_QuartzScheduler4_SingleThreaded()
        {
            _quartzScheduler4.NotifySchedulerListenersStarted().GetAwaiter().GetResult();
        }

        [Benchmark(OperationsPerInvoke = 200_000)]
        public void NotifySchedulerListenersStarted_QuartzScheduler4_MultiThreaded()
        {
            Execute(_quartzScheduler4, 20, 10_000, (scheduler) =>
                {
                    scheduler.NotifySchedulerListenersStarted().GetAwaiter().GetResult();
                });
        }

        [Benchmark]
        public void NotifyJobListenersToBeExecuted_QuartScheduler1_SingleThreaded()
        {
            _quartzScheduler1.NotifyJobListenersToBeExecuted(_jobExecutionContext).GetAwaiter().GetResult();
        }

        [Benchmark(OperationsPerInvoke = 200_000)]
        public void NotifyJobListenersToBeExecuted_QuartzScheduler1_MultiThreaded()
        {
            Execute(_quartzScheduler1, 20, 10_000, (scheduler) =>
                {
                    scheduler.NotifyJobListenersToBeExecuted(_jobExecutionContext).GetAwaiter().GetResult();
                });
        }

        [Benchmark]
        public void NotifyJobListenersToBeExecuted_QuartScheduler2_SingleThreaded()
        {
            _quartzScheduler2.NotifyJobListenersToBeExecuted(_jobExecutionContext).GetAwaiter().GetResult();
        }

        [Benchmark(OperationsPerInvoke = 200_000)]
        public void NotifyJobListenersToBeExecuted_QuartzScheduler2_MultiThreaded()
        {
            Execute(_quartzScheduler2, 20, 10_000, (scheduler) =>
                {
                    scheduler.NotifyJobListenersToBeExecuted(_jobExecutionContext).GetAwaiter().GetResult();
                });
        }

        [Benchmark]
        public void NotifyJobListenersToBeExecuted_QuartScheduler3_SingleThreaded()
        {
            _quartzScheduler3.NotifyJobListenersToBeExecuted(_jobExecutionContext).GetAwaiter().GetResult();
        }

        [Benchmark(OperationsPerInvoke = 200_000)]
        public void NotifyJobListenersToBeExecuted_QuartzScheduler3_MultiThreaded()
        {
            Execute(_quartzScheduler3, 20, 10_000, (scheduler) =>
                {
                    scheduler.NotifyJobListenersToBeExecuted(_jobExecutionContext).GetAwaiter().GetResult();
                });
        }

        [Benchmark]
        public void NotifyJobListenersToBeExecuted_QuartScheduler4_SingleThreaded()
        {
            _quartzScheduler4.NotifyJobListenersToBeExecuted(_jobExecutionContext).GetAwaiter().GetResult();
        }

        [Benchmark(OperationsPerInvoke = 200_000)]
        public void NotifyJobListenersToBeExecuted_QuartzScheduler4_MultiThreaded()
        {
            Execute(_quartzScheduler4, 20, 10_000, (scheduler) =>
                {
                    scheduler.NotifyJobListenersToBeExecuted(_jobExecutionContext).GetAwaiter().GetResult();
                });
        }

        private static QuartzScheduler CreateQuartzScheduler(string name, string instanceId, int threadCount)
        {
            var threadPool = new DefaultThreadPool { MaxConcurrency = threadCount };
            threadPool.Initialize();

            QuartzSchedulerResources res = new QuartzSchedulerResources
            {
                Name = name,
                InstanceId = instanceId,
                ThreadPool = threadPool,
                JobStore = new RAMJobStore(),
                IdleWaitTime = TimeSpan.FromSeconds(30),
                MaxBatchSize = threadCount,
                BatchTimeWindow = TimeSpan.Zero
            };

            return new QuartzScheduler(res);
        }

        private JobExecutionContextImpl CreateJobExecutionContext(IScheduler scheduler, IOperableTrigger trigger)
        {
            var job = new Job();
            var jobDetail = CreateJobDetail("A", job.GetType());
            var triggerFiredBundle = new TriggerFiredBundle(jobDetail, trigger, null, false, DateTimeOffset.Now, null, null, null);

            return new JobExecutionContextImpl(scheduler, triggerFiredBundle, job);
        }

        private static ITrigger CreateTrigger(TimeSpan repeatInterval)
        {
            return TriggerBuilder.Create()
                                 .WithSimpleSchedule(sb => sb.RepeatForever()
                                                             .WithInterval(repeatInterval)
                                                             .WithMisfireHandlingInstructionFireNow())
                                 .Build();
        }

        private static IJobDetail CreateJobDetail(string group, Type jobType)
        {
            return JobBuilder.Create(jobType).WithIdentity(Guid.NewGuid().ToString(), group).Build();
        }

        private static void Execute(QuartzScheduler scheduler, int threadCount, int iterationsPerThread, Action<QuartzScheduler> action)
        {
            ManualResetEvent start = new ManualResetEvent(false);

            var tasks = Enumerable.Range(0, threadCount).Select(i =>
            {
                return Task.Run(() =>
                {
                    start.WaitOne();

                    for (var j = 0; j < iterationsPerThread; j++)
                    {
                        action(scheduler);
                    }
                });
            }).ToArray();

            start.Set();

            Task.WaitAll(tasks);
        }

        [DisallowConcurrentExecution]
        public class Job : IJob
        {
            private static readonly ManualResetEvent Done = new ManualResetEvent(false);
            private static int RunCount = 0;
            private static int _operationsPerRun;

            public Task Execute(IJobExecutionContext context)
            {
                if (Interlocked.Increment(ref RunCount) == _operationsPerRun)
                {
                    Done.Set();
                }
                return Task.CompletedTask;
            }

            public static void Initialize(int operationsPerRun)
            {
                _operationsPerRun = operationsPerRun;
            }

            public static void Wait()
            {
                Done.WaitOne();
            }

            public static void Reset()
            {
                Done.Reset();
                RunCount = 0;
            }
        }

        private class NoOpListener : IJobListener, ITriggerListener, ISchedulerListener
        {
            public NoOpListener(string name)
            {
                Name = name;
            }

            public string Name { get; }

            public Task JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task JobUnscheduled(TriggerKey triggerKey, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task SchedulerError(string msg, SchedulerException cause, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task SchedulerInStandbyMode(CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task SchedulerShutdown(CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task SchedulerShuttingdown(CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task SchedulerStarted(CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task SchedulerStarting(CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task SchedulingDataCleared(CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task TriggerComplete(ITrigger trigger, IJobExecutionContext context, SchedulerInstruction triggerInstructionCode, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task TriggerFinalized(ITrigger trigger, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task TriggerFired(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task TriggerMisfired(ITrigger trigger, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task TriggerPaused(TriggerKey triggerKey, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task TriggerResumed(TriggerKey triggerKey, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task TriggersPaused(string? triggerGroup, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task TriggersResumed(string? triggerGroup, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task<bool> VetoJobExecution(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(false);
            }

            public Task JobAdded(IJobDetail jobDetail, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task JobDeleted(JobKey jobKey, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task JobInterrupted(JobKey jobKey, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task JobPaused(JobKey jobKey, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task JobResumed(JobKey jobKey, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task JobScheduled(ITrigger trigger, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task JobsPaused(string jobGroup, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task JobsResumed(string jobGroup, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }
        }
    }
}
