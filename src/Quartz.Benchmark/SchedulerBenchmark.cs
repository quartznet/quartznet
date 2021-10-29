using BenchmarkDotNet.Attributes;
using Quartz.Impl;
using Quartz.Simpl;
using Quartz.Spi;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Quartz.Benchmark
{
    /// <summary>
    /// This is a benchmark for the following scenario:
    /// <list type="bullet">
    /// <item>
    /// <description>Create a single scheduler with a RAMJobStore, a maximum batch size of 15 and a default threadpool with a maximum concurrency of 15.</description>
    /// <description>Create 15 jos that execute the same job type.</description>
    /// <description>Each job has a corresponding trigger with an interval of 0.1 milliseconds.</description>
    /// <description>Each job has a corresponding trigger with an interval of 0.1 milliseconds.</description>
    /// <description>Upon each excution, a job increments a counter that is shared by all job instancces.</description>
    /// <description>When that counter reaches 200,000 a wait handle is set..</description>
    /// </item>
    /// </list>
    /// A given iteration of the benchmark is considerd done when the wait handle is set, and the scheduler
    /// has shut down.
    /// </summary>
    [MemoryDiagnoser]
    public class SchedulerBenchmark
    {
        private const int OperationsPerRun = 200_000;
        private static readonly IJobFactory _jobFactory = new SimpleJobFactory();

        [Benchmark(OperationsPerInvoke = OperationsPerRun)]
        public void Run()
        {
            Run(OperationsPerRun);
        }

        /// <summary>
        /// Convenience method run this benchmark without BDN.
        /// </summary>
        /// <param name="operationsPerRun">The number of times the job should be executed.</param>
        public void Run(int operationsPerRun)
        {
            Job.Initialize(operationsPerRun);

            var scheduler = CreateAndConfigureScheduler("A", "1", 15);
            scheduler.Start();

            Job.Wait();

            scheduler.Shutdown(true).GetAwaiter().GetResult();
            Job.Reset();
        }

        private static IScheduler CreateAndConfigureScheduler(string name, string instanceId, int threadCount)
        {
            RAMJobStore store = new RAMJobStore();

            var threadPool = new DefaultThreadPool { MaxConcurrency = threadCount };

            DirectSchedulerFactory.Instance.CreateScheduler(name,
                                                            instanceId,
                                                            threadPool,
                                                            store,
                                                            null,
                                                            TimeSpan.Zero,
                                                            threadCount,
                                                            TimeSpan.Zero,
                                                            null);


            var scheduler = DirectSchedulerFactory.Instance.GetScheduler(name).ConfigureAwait(false).GetAwaiter().GetResult();
            scheduler!.JobFactory = _jobFactory;

            for (var i = 0; i < threadCount; i++)
            {
                var trigger = Create(TimeSpan.FromMilliseconds(0.1d));
                var job = Create(typeof(SchedulerBenchmark).Name, typeof(Job));
                scheduler.ScheduleJob(job, trigger).GetAwaiter().GetResult();
            }

            return scheduler;
        }

        private static ITrigger Create(TimeSpan repeatInterval)
        {
            return TriggerBuilder.Create()
                                 .WithSimpleSchedule(
                                     sb => sb.RepeatForever()
                                             .WithInterval(repeatInterval)
                                             .WithMisfireHandlingInstructionFireNow())
                                 .Build();
        }

        private static IJobDetail Create(string group, Type jobType)
        {
            return JobBuilder.Create(jobType).WithIdentity(Guid.NewGuid().ToString(), group).Build();
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
