#nullable enable

using Microsoft.Extensions.Hosting;

namespace Quartz.Tests.Unit.Aspire;

/// <summary>
/// Where <see cref="QuartzAspireSettings"/> comes from, and in what order.
/// </summary>
/// <remarks>
/// Four sources, each more specific than the one before it: the <c>Aspire:Quartz</c> section shared by
/// every connection, the <c>Aspire:Quartz:&lt;connection name&gt;</c> section for one of them, the
/// connection string the AppHost injected, and the caller's own callback. That ladder is the shape every
/// first-party Aspire client integration has, and an application moving between them should not have to
/// learn a second one.
/// </remarks>
public class QuartzAspireSettingsBindingTest
{
    [Test]
    public void TheSharedSectionConfiguresEveryConnection()
    {
        HostApplicationBuilder builder = AspireApplication.Worker(
            ("ConnectionStrings:quartz", AspireApplication.Postgres),
            ("Aspire:Quartz:TablePrefix", "SHARED_"),
            ("Aspire:Quartz:Clustered", "true"));

        QuartzAspireSettings settings = Capture(builder, "quartz");

        settings.TablePrefix.Should().Be("SHARED_");
        settings.Clustered.Should().BeTrue(
            "Aspire:Quartz is what an application says once about all of its Quartz connections");
    }

    [Test]
    public void TheConnectionsOwnSectionOverridesTheSharedOne()
    {
        HostApplicationBuilder builder = AspireApplication.Worker(
            ("ConnectionStrings:quartz", AspireApplication.Postgres),
            ("Aspire:Quartz:TablePrefix", "SHARED_"),
            ("Aspire:Quartz:Clustered", "true"),
            ("Aspire:Quartz:quartz:TablePrefix", "OWN_"));

        QuartzAspireSettings settings = Capture(builder, "quartz");

        settings.TablePrefix.Should().Be("OWN_",
            "the connection's own section is bound over the shared one, so it wins where the two disagree");
        settings.Clustered.Should().BeTrue(
            "it is bound over the shared section rather than instead of it, so a setting it says nothing "
            + "about keeps the shared value");
    }

    [Test]
    public void TheOverrideSectionIsThisConnectionsAlone()
    {
        HostApplicationBuilder builder = AspireApplication.Worker(
            ("ConnectionStrings:quartz", AspireApplication.Postgres),
            ("Aspire:Quartz:reporting:TablePrefix", "REPORTING_"));

        QuartzAspireSettings settings = Capture(builder, "quartz");

        settings.TablePrefix.Should().BeNull(
            "a section named after another connection has nothing to do with this one");
    }

    [Test]
    public void TheConnectionStringComesFromTheConnectionStringsSection()
    {
        HostApplicationBuilder builder = AspireApplication.WorkerWith(AspireApplication.Postgres);

        QuartzAspireSettings settings = Capture(builder, "quartz");

        settings.ConnectionString.Should().Be(AspireApplication.Postgres,
            "ConnectionStrings:quartz is what the AppHost's WithReference injected, and reading it is the "
            + "whole reason this method takes a connection name");
    }

    [Test]
    public void TheInjectedConnectionStringWinsOverOneLeftInConfiguration()
    {
        const string Stale = "Host=stale;Username=u;Password=p;Database=stale";

        HostApplicationBuilder builder = AspireApplication.Worker(
            ("Aspire:Quartz:ConnectionString", Stale),
            ("ConnectionStrings:quartz", AspireApplication.Postgres));

        QuartzAspireSettings settings = Capture(builder, "quartz");

        settings.ConnectionString.Should().Be(AspireApplication.Postgres,
            "the AppHost decides where the database is, so a string left in an appsettings file must not "
            + "quietly send the scheduler somewhere else");
    }

    [Test]
    public void TheCallbackHasTheLastWord()
    {
        HostApplicationBuilder builder = AspireApplication.Worker(
            ("ConnectionStrings:quartz", AspireApplication.Postgres),
            ("Aspire:Quartz:TablePrefix", "SHARED_"),
            ("Aspire:Quartz:quartz:TablePrefix", "OWN_"));

        QuartzAspireSettings settings = Capture(builder, "quartz", x => x.TablePrefix = "CODE_");

        settings.TablePrefix.Should().Be("CODE_",
            "code is the most specific of the four sources, so nothing configuration says can beat it");
    }

    /// <summary>
    /// The settings as the call was about to act on them: the callback runs after every configuration
    /// source has had its say, so what it is handed is the answer.
    /// </summary>
    private static QuartzAspireSettings Capture(
        HostApplicationBuilder builder,
        string connectionName,
        Action<QuartzAspireSettings>? then = null)
    {
        QuartzAspireSettings? captured = null;

        builder.AddQuartzPersistentStore(connectionName, settings =>
        {
            captured = settings;
            then?.Invoke(settings);
        });

        captured.Should().NotBeNull("the callback is what this test observes the settings through");
        return captured!;
    }
}
