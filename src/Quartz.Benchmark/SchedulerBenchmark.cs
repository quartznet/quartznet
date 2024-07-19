using BenchmarkDotNet.Attributes;

using Quartz.Impl;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Benchmark;

[MemoryDiagnoser]
public class SchedulerBenchmark
{
    private static readonly IJobFactory _jobFactory = new SimpleJobFactory();

    private IScheduler? _scheduler;

    [Benchmark(OperationsPerInvoke = 450_000)]
    public void DisableConcurrent_15Threads_15Jobs_1TriggersPerJob()
    {
        RunDisableConcurrent(operationsPerRun: 450_000,
            threadCount: 15,
            jobCount: 15,
            disableConcurrentExecution: true,
            persistJobDataAfterExecution: false,
            triggersPerJob: 1,
            maxBatchSize: 16,
            idleWaitTime: TimeSpan.FromTicks(1L), // avoid using Zero since this is ignored on some versions of Quartz.NET
            repeatInterval: TimeSpan.FromTicks(1L),
            repeatCount: 29_999,
            misfireInstruction: MisfireInstruction.IgnoreMisfirePolicy);
    }

    [Benchmark(OperationsPerInvoke = 150_000)]
    public void DisableConcurrent_15Threads_15Jobs_5TriggersPerJob()
    {
        RunDisableConcurrent(operationsPerRun: 150_000,
            threadCount: 50,
            jobCount: 15,
            disableConcurrentExecution: true,
            persistJobDataAfterExecution: false,
            triggersPerJob: 5,
            maxBatchSize: 16,
            idleWaitTime: TimeSpan.FromTicks(1L), // avoid using Zero since this is ignored on some versions of Quartz.NET
            repeatInterval: TimeSpan.FromTicks(1L),
            repeatCount: 1_999,
            misfireInstruction: MisfireInstruction.IgnoreMisfirePolicy);
    }

    [Benchmark(OperationsPerInvoke = 450_000)]
    public void DisableConcurrent_15Threads_30Jobs_1TriggersPerJob()
    {
        RunDisableConcurrent(operationsPerRun: 450_000,
            threadCount: 15,
            jobCount: 30,
            disableConcurrentExecution: true,
            persistJobDataAfterExecution: false,
            triggersPerJob: 1,
            maxBatchSize: 16,
            idleWaitTime: TimeSpan.FromTicks(1L), // avoid using Zero since this is ignored on some versions of Quartz.NET
            repeatInterval: TimeSpan.FromTicks(1L),
            repeatCount: 14_999,
            misfireInstruction: MisfireInstruction.IgnoreMisfirePolicy);
    }

    [Benchmark(OperationsPerInvoke = 225_000)]
    public void DisableConcurrent_2Threads_15Jobs_2TriggersPerJob()
    {
        RunDisableConcurrent(operationsPerRun: 225_000,
            threadCount: 2,
            jobCount: 15,
            disableConcurrentExecution: true,
            persistJobDataAfterExecution: false,
            triggersPerJob: 2,
            maxBatchSize: 16,
            idleWaitTime: TimeSpan.FromTicks(1L), // avoid using Zero since this is ignored on some versions of Quartz.NET
            repeatInterval: TimeSpan.FromTicks(1L),
            repeatCount: 7_499,
            misfireInstruction: MisfireInstruction.IgnoreMisfirePolicy);
    }

    [Benchmark(OperationsPerInvoke = 450_000)]
    public void DisableConcurrent_50Threads_15Jobs_2TriggersPerJob()
    {
        RunDisableConcurrent(operationsPerRun: 450_000,
            threadCount: 50,
            jobCount: 15,
            disableConcurrentExecution: true,
            persistJobDataAfterExecution: false,
            triggersPerJob: 2,
            maxBatchSize: 16,
            idleWaitTime: TimeSpan.FromTicks(1L), // avoid using Zero since this is ignored on some versions of Quartz.NET
            repeatInterval: TimeSpan.FromTicks(1L),
            repeatCount: 14_999,
            misfireInstruction: MisfireInstruction.IgnoreMisfirePolicy);
    }

    [Benchmark(OperationsPerInvoke = 450_000)]
    public void DisableConcurrent_50Threads_30Jobs_1TriggersPerJob()
    {
        RunDisableConcurrent(operationsPerRun: 450_000,
            threadCount: 50,
            jobCount: 30,
            disableConcurrentExecution: true,
            persistJobDataAfterExecution: false,
            triggersPerJob: 1,
            maxBatchSize: 16,
            idleWaitTime: TimeSpan.FromTicks(1L), // avoid using Zero since this is ignored on some versions of Quartz.NET
            repeatInterval: TimeSpan.FromTicks(1L),
            repeatCount: 14_999,
            misfireInstruction: MisfireInstruction.IgnoreMisfirePolicy);
    }

    [Benchmark(OperationsPerInvoke = 450_000)]
    public void DisableConcurrent_50Threads_30Jobs_3TriggersPerJob()
    {
        RunDisableConcurrent(operationsPerRun: 450_000,
            threadCount: 50,
            jobCount: 30,
            disableConcurrentExecution: true,
            persistJobDataAfterExecution: false,
            triggersPerJob: 3,
            maxBatchSize: 16,
            idleWaitTime: TimeSpan.FromTicks(1L), // avoid using Zero since this is ignored on some versions of Quartz.NET
            repeatInterval: TimeSpan.FromTicks(1000L),
            repeatCount: 4_999,
            misfireInstruction: MisfireInstruction.IgnoreMisfirePolicy);
    }

    [IterationSetup(Target = nameof(DisableConcurrent_30Threads_15Jobs_1TriggersPerJob_RepeatCountZero))]
    public void IterationSetup_DisableConcurrent_30Threads_15Jobs_1TriggersPerJob_RepeatCountZero()
    {
        _scheduler = CreateAndConfigureScheduler<ConcurrentJob>(name: "A",
            instanceId: "1",
            threadCount: 30,
            jobCount: 15,
            disableConcurrentExecution: true,
            persistJobDataAfterExecution: false,
            triggersPerJob: 1,
            maxBatchSize: 16,
            idleWaitTime: TimeSpan.FromMinutes(1),
            repeatInterval: TimeSpan.FromMilliseconds(1),
            repeatCount: 0,
            misfireInstruction: MisfireInstruction.SimpleTrigger.FireNow);
    }

    /// <summary>
    /// Primary goal of this benchmark is to measure allocations.
    /// With this benchmark we should:
    /// 1. Acquire 15 triggers.
    /// 2. Acquire 0 triggers.
    /// 3. Wait for 'idleWaitTime', which should be interrupted by either signal change or shutdown of the scheduler.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 15)]
    public void DisableConcurrent_30Threads_15Jobs_1TriggersPerJob_RepeatCountZero()
    {
        ConcurrentJob.Initialize(15);

        _scheduler!.Start();

        ConcurrentJob.Wait();

        _scheduler.Shutdown(false).GetAwaiter().GetResult();
        ConcurrentJob.Reset();
    }

    [IterationSetup(Target = nameof(DisableConcurrent_30Threads_15Jobs_2TriggersPerJob_RepeatCountZero))]
    public void IterationSetup_DisableConcurrent_30Threads_15Jobs_2TriggersPerJob_RepeatCountZero()
    {
        _scheduler = CreateAndConfigureScheduler<ConcurrentJob>(name: "A",
            instanceId: "1",
            threadCount: 30,
            jobCount: 15,
            disableConcurrentExecution: true,
            persistJobDataAfterExecution: false,
            triggersPerJob: 2,
            maxBatchSize: 16,
            idleWaitTime: TimeSpan.FromSeconds(1),
            repeatInterval: TimeSpan.Zero, // we're not repeating a trigger
            repeatCount: 0, // we only want a given trigger to fire once
            misfireInstruction: MisfireInstruction.SimpleTrigger.FireNow);
    }

    /// <summary>
    /// Primary goal of this benchmark is to measure allocations.
    /// With this benchmark we should:
    /// 1. Acquire 15 triggers.
    /// 2. Acquire 15 triggers.
    /// 3. Wait for 'idleWaitTime', which should be interrupted by either signal change or shutdown of the scheduler.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 30)]
    public void DisableConcurrent_30Threads_15Jobs_2TriggersPerJob_RepeatCountZero()
    {
        ConcurrentJob.Initialize(30);

        _scheduler!.Start();

        ConcurrentJob.Wait();

        _scheduler.Shutdown(false).GetAwaiter().GetResult();
        ConcurrentJob.Reset();
    }

    [Benchmark(OperationsPerInvoke = 450_000)]
    public void Concurrent_15Threads_15Jobs_1TriggersPerJob()
    {
        RunConcurrent(operationsPerRun: 450_000,
            threadCount: 15,
            jobCount: 15,
            disableConcurrentExecution: false,
            persistJobDataAfterExecution: false,
            triggersPerJob: 1,
            maxBatchSize: 16,
            idleWaitTime: TimeSpan.FromTicks(1L), // avoid using Zero since this is ignored on some versions of Quartz.NET
            repeatInterval: TimeSpan.FromTicks(1L),
            repeatCount: 29_999,
            misfireInstruction: MisfireInstruction.IgnoreMisfirePolicy);
    }

    [Benchmark(OperationsPerInvoke = 150_000)]
    public void Concurrent_15Threads_15Jobs_5TriggersPerJob()
    {
        RunConcurrent(operationsPerRun: 150_000,
            threadCount: 15,
            jobCount: 15,
            disableConcurrentExecution: false,
            persistJobDataAfterExecution: false,
            triggersPerJob: 5,
            maxBatchSize: 16,
            idleWaitTime: TimeSpan.FromTicks(1L), // avoid using Zero since this is ignored on some versions of Quartz.NET
            repeatInterval: TimeSpan.FromTicks(1L),
            repeatCount: 1_999,
            misfireInstruction: MisfireInstruction.IgnoreMisfirePolicy);
    }

    [Benchmark(OperationsPerInvoke = 450_000)]
    public void Concurrent_15Threads_30Jobs_1TriggersPerJob()
    {
        RunConcurrent(operationsPerRun: 450_000,
            threadCount: 15,
            jobCount: 30,
            disableConcurrentExecution: false,
            persistJobDataAfterExecution: false,
            triggersPerJob: 1,
            maxBatchSize: 16,
            idleWaitTime: TimeSpan.FromTicks(1L), // avoid using Zero since this is ignored on some versions of Quartz.NET
            repeatInterval: TimeSpan.FromTicks(1L),
            repeatCount: 14_999,
            misfireInstruction: MisfireInstruction.IgnoreMisfirePolicy);
    }

    [Benchmark(OperationsPerInvoke = 450_000)]
    public void Concurrent_50Threads_15Jobs_2TriggersPerJob()
    {
        RunConcurrent(operationsPerRun: 450_000,
            threadCount: 50,
            jobCount: 15,
            disableConcurrentExecution: false,
            persistJobDataAfterExecution: false,
            triggersPerJob: 2,
            maxBatchSize: 16,
            idleWaitTime: TimeSpan.FromTicks(1L), // avoid using Zero since this is ignored on some versions of Quartz.NET
            repeatInterval: TimeSpan.FromTicks(1L),
            repeatCount: 14_999,
            misfireInstruction: MisfireInstruction.IgnoreMisfirePolicy);
    }

    [Benchmark(OperationsPerInvoke = 450_000)]
    public void Concurrent_50Threads_30Jobs_1TriggersPerJob()
    {
        RunConcurrent(operationsPerRun: 450_000,
            threadCount: 50,
            jobCount: 30,
            disableConcurrentExecution: false,
            persistJobDataAfterExecution: false,
            triggersPerJob: 1,
            maxBatchSize: 16,
            idleWaitTime: TimeSpan.FromTicks(1L), // avoid using Zero since this is ignored on some versions of Quartz.NET
            repeatInterval: TimeSpan.FromTicks(1L),
            repeatCount: 14_999,
            misfireInstruction: MisfireInstruction.IgnoreMisfirePolicy);
    }

    [Benchmark(OperationsPerInvoke = 450_000)]
    public void Concurrent_50Threads_30Jobs_3TriggersPerJob()
    {
        RunConcurrent(operationsPerRun: 450_000,
            threadCount: 50,
            jobCount: 30,
            disableConcurrentExecution: false,
            persistJobDataAfterExecution: false,
            triggersPerJob: 3,
            maxBatchSize: 16,
            idleWaitTime: TimeSpan.FromTicks(1L), // avoid using Zero since this is ignored on some versions of Quartz.NET
            repeatInterval: TimeSpan.FromTicks(1L),
            repeatCount: 4_999,
            misfireInstruction: MisfireInstruction.IgnoreMisfirePolicy);
    }

    [Benchmark(OperationsPerInvoke = 225_000)]
    public void Concurrent_2Threads_15Jobs_2TriggersPerJob()
    {
        RunConcurrent(operationsPerRun: 225_000,
            threadCount: 2,
            jobCount: 15,
            disableConcurrentExecution: false,
            persistJobDataAfterExecution: false,
            triggersPerJob: 2,
            maxBatchSize: 16,
            idleWaitTime: TimeSpan.FromTicks(1L), // avoid using Zero since this is ignored on some versions of Quartz.NET
            repeatInterval: TimeSpan.FromTicks(1L),
            repeatCount: 7_499,
            misfireInstruction: MisfireInstruction.IgnoreMisfirePolicy);
    }

    [IterationSetup(Target = nameof(Concurrent_30Threads_15Jobs_1TriggersPerJob_RepeatCountZero))]
    public void IterationSetup_Concurrent_30Threads_15Jobs_1TriggersPerJob_RepeatCountZero()
    {
        _scheduler = CreateAndConfigureScheduler<ConcurrentJob>(name: "A",
            instanceId: "1",
            threadCount: 30,
            jobCount: 15,
            disableConcurrentExecution: false,
            persistJobDataAfterExecution: false,
            triggersPerJob: 1,
            maxBatchSize: 16,
            idleWaitTime: TimeSpan.FromMinutes(1),
            repeatInterval: TimeSpan.FromMilliseconds(1),
            repeatCount: 0,
            misfireInstruction: MisfireInstruction.SimpleTrigger.FireNow);
    }

    /// <summary>
    /// Primary goal of this benchmark is to measure allocations.
    /// With this benchmark we should:
    /// 1. Acquire 15 triggers.
    /// 2. Acquire 0 triggers.
    /// 3. Wait for 'idleWaitTime', which should be interrupted by either signal change or shutdown of the scheduler.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 15)]
    public void Concurrent_30Threads_15Jobs_1TriggersPerJob_RepeatCountZero()
    {
        ConcurrentJob.Initialize(15);

        _scheduler!.Start();

        ConcurrentJob.Wait();

        _scheduler.Shutdown(false).GetAwaiter().GetResult();
        ConcurrentJob.Reset();
    }

    [IterationSetup(Target = nameof(Concurrent_30Threads_15Jobs_2TriggersPerJob_RepeatCountZero))]
    public void IterationSetup_Concurrent_30Threads_15Jobs_2TriggersPerJob_RepeatCountZero()
    {
        _scheduler = CreateAndConfigureScheduler<ConcurrentJob>(name: "A",
            instanceId: "1",
            threadCount: 30,
            jobCount: 15,
            disableConcurrentExecution: false,
            persistJobDataAfterExecution: false,
            triggersPerJob: 2,
            maxBatchSize: 16,
            idleWaitTime: TimeSpan.FromSeconds(1),
            repeatInterval: TimeSpan.Zero, // we're not repeating a trigger
            repeatCount: 0, // we only want a given trigger to fire once
            misfireInstruction: MisfireInstruction.SimpleTrigger.FireNow);
    }

    /// <summary>
    /// Primary goal of this benchmark is to measure allocations.
    /// With this benchmark we should:
    /// 1. Acquire 15 triggers.
    /// 2. Acquire 15 triggers.
    /// 3. Wait for 'idleWaitTime', which should be interrupted by either signal change or shutdown of the scheduler.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 30)]
    public void Concurrent_30Threads_15Jobs_2TriggersPerJob_RepeatCountZero()
    {
        ConcurrentJob.Initialize(30);

        _scheduler!.Start();

        ConcurrentJob.Wait();

        _scheduler.Shutdown(false).GetAwaiter().GetResult();
        ConcurrentJob.Reset();
    }

    /// <summary>
    /// Convenience method run this benchmark without BDN.
    /// </summary>
    /// <param name="operationsPerRun">The number of times the job should be executed.</param>
    /// <param name="threadCount">The maximum number of threads to use to execute the job.</param>
    /// <param name="repeatCount">The number of triggers to create.</param>
    public static void RunDisableConcurrent(int operationsPerRun,
        int threadCount,
        int jobCount,
        bool disableConcurrentExecution,
        bool persistJobDataAfterExecution,
        int triggersPerJob,
        int maxBatchSize,
        TimeSpan idleWaitTime,
        TimeSpan repeatInterval,
        int repeatCount,
        int misfireInstruction)
    {
        DisableConcurrentJob.Initialize(operationsPerRun);

        var scheduler = CreateAndConfigureScheduler<DisableConcurrentJob>("A",
            "1",
            threadCount,
            jobCount,
            disableConcurrentExecution,
            persistJobDataAfterExecution,
            triggersPerJob,
            maxBatchSize,
            idleWaitTime,
            repeatInterval,
            repeatCount,
            misfireInstruction);

        scheduler.Start();

        DisableConcurrentJob.Wait();

        scheduler.Shutdown(true).GetAwaiter().GetResult();

        if (DisableConcurrentJob.RunCount != operationsPerRun)
        {
            throw new Exception($"Expected job to be executed {operationsPerRun} times, but was {DisableConcurrentJob.RunCount}.");
        }

        DisableConcurrentJob.Reset();
    }

    /// <summary>
    /// Convenience method run this benchmark without BDN.
    /// </summary>
    /// <param name="operationsPerRun">The number of times the job should be executed.</param>
    /// <param name="threadCount">The maximum number of threads to use to execute the job.</param>
    /// <param name="jobCount">The number of numbers to create.</param>
    /// <param name="triggersPerJob">The number of triggers to create for each job.</param>
    public static void RunConcurrent(int operationsPerRun,
        int threadCount,
        int jobCount,
        bool disableConcurrentExecution,
        bool persistJobDataAfterExecution,
        int triggersPerJob,
        int maxBatchSize,
        TimeSpan idleWaitTime,
        TimeSpan repeatInterval,
        int repeatCount,
        int misfireInstruction)
    {
        ConcurrentJob.Initialize(operationsPerRun);

        var scheduler = CreateAndConfigureScheduler<ConcurrentJob>("A",
            "1",
            threadCount,
            jobCount,
            disableConcurrentExecution,
            persistJobDataAfterExecution,
            triggersPerJob,
            maxBatchSize,
            idleWaitTime,
            repeatInterval,
            repeatCount,
            misfireInstruction);
        scheduler.Start();

        ConcurrentJob.Wait();

        scheduler.Shutdown(true).GetAwaiter().GetResult();

        if (ConcurrentJob.RunCount != operationsPerRun)
        {
            throw new Exception($"Expected job to be executed {operationsPerRun} times, but was {ConcurrentJob.RunCount}.");
        }

        ConcurrentJob.Reset();
    }

    /// <summary>
    /// Convenience method run this benchmark without BDN.
    /// </summary>
    /// <param name="operationsPerRun">The number of times the job should be executed.</param>
    /// <param name="threadCount">The maximum number of threads to use to execute the job.</param>
    /// <param name="repeatCount">The number of triggers to create.</param>
    public static void RunDelayedConcurrent(int operationsPerRun,
        int threadCount,
        int jobCount,
        bool disableConcurrentExecution,
        bool persistJobDataAfterExecution,
        int triggersPerJob,
        int maxBatchSize,
        TimeSpan idleWaitTime,
        TimeSpan repeatInterval,
        int repeatCount,
        int misfireInstruction)
    {
        DelayedConcurrentJob.Initialize(operationsPerRun);

        var scheduler = CreateAndConfigureScheduler<DelayedConcurrentJob>("A",
            "1",
            threadCount,
            jobCount,
            disableConcurrentExecution,
            persistJobDataAfterExecution,
            triggersPerJob,
            maxBatchSize,
            idleWaitTime,
            repeatInterval,
            repeatCount,
            misfireInstruction);

        scheduler.Start();

        DelayedConcurrentJob.Wait();

        scheduler.Shutdown(true).GetAwaiter().GetResult();

        if (DelayedConcurrentJob.RunCount != operationsPerRun)
        {
            throw new Exception($"Expected job to be executed {operationsPerRun} times, but was {DelayedConcurrentJob.RunCount}.");
        }

        DelayedConcurrentJob.Reset();
    }

    private static IScheduler CreateAndConfigureScheduler<T>(string name,
        string instanceId,
        int threadCount,
        int jobCount,
        bool disableConcurrentExecution,
        bool persistJobDataAfterExecution,
        int triggersPerJob,
        int maxBatchSize,
        TimeSpan idleWaitTime,
        TimeSpan repeatInterval,
        int repeatCount,
        int misfireInstruction) where T : IJob
    {
        RAMJobStore store = new RAMJobStore();

        var threadPool = new DefaultThreadPool { MaxConcurrency = threadCount };

        DirectSchedulerFactory.Instance.CreateScheduler(
            name,
            instanceId,
            threadPool,
            store,
            null,
            idleWaitTime,
            maxBatchSize,
            TimeSpan.Zero).GetAwaiter().GetResult();


        var scheduler = DirectSchedulerFactory.Instance.GetScheduler(name).ConfigureAwait(false).GetAwaiter().GetResult();
        scheduler!.JobFactory = _jobFactory;

        var triggersByJob = new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>>();

        for (var i = 0; i < jobCount; i++)
        {
            var job = CreateJobDetail(typeof(SchedulerBenchmark).Name, typeof(T), disableConcurrentExecution, persistJobDataAfterExecution);

            var triggers = new ITrigger[triggersPerJob];
            for (var j = 0; j < triggersPerJob; j++)
            {
                triggers[j] = CreateTrigger(job, repeatInterval, repeatCount, misfireInstruction);
            }

            triggersByJob.Add(job, triggers);
        }

        scheduler.ScheduleJobs(triggersByJob, false).GetAwaiter().GetResult();

        return scheduler;
    }

    private static ITrigger CreateTrigger(IJobDetail job, TimeSpan repeatInterval, int repeatCount, int misfireInstruction)
    {
        return TriggerBuilder.Create()
            .ForJob(job)
            .WithSimpleSchedule(
                sb => sb.WithRepeatCount(repeatCount)
                    .WithInterval(repeatInterval)
                    .WithMisfireHandlingInstruction(misfireInstruction))
            .Build();
    }

    private static IJobDetail CreateJobDetail(string group, Type jobType, bool disableConcurrentExecution, bool persistJobDataAfterExecution)
    {
        return JobBuilder.Create(jobType)
            .WithIdentity(Guid.NewGuid().ToString(), group)
            .DisallowConcurrentExecution(disableConcurrentExecution)
            .PersistJobDataAfterExecution(persistJobDataAfterExecution)
            .Build();
    }

    [DisallowConcurrentExecution]
    public class DisableConcurrentJob : IJob
    {
        private static readonly ManualResetEvent Done = new(false);
        private static int _runCount;
        private static int _operationsPerRun;

        public static int RunCount => _runCount;

        public ValueTask Execute(IJobExecutionContext context)
        {
            if (Interlocked.Increment(ref _runCount) == _operationsPerRun)
            {
                Done.Set();
            }
            return default;
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
            Console.WriteLine("[DisableConcurrentJob] Run count: " + _runCount);
        }

        public static void Reset()
        {
            Done.Reset();
            _runCount = 0;
        }
    }

    public class ConcurrentJob : IJob
    {
        private static readonly ManualResetEvent Done = new(false);
        private static int _runCount;
        private static int _operationsPerRun;

        public static int RunCount => _runCount;

        public ValueTask Execute(IJobExecutionContext context)
        {
            if (Interlocked.Increment(ref _runCount) == _operationsPerRun)
            {
                Done.Set();
            }
            return default;
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
            Console.WriteLine("[ConcurrentJob] Run count: " + _runCount);
        }

        public static void Reset()
        {
            Done.Reset();
            _runCount = 0;
        }
    }

    public class DelayedConcurrentJob : IJob
    {
        private static readonly ManualResetEvent Done = new(false);
        private static int _runCount;
        private static int _operationsPerRun;
        private static readonly TimeSpan _delay = TimeSpan.FromMilliseconds(500);

        public static int RunCount => _runCount;

        public async ValueTask Execute(IJobExecutionContext context)
        {
            int runs = Interlocked.Increment(ref _runCount);

            if (runs < _operationsPerRun)
            {
                await Task.Delay(_delay).ConfigureAwait(false);
            }
            else if (runs == _operationsPerRun)
            {
                Done.Set();
            }
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
            Console.WriteLine("[DelayedConcurrentJob] Run count: " + _runCount);
        }

        public static void Reset()
        {
            Done.Reset();
            _runCount = 0;
        }
    }
}