#nullable enable

using Microsoft.Extensions.Hosting;

namespace Quartz.Tests.Unit.Aspire;

/// <summary>
/// What the store does about its schema when an Aspire connection wired it, and who gets to say so.
/// </summary>
/// <remarks>
/// The unset default is the interesting one: it is the only setting on
/// <see cref="QuartzAspireSettings"/> whose answer depends on something outside the settings, so both
/// branches of it are pinned here rather than one branch and a comment.
/// </remarks>
public class AspireSchemaProvisioningTest
{
    [Test]
    public void DevelopmentCreatesWhateverTheSchemaIsMissing()
    {
        Provisioning(Environments.Development).Should().Be(SchemaProvisioning.CreateIfMissing,
            "the AppHost's database container comes up empty, so a scheduler that refuses to start "
            + "until somebody applies a script refuses on every first run");
    }

    [Test]
    public void EveryOtherEnvironmentValidatesAndCreatesNothing()
    {
        Provisioning(Environments.Production).Should().Be(SchemaProvisioning.Validate,
            "creating tables needs DDL permission that a production account is usually right not to "
            + "have, so the default there is the one that only reads");

        Provisioning(Environments.Staging).Should().Be(SchemaProvisioning.Validate,
            "Development is the environment this is safe in, not merely the one it is most useful in, "
            + "so everything that is not Development gets the careful answer");
    }

    [Test]
    public void ProvisionSchemaTrueCreatesInEveryEnvironment()
    {
        Provisioning(Environments.Production, settings => settings.ProvisionSchema = true)
            .Should().Be(SchemaProvisioning.CreateIfMissing,
                "an application that says it outright has said it about this deployment, whatever the "
                + "environment name happens to be");
    }

    [Test]
    public void ProvisionSchemaFalseCreatesInNone()
    {
        Provisioning(Environments.Development, settings => settings.ProvisionSchema = false)
            .Should().Be(SchemaProvisioning.Validate,
                "a deployment whose schema is applied by something else says so once, and the "
                + "development database is the same schema as the production one");
    }

    /// <summary>
    /// An <c>AdoJobStoreOptions.SchemaProvisioning</c> the application set is a decision about this
    /// store, and this call fills gaps rather than overruling decisions.
    /// </summary>
    /// <remarks>
    /// Options are last-wins and <c>ConfigureAllQuartzSchedulers</c> is applied after the scheduler's
    /// own callback, so what this call contributes would win by position if it were written as an
    /// assignment. Reading the value first is what keeps the application's word — the same shape that
    /// leaves an explicit <c>InstanceId</c> alone.
    /// </remarks>
    [Test]
    public void AStoreTaughtToLeaveItsSchemaAloneIsLeftAlone()
    {
        using IHost host = Host(Environments.Development, builder =>
        {
            builder.AddQuartzPersistentStore("quartz");
            builder.AddQuartz(q => q.UsePersistentStore(store =>
                store.ConfigureStore(options => options.SchemaProvisioning = SchemaProvisioning.None)));
        });

        AspireApplication.StoreOf(host.Services).SchemaProvisioning.Should().Be(SchemaProvisioning.None,
            "a store told to assume its schema is there has been told, and an environment name is not "
            + "an argument against it");
    }

    [Test]
    public void AStoreTaughtToCreateItsSchemaKeepsThatOutsideDevelopment()
    {
        using IHost host = Host(Environments.Production, builder =>
        {
            builder.AddQuartzPersistentStore("quartz", settings => settings.ProvisionSchema = false);
            builder.AddQuartz(q => q.UsePersistentStore(store => store.ProvisionSchema()));
        });

        AspireApplication.StoreOf(host.Services).SchemaProvisioning.Should().Be(SchemaProvisioning.CreateIfMissing,
            "ProvisionSchema = false is this call declining to decide, not an instruction to undo what "
            + "the application decided for itself");
    }

    [Test]
    public void ConfigurationSaysItAsWellAsCodeDoes()
    {
        HostApplicationBuilder builder = AspireApplication.WorkerIn(
            Environments.Development,
            ("Quartz:JobStore:SchemaProvisioning", "None"));

        builder.AddQuartzPersistentStore("quartz");
        builder.AddQuartz();

        using IHost host = builder.Build();

        AspireApplication.StoreOf(host.Services).SchemaProvisioning.Should().Be(SchemaProvisioning.None,
            "this runs from ConfigureAllQuartzSchedulers, which AddQuartz applies after the "
            + "configuration binding, so Quartz:JobStore is read as something the application said");
    }

    [Test]
    public void TheSettingIsBoundFromTheSharedSectionAndTheConnectionsOwn()
    {
        HostApplicationBuilder shared = AspireApplication.WorkerIn(
            Environments.Production,
            ("Aspire:Quartz:ProvisionSchema", "true"));

        shared.AddQuartzPersistentStore("quartz");
        shared.AddQuartz();

        using IHost sharedHost = shared.Build();

        AspireApplication.StoreOf(sharedHost.Services).SchemaProvisioning.Should().Be(SchemaProvisioning.CreateIfMissing,
            "every setting is bound from Aspire:Quartz, and one that is only reachable from code would "
            + "be one an appsettings.json cannot say");

        HostApplicationBuilder own = AspireApplication.WorkerIn(
            Environments.Development,
            ("Aspire:Quartz:ProvisionSchema", "true"),
            ("Aspire:Quartz:quartz:ProvisionSchema", "false"));

        own.AddQuartzPersistentStore("quartz");
        own.AddQuartz();

        using IHost ownHost = own.Build();

        AspireApplication.StoreOf(ownHost.Services).SchemaProvisioning.Should().Be(SchemaProvisioning.Validate,
            "the connection's own section is bound over the shared one, so it wins where the two "
            + "disagree - which is what an application with one provisioned database and one it may "
            + "not touch writes");
    }

    private static SchemaProvisioning Provisioning(
        string environmentName,
        Action<QuartzAspireSettings>? configureSettings = null)
    {
        using IHost host = Host(environmentName, builder =>
        {
            builder.AddQuartzPersistentStore("quartz", configureSettings);
            builder.AddQuartz();
        });

        return AspireApplication.StoreOf(host.Services).SchemaProvisioning;
    }

    private static IHost Host(string environmentName, Action<HostApplicationBuilder> configure)
    {
        HostApplicationBuilder builder = AspireApplication.WorkerIn(environmentName);
        configure(builder);
        return builder.Build();
    }
}
