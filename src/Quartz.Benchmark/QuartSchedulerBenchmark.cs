using BenchmarkDotNet.Attributes;
using Quartz.Core;
using Quartz.Impl;
using Quartz.Simpl;
using Quartz.Spi;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Quartz.Benchmark
{
    [MemoryDiagnoser]
    public class QuartSchedulerBenchmark
    {
        private QuartzScheduler _basicQuartzScheduler;
        private StdScheduler _basicScheduler;
        private JobExecutionContextImpl _jobExecutionContext;

        public QuartSchedulerBenchmark()
        {
            _basicQuartzScheduler = CreateQuartzScheduler("basic", "basic", 5);
            _basicScheduler = new StdScheduler(_basicQuartzScheduler);
            _jobExecutionContext = CreateJobExecutionContext(_basicScheduler);
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            _basicQuartzScheduler.Shutdown(true).GetAwaiter().GetResult();
        }

        [Benchmark]
        public void NotifyTriggerListenersFired_SingleThreaded()
        {
            _basicQuartzScheduler.NotifyTriggerListenersFired(_jobExecutionContext).GetAwaiter().GetResult();
        }

        [Benchmark(OperationsPerInvoke = 200_000)]
        public void NotifyTriggerListenersFired_MultiThreaded()
        {
            Execute(_basicQuartzScheduler, 20, 10_000, (scheduler) =>
            {
                _basicQuartzScheduler.NotifyTriggerListenersFired(_jobExecutionContext).GetAwaiter().GetResult();
            });
        }

        [Benchmark]
        public void NotifySchedulerListenersStarted_SingleThreaded()
        {
            _basicQuartzScheduler.NotifySchedulerListenersStarted().GetAwaiter().GetResult();
        }

        [Benchmark(OperationsPerInvoke = 200_000)]
        public void NotifySchedulerListenersStarted_MultiThreaded()
        {
            Execute(_basicQuartzScheduler, 20, 10_000, (scheduler) =>
            {
                scheduler.NotifySchedulerListenersStarted().GetAwaiter().GetResult();
            });
        }

        [Benchmark]
        public void NotifyJobListenersToBeExecuted_SingleThreaded()
        {
            _basicQuartzScheduler.NotifyJobListenersToBeExecuted(_jobExecutionContext).GetAwaiter().GetResult();
        }

        [Benchmark(OperationsPerInvoke = 200_000)]
        public void NotifyJobListenersToBeExecuted_MultiThreaded()
        {
            Execute(_basicQuartzScheduler, 20, 10_000, (scheduler) =>
            {
                scheduler.NotifyJobListenersToBeExecuted(_jobExecutionContext).GetAwaiter().GetResult();
            });
        }

        private static QuartzScheduler CreateQuartzScheduler(string name, string instanceId, int threadCount)
        {
            QuartzSchedulerResources res = new QuartzSchedulerResources
            {
                Name = name,
                InstanceId = instanceId,
                ThreadPool = new DefaultThreadPool { MaxConcurrency = threadCount },
                JobStore = new RAMJobStore(),
                MaxBatchSize = threadCount,
                BatchTimeWindow = TimeSpan.Zero
            };

            return new QuartzScheduler(res, TimeSpan.Zero);
        }

        private JobExecutionContextImpl CreateJobExecutionContext(IScheduler scheduler)
        {
            var job = new Job();
            var jobDetail = CreateJobDetail("A", job.GetType());
            var trigger = (IOperableTrigger)CreateTrigger(TimeSpan.Zero);
            trigger.FireInstanceId = Guid.NewGuid().ToString();

            var triggerFiredBundle = new TriggerFiredBundle(jobDetail, trigger, null, false, DateTimeOffset.Now, null, null, null);

            return new JobExecutionContextImpl(scheduler, triggerFiredBundle, job);
        }

        private static ITrigger CreateTrigger(TimeSpan repeatInterval)
        {
            return TriggerBuilder.Create()
                                 .WithSimpleSchedule(
                                     sb => sb.RepeatForever()
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

                    for (var i = 0; i < iterationsPerThread; i++)
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
    }
}
