using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// Typed job data through the DI configuration DSL, all the way to a job that ran with it.
/// </summary>
[NonParallelizable]
public sealed class TypedJobDataConfigurationTest
{
    private static readonly TaskCompletionSource<SampleJob> executed = new();

    public enum RunMode
    {
        Slow,
        Fast
    }

    public sealed class SampleJob : IJob
    {
        public string Name { get; set; } = "";

        public int RetryCount { get; set; }

        public RunMode Mode { get; set; }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            executed.TrySetResult(this);
            return default;
        }
    }

    [Test]
    public void AddJob_BindsTypedDataToTheJobDetail()
    {
        var services = NewServices();
        services.AddQuartz(q => q.AddJob<SampleJob>(j => j
            .WithIdentity("typed")
            .StoreDurably()
            .UsingJobData(s => s.Name, "hello")
            .UsingJobData(s => s.RetryCount, 3)
            .UsingJobData(s => s.Mode, RunMode.Fast)));

        using var provider = services.BuildServiceProvider();

        var job = provider.ScheduledJobs().Should().ContainSingle().Subject;
        job.JobDataMap["Name"].Should().Be("hello");
        job.JobDataMap["RetryCount"].Should().Be(3);
        job.JobDataMap["Mode"].Should().Be("Fast");
    }

    [Test]
    public void ScheduleJob_BindsTypedDataToBothTheJobAndTheTrigger()
    {
        var services = NewServices();
        services.AddQuartz(q => q.ScheduleJob<SampleJob>(
            trigger => trigger
                .WithIdentity("typedTrigger")
                .StartNow()
                .UsingJobData(s => s.Name, "per-trigger"),
            job => job
                .WithIdentity("typedJob")
                .UsingJobData(s => s.Name, "per-job")
                .UsingJobData(s => s.RetryCount, 3)));

        using var provider = services.BuildServiceProvider();

        provider.ScheduledJobs().Should().ContainSingle().Subject
            .JobDataMap["Name"].Should().Be("per-job");
        provider.ScheduledTriggers().Should().ContainSingle().Subject
            .JobDataMap["Name"].Should().Be("per-trigger");
    }

    [Test]
    public void AddTrigger_ForAKnownJobType_BindsTypedData()
    {
        var jobKey = new JobKey("shared");

        var services = NewServices();
        services.AddQuartz(q =>
        {
            q.AddJob<SampleJob>(jobKey, j => j.StoreDurably());
            q.AddTrigger<SampleJob>(t => t
                .WithIdentity("sharedTrigger")
                .ForJob(jobKey)
                .StartNow()
                .UsingJobData(s => s.Name, "per-trigger"));
        });

        using var provider = services.BuildServiceProvider();

        provider.ScheduledTriggers().Should().ContainSingle().Subject
            .JobDataMap["Name"].Should().Be("per-trigger");
    }

    [Test]
    public void AddTrigger_WithoutAJobType_StillTakesStringKeys()
    {
        var jobKey = new JobKey("shared");

        var services = NewServices();
        services.AddQuartz(q =>
        {
            q.AddJob<SampleJob>(jobKey, j => j.StoreDurably());
            q.AddTrigger(t => t
                .WithIdentity("sharedTrigger")
                .ForJob(jobKey)
                .StartNow()
                .UsingJobData("Name", "per-trigger"));
        });

        using var provider = services.BuildServiceProvider();

        provider.ScheduledTriggers().Should().ContainSingle().Subject
            .JobDataMap["Name"].Should().Be("per-trigger");
    }

    [Test]
    public async Task TypedJobDataReachesTheRunningJob()
    {
        var services = NewServices();
        services.AddQuartz(q => q.ScheduleJob<SampleJob>(
            trigger => trigger
                .WithIdentity("bindingTrigger")
                .StartNow()
                // Trigger data wins over job data in the map the job is given, which is the whole point of
                // being able to set the same property from both.
                .UsingJobData(s => s.Mode, RunMode.Fast),
            job => job
                .WithIdentity("bindingJob")
                .UsingJobData(s => s.Name, "hello")
                .UsingJobData(s => s.RetryCount, 3)
                .UsingJobData(s => s.Mode, RunMode.Slow)));

        await using var provider = services.BuildServiceProvider();
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        try
        {
            await scheduler.Start();

            var completed = await Task.WhenAny(executed.Task, Task.Delay(TimeSpan.FromSeconds(20)));
            completed.Should().BeSameAs(executed.Task, "the scheduled job should have fired");

            var job = await executed.Task;
            job.Name.Should().Be("hello");
            job.RetryCount.Should().Be(3);
            job.Mode.Should().Be(RunMode.Fast, "trigger data overrides job data");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    private static ServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        return services;
    }
}
