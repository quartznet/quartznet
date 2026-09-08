#nullable enable

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// What <c>AddQuartz(name, …)</c> records about itself, which is what a restart replays.
/// </summary>
/// <remarks>
/// A registration is descriptors in a collection that is closed once the container is built, and the
/// instances behind them cannot be initialised twice — so building a scheduler a second time means
/// running the registration again rather than reviving anything. That is only possible if the
/// registration was written down, and this is where the boundary of what was written down is drawn: what
/// <c>AddQuartz</c> itself was handed, and nothing that was written beside it.
/// </remarks>
public sealed class SchedulerBlueprintTest
{
    [Test]
    public void ANamedRegistrationRecordsWhatItWasTold()
    {
        ServiceCollection services = new();
        Action<IQuartzBuilder> configure = _ => { };

        services.AddQuartz("acme", new Dictionary<string, string?> { ["quartz.threadPool.maxConcurrency"] = "7" }, configure);

        SchedulerBlueprint blueprint = Blueprint(services, "acme")!;

        blueprint.Name.Should().Be("acme", "the recipe is found by the name it was registered under");
        blueprint.Configure.Should().BeSameAs(configure,
            "a delegate that has been run and thrown away can never be run again, which is the whole reason a "
            + "recipe is recorded rather than reconstructed");
        blueprint.Properties["quartz.threadPool.maxConcurrency"].Should().Be("7");
        blueprint.Configuration.Should().BeNull("this registration was given a property bag, not a section");
    }

    [Test]
    public void ARegistrationWithNoRecipeAtAllStillRecordsOne()
    {
        ServiceCollection services = new();

        services.AddQuartz("acme");

        SchedulerBlueprint? blueprint = Blueprint(services, "acme");

        blueprint.Should().NotBeNull(
            "a scheduler registered with nothing but a name is still restartable - the recipe is empty, not absent");
        blueprint!.Configure.Should().BeNull();
        blueprint.Properties.Count.Should().Be(0);
    }

    /// <summary>
    /// The recipe is looked up the way the repository indexes names, so a restart asked for under a
    /// different spelling finds it.
    /// </summary>
    [Test]
    public void ARecipeIsFoundWhateverTheCaseOfTheNameAskedFor()
    {
        ServiceCollection services = new();

        services.AddQuartz("Acme", _ => { });

        Blueprint(services, "ACME").Should().NotBeNull();
        Blueprint(services, "acme")!.Name.Should().Be("Acme", "the recipe carries the spelling the registration used");
    }

    /// <summary>
    /// A registration made from a root section records the section it actually read, not the one it was
    /// handed.
    /// </summary>
    /// <remarks>
    /// <c>AddQuartz(name, configuration, …)</c> takes either the scheduler's own section or a root
    /// section holding <c>Schedulers:{name}</c>, and resolves between them. Recording the caller's would
    /// leave that resolution to be done again later against a configuration that is free to have changed
    /// in between.
    /// </remarks>
    [Test]
    public void TheConfigurationOverloadRecordsTheSectionItRead()
    {
        IConfiguration root = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Schedulers:acme:quartz.threadPool.maxConcurrency"] = "3"
            })
            .Build();

        ServiceCollection services = new();
        services.AddQuartz("acme", root);

        SchedulerBlueprint blueprint = Blueprint(services, "acme")!;

        blueprint.Configuration.Should().NotBeNull();
        blueprint.Configuration!["quartz.threadPool.maxConcurrency"].Should().Be("3",
            "the recorded section is the resolved one, so replaying it reads the settings directly rather than "
            + "looking for a Schedulers:acme child underneath them");
    }

    /// <summary>
    /// The default scheduler has no recipe, and that is not an oversight.
    /// </summary>
    /// <remarks>
    /// Its parts are the container's <em>unkeyed</em> registrations, which nothing can tell apart from
    /// the application's own — so there is no set of descriptors to replay into a container of its own.
    /// <c>ISchedulerRuntime.Restart</c> says so rather than guessing.
    /// </remarks>
    [Test]
    public void TheDefaultSchedulerRecordsNoRecipe()
    {
        ServiceCollection services = new();

        services.AddQuartz(q => q.ConfigureScheduler(o => o.InstanceName = "TheDefaultOne"));

        Blueprint(services, "TheDefaultOne").Should().BeNull();
        SchedulerNameRegistry.For(services).HasDefaultScheduler.Should().BeTrue(
            "the registration is recorded as existing; it is the recipe that cannot be");
    }

    /// <summary>
    /// Configuration written <em>beside</em> the registration is not part of it.
    /// </summary>
    /// <remarks>
    /// This is the limit of what a restart can promise, and it is drawn where the call is: nothing in a
    /// service descriptor says which scheduler an application wrote it for, so the alternative to this
    /// boundary is guessing. Move the setting inside the <c>AddQuartz(name, …)</c> callback and it is
    /// part of the recipe.
    /// </remarks>
    [Test]
    public void OptionsConfiguredBesideTheRegistrationAreNotPartOfIt()
    {
        ServiceCollection services = new();

        services.AddQuartz("acme", q => q.ConfigureScheduler(o => o.InstanceId = "from-the-recipe"));
        services.Configure<QuartzSchedulerOptions>("acme", o => o.InstanceId = "from-beside-it");
        services.AddKeyedSingleton<ISchedulerListener, CountingListener>("acme");

        SchedulerBlueprint blueprint = Blueprint(services, "acme")!;

        ServiceCollection replayed = new();
        blueprint.Configure!(new QuartzBuilder(replayed, "acme"));

        ServiceProvider provider = replayed.AddOptions().BuildServiceProvider();
        provider.GetRequiredService<IOptionsMonitor<QuartzSchedulerOptions>>().Get("acme").InstanceId
            .Should().Be("from-the-recipe",
                "the recipe is what AddQuartz was handed; a Configure written beside the call belongs to the "
                + "application's container and is replayed by nobody");

        provider.Dispose();
    }

    private static SchedulerBlueprint? Blueprint(IServiceCollection services, string name)
    {
        return SchedulerNameRegistry.For(services).Blueprint(name);
    }

    private sealed class CountingListener : ISchedulerListener;
}
