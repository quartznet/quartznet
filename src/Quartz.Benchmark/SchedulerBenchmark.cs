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
        private const int OperationsPerRun = 500_000;
        private static readonly IJobFactory _jobFactory = new SimpleJobFactory();

        [Benchmark(OperationsPerInvoke = OperationsPerRun)]
        public void DisableConcurrent_15Thread_15Triggers()
        {
            RunDisableConcurrent(OperationsPerRun, 15, 15);
        }

        [Benchmark(OperationsPerInvoke = OperationsPerRun)]
        public void DisableConcurrent_15Thread_30Triggers()
        {
            RunDisableConcurrent(OperationsPerRun, 15, 30);
        }

        [Benchmark(OperationsPerInvoke = OperationsPerRun)]
        public void DisableConcurrent_50Threads_15Triggers()
        {
            RunDisableConcurrent(OperationsPerRun, 50, 15);
        }

        [Benchmark(OperationsPerInvoke = OperationsPerRun)]
        public void DisableConcurrent_50Threads_30Triggers()
        {
            RunDisableConcurrent(OperationsPerRun, 50, 30);
        }

        [Benchmark(OperationsPerInvoke = OperationsPerRun)]
        public void Concurrent_15Thread_15Triggers()
        {
            RunConcurrent(OperationsPerRun, 15, 15);
        }

        [Benchmark(OperationsPerInvoke = OperationsPerRun)]
        public void Concurrent_15Thread_30Triggers()
        {
            RunConcurrent(OperationsPerRun, 15, 30);
        }

        [Benchmark(OperationsPerInvoke = OperationsPerRun)]
        public void Concurrent_50Threads_15Triggers()
        {
            RunConcurrent(OperationsPerRun, 50, 15);
        }

        [Benchmark(OperationsPerInvoke = OperationsPerRun)]
        public void Concurrent_50Threads_30Triggers()
        {
            RunConcurrent(OperationsPerRun, 50, 30);
        }

        /// <summary>
        /// Convenience method run this benchmark without BDN.
        /// </summary>
        /// <param name="operationsPerRun">The number of times the job should be executed.</param>
        /// <param name="threadCount">The maximum number of threads to use to execute the job.</param>
        /// <param name="triggerCount">The number of triggers to create.</param>
        public void RunDisableConcurrent(int operationsPerRun, int threadCount, int triggerCount)
        {
            DisableConcurrentJob.Initialize(operationsPerRun);

            var scheduler = CreateAndConfigureScheduler<DisableConcurrentJob>("A", "1", threadCount, triggerCount);
            scheduler.Start();

            //Stopwatch sw = Stopwatch.StartNew();

            DisableConcurrentJob.Wait();

            //Console.WriteLine(sw.ElapsedMilliseconds);

            scheduler.Shutdown(true).GetAwaiter().GetResult();
            DisableConcurrentJob.Reset();
        }

        /// <summary>
        /// Convenience method run this benchmark without BDN.
        /// </summary>
        /// <param name="operationsPerRun">The number of times the job should be executed.</param>
        /// <param name="threadCount">The maximum number of threads to use to execute the job.</param>
        /// <param name="triggerCount">The number of triggers to create.</param>
        public void RunConcurrent(int operationsPerRun, int threadCount, int triggerCount)
        {
            ConcurrentJob.Initialize(operationsPerRun);

            var scheduler = CreateAndConfigureScheduler<ConcurrentJob>("A", "1", threadCount, triggerCount);
            scheduler.Start();

            ConcurrentJob.Wait();

            scheduler.Shutdown(true).GetAwaiter().GetResult();
            ConcurrentJob.Reset();
        }

        private static IScheduler CreateAndConfigureScheduler<T>(string name, string instanceId, int threadCount, int triggerCount) where T:IJob
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

            for (var i = 0; i < triggerCount; i++)
            {
                var trigger = CreateTrigger(TimeSpan.FromTicks(1000L));
                var job = CreateJobDetail(typeof(SchedulerBenchmark).Name, typeof(T));
                scheduler.ScheduleJob(job, trigger).GetAwaiter().GetResult();
            }

            return scheduler;
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

        [DisallowConcurrentExecution]
        public class DisableConcurrentJob : IJob
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

            public static void Dump()
            {
                Console.WriteLine("[DisableConcurrentJob] Run count: " + RunCount);
            }

            public static void Reset()
            {
                Done.Reset();
                RunCount = 0;
            }
        }

        public class ConcurrentJob : IJob
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

            public static void Dump()
            {
                Console.WriteLine("[ConcurrentJob] Run count: " + RunCount);
            }

            public static void Reset()
            {
                Done.Reset();
                RunCount = 0;
            }
        }
    }
}
