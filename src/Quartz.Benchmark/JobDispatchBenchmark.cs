using Microsoft.Extensions.Logging.Abstractions;
using BenchmarkDotNet.Attributes;

using Quartz.Core;
using Quartz.Impl;
using Quartz.Jobs;
using Quartz.Extensibility;

namespace Quartz.Benchmark;

[MemoryDiagnoser]
public class JobDispatchBenchmark
{
    private readonly StdScheduler scheduler;
    private readonly JobRunShell shell;

    public JobDispatchBenchmark()
    {
        scheduler = (StdScheduler) QuartzSchedulerBuilder.Create().BuildScheduler().GetAwaiter().GetResult();
        var job = JobBuilder.Create<NoOpJob>().Build();
        var trigger = (IOperableTrigger) TriggerBuilder.Create()
            .ForJob(job.Key)
            .WithSimpleSchedule()
            .StartNow()
            .Build();

        trigger.FireInstanceId = "fire-instance-id";
        trigger.NextFireTimeUtc = DateTimeOffset.UtcNow.AddSeconds(10);
        var bundle = new TriggerFiredBundle(job, trigger, null, false, DateTimeOffset.UtcNow, null, null, null);
        shell = new JobRunShell(scheduler, bundle, NullLogger<JobRunShell>.Instance);
    }

    [Benchmark]
    public async Task Run()
    {
        await shell.Initialize(scheduler.scheduler);
        await shell.Run();
    }
}