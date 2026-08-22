using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Quartz.Documentation.Samples.Packages;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// Runs the configuration samples the documentation shows.
/// </summary>
/// <remarks>
/// <para>
/// This test used to hold its own transcription of the pages' snippets, which is the arrangement that let
/// a page ship a call that did not exist: the page and the test were two copies of the same code and
/// nothing compared them. The samples now live in <c>src/Quartz.Documentation.Samples</c> and the pages
/// are generated from them, so compiling that project is what keeps the documentation honest.
/// </para>
/// <para>
/// What is left here is the part compilation cannot check — that the registration the DI page shows
/// really does produce the schedule it claims to.
/// </para>
/// </remarks>
public class DocumentedConfigurationTest
{
    [Test]
    public void TheWorkedConfigurationSchedulesEveryTriggerItShows()
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddLogging();

        MicrosoftDiIntegrationSamples.JobsAndTriggers(builder);
        MicrosoftDiIntegrationSamples.TheRestOfTheWorkedConfiguration(builder);

        using ServiceProvider provider = builder.Services.BuildServiceProvider();

        provider.ScheduledTriggers().Select(x => x.Key.Name).Should().BeEquivalentTo(
            [
                "Combined Configuration Trigger",
                "Simple Trigger",
                "Cron Trigger",
                "Spread Cron Trigger",
                "Daily Trigger",
                "slowJobTrigger"
            ],
            "every trigger the worked configuration on the Microsoft DI page shows should be scheduled");
    }

    [Test]
    public void TheScheduleJobSampleRegistersItsJobAndTrigger()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(q => q.ScheduleJob<ExampleJob>(trigger => trigger
            .WithIdentity("example")
            .WithCronSchedule("0 0/5 * * * ?")));

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.ScheduledTriggers().Should().ContainSingle().Which.Key.Name.Should().Be("example");
    }

    public class ExampleJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
