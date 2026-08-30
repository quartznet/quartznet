using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Tests.Unit;

/// <summary>
/// <see cref="SchedulerFactoryExtensions.GetRequiredScheduler" /> is the throw every caller of
/// <see cref="ISchedulerFactory.LookupScheduler" /> was writing for itself.
/// </summary>
[NonParallelizable]
public class SchedulerFactoryExtensionsTest
{
    [Test]
    public async Task ANameTheContainerKnowsIsTheSameSchedulerLookupWouldHaveFound()
    {
        await using ServiceProvider provider = Container("reporting");
        ISchedulerFactory factory = provider.GetRequiredKeyedService<ISchedulerFactory>("reporting");

        IScheduler required = await factory.GetRequiredScheduler("reporting");

        try
        {
            required.Should().BeSameAs(await factory.LookupScheduler("reporting"),
                "the throwing form differs from the lookup only in what it does with absence");
        }
        finally
        {
            await required.Shutdown();
        }
    }

    [Test]
    public async Task ANameTheContainerDoesNotKnowThrowsAnExceptionThatNamesIt()
    {
        await using ServiceProvider provider = Container("reporting");
        ISchedulerFactory factory = provider.GetRequiredKeyedService<ISchedulerFactory>("reporting");

        Func<Task> missing = async () => await factory.GetRequiredScheduler("imports");

        SchedulerNotFoundException thrown = (await missing.Should().ThrowAsync<SchedulerNotFoundException>(
            "a caller that treats absence as a bug should not have to test for null to say so"))
            .Which;

        thrown.SchedulerName.Should().Be("imports",
            "the name is on the exception so a report does not have to parse the message");
        thrown.Should().BeAssignableTo<SchedulerException>(
            "everything Quartz throws for a scheduling failure is catchable as one");
    }

    [Test]
    public async Task LookupStillAnswersNullForTheSameName()
    {
        await using ServiceProvider provider = Container("reporting");
        ISchedulerFactory factory = provider.GetRequiredKeyedService<ISchedulerFactory>("reporting");

        (await factory.LookupScheduler("imports")).Should().BeNull(
            "the shorthand is additive: the nullable form keeps answering the way it did");
    }

    [Test]
    public async Task ABlankNameIsRejectedBeforeTheContainerIsAsked()
    {
        await using ServiceProvider provider = Container("reporting");
        ISchedulerFactory factory = provider.GetRequiredKeyedService<ISchedulerFactory>("reporting");

        Func<Task> blank = async () => await factory.GetRequiredScheduler("   ");

        await blank.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("schedulerName", "a name that is only whitespace is a caller's mistake, not a missing scheduler");
    }

    private static ServiceProvider Container(string schedulerName)
    {
        ServiceCollection services = new();
        services.AddQuartz(schedulerName);
        return services.BuildServiceProvider();
    }
}
