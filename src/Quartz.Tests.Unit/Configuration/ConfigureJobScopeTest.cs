#nullable enable

using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// The job scope can be prepared without writing a job factory to do it.
/// </summary>
public sealed class ConfigureJobScopeTest
{
    private static readonly AsyncLocal<string?> ambient = new();

    [Test]
    public async Task ConfigureJobScope_RunsBeforeTheJobIsBuiltAndItsValuesSurviveIntoExecute()
    {
        var seen = new List<string>();

        var services = new ServiceCollection();
        services.AddQuartz(q =>
        {
            q.ConfigureJobScope((_, bundle, scheduler) =>
            {
                seen.Add($"scope:{bundle.JobDetail.Key.Name}:{scheduler.SchedulerName}");
                ambient.Value = bundle.JobDetail.Key.Name;
            });

            // Two callbacks combine rather than replace, so a library and an application can each have
            // their say.
            q.ConfigureJobScope((_, _, _) => seen.Add("second"));

            q.ScheduleJob<AmbientReadingJob>(
                trigger => trigger.WithIdentity("scope-trigger").StartNow(),
                job => job.WithIdentity("scope-job"));
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        IScheduler scheduler = provider.GetRequiredService<IScheduler>();
        try
        {
            await scheduler.Start();

            Task finished = await Task.WhenAny(AmbientReadingJob.Executed, Task.Delay(TimeSpan.FromSeconds(30)));
            finished.Should().BeSameAs(AmbientReadingJob.Executed, "the scheduled job should have run");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        seen.Should().BeEquivalentTo(["scope:scope-job:QuartzScheduler", "second"], options => options.WithStrictOrdering());

        (await AmbientReadingJob.Executed).Should().Be("scope-job",
            "the hook is synchronous precisely so that an AsyncLocal set in it survives into Execute");
    }

    public sealed class AmbientReadingJob : IJob
    {
        private static readonly TaskCompletionSource<string?> executed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static Task<string?> Executed => executed.Task;

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            executed.TrySetResult(ambient.Value);
            return default;
        }
    }
}
