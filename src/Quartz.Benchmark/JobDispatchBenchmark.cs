using BenchmarkDotNet.Attributes;

using Quartz.Core;
using Quartz.Impl;
using Quartz.Job;
using Quartz.Spi;

namespace Quartz.Benchmark;

[MemoryDiagnoser]
public class JobDispatchBenchmark
{
    private readonly StdScheduler scheduler;
    private readonly JobRunShell shell;

    public JobDispatchBenchmark()
    {
        scheduler = (StdScheduler) new StdSchedulerFactory().GetScheduler().GetAwaiter().GetResult();
        var job = JobBuilder.Create<NoOpJob>().Build();
        var trigger = (IOperableTrigger) TriggerBuilder.Create()
            .ForJob(job.Key)
            .WithSimpleSchedule()
            .StartNow()
            .Build();

        trigger.FireInstanceId = "fire-instance-id";
        trigger.SetNextFireTimeUtc(DateTimeOffset.UtcNow.AddSeconds(10));
        var bundle = new TriggerFiredBundle(job, trigger, null, false, DateTimeOffset.UtcNow, null, null, null);
        shell = new JobRunShell(scheduler, bundle);
    }

    [Benchmark]
    public async Task Run()
    {
        await shell.Initialize(scheduler.sched);
        await shell.Run();
    }
}