using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// The configuration snippets the documentation shows, compiled.
/// </summary>
/// <remarks>
/// The DI configurator and the concrete trigger builder carry two parallel families of schedule
/// extensions, and a gap in either one makes documented code stop compiling without any test noticing.
/// </remarks>
public class DocumentedConfigurationTest
{
    public class ExampleJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    [Test]
    public void TheDiIntegrationSnippetsCompile()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(q =>
        {
            q.ScheduleJob<ExampleJob>(trigger => trigger
                .WithIdentity("Combined Configuration Trigger")
                .WithDailyTimeIntervalSchedule(x => x.WithInterval(10, IntervalUnit.Second)));

            var jobKey = new JobKey("awesome job", "awesome group");
            q.AddJob<ExampleJob>(jobKey, j => j.WithDescription("my awesome job"));
            q.AddTrigger(t => t.WithIdentity("t2").ForJob(jobKey)
                .WithDailyTimeIntervalSchedule(x => x.WithInterval(10, IntervalUnit.Second)));
        });

        using var provider = services.BuildServiceProvider();
        provider.ScheduledTriggers().Should().HaveCount(2);
    }
}
