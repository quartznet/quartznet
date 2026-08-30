#nullable enable

using Microsoft.Extensions.Hosting;

namespace Quartz.Tests.Unit.Aspire;

/// <summary>
/// Working out which database a connection string is for, and refusing to when it is not clear.
/// </summary>
/// <remarks>
/// <para>
/// An Aspire connection name arrives as a string and nothing else — the AppHost sets
/// <c>ConnectionStrings__quartz</c> and leaves no note of which resource wrote it — so this is the one
/// piece of guessing the package does. The cases below are the strings Aspire's own database resources
/// actually inject, because those are the inputs that matter.
/// </para>
/// <para>
/// The refusals matter more than the matches. A wrong provider name picks the driver delegate that writes
/// the SQL, so the scheduler starts, connects, and then fails at the first trigger acquisition with a
/// syntax error nobody would trace back to a connection string. Both failure messages therefore name
/// <c>QuartzAspireSettings.Provider</c> and <c>DataSourceOptions.Providers</c>.
/// </para>
/// </remarks>
public class ProviderInferenceTest
{
    [TestCase(AspireApplication.Postgres, DataSourceOptions.Providers.Npgsql)]
    [TestCase(AspireApplication.SqlServer, DataSourceOptions.Providers.SqlServer)]
    [TestCase(AspireApplication.MySql, DataSourceOptions.Providers.MySqlConnector)]
    [TestCase(AspireApplication.Sqlite, DataSourceOptions.Providers.Sqlite)]
    [TestCase(AspireApplication.Oracle, DataSourceOptions.Providers.Oracle)]
    [TestCase("Data Source=quartz;Mode=Memory;Cache=Shared", DataSourceOptions.Providers.Sqlite)]
    [TestCase("Data Source=:memory:", DataSourceOptions.Providers.Sqlite)]
    [TestCase("Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=db)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=orcl)));User Id=system;Password=secret",
        DataSourceOptions.Providers.Oracle)]
    [TestCase("Server=db;Initial Catalog=quartz;Integrated Security=True", DataSourceOptions.Providers.SqlServer)]
    public void AConnectionStringSaysWhichDatabaseItIsFor(string connectionString, string expected)
    {
        Provider(connectionString).Should().Be(expected);
    }

    /// <summary>
    /// The two shapes that would collide without the rules that keep them apart, written out so that a
    /// change to either rule fails here rather than in an application.
    /// </summary>
    /// <remarks>
    /// Aspire's SQL Server and MySQL resources inject nearly the same string — a server, a login, a
    /// password and a database. <c>Port</c> is the whole of the difference: SQL Server writes the port
    /// into the server as <c>Server=host,1433</c> and <c>Microsoft.Data.SqlClient</c> rejects a
    /// <c>Port</c> keyword outright, so a string that has one cannot be its.
    /// </remarks>
    [Test]
    public void SqlServerAndMySqlAreToldApartByThePortKeyword()
    {
        Provider(AspireApplication.SqlServer).Should().Be(DataSourceOptions.Providers.SqlServer,
            "SQL Server puts the port inside Server=, so there is no Port keyword to disqualify it");

        Provider(AspireApplication.MySql).Should().Be(DataSourceOptions.Providers.MySqlConnector,
            "the Port keyword is what says this is not SQL Server, which accepts no such keyword");
    }

    [Test]
    public void AnUnrecognisedShapeThrowsRatherThanGuessing()
    {
        Action act = () => Provider("Data Source=orcl;User Id=scott;Password=tiger");

        act.Should().Throw<SchedulerConfigException>(
                "a bare data source name is a TNS alias and a SQL Server instance name equally, and "
                + "choosing between them would pick the driver delegate that writes the SQL")
            .WithMessage("*QuartzAspireSettings.Provider*")
            .And.Message.Should().Contain("DataSourceOptions.Providers")
            .And.Contain("quartz", "the message has to say which connection string it is about");
    }

    [Test]
    public void AnAmbiguousShapeThrowsRatherThanPickingOne()
    {
        Action act = () => Provider("Data Source=quartz.db;Initial Catalog=quartz");

        act.Should().Throw<SchedulerConfigException>()
            .WithMessage("*QuartzAspireSettings.Provider*")
            .And.Message.Should().Contain(DataSourceOptions.Providers.Sqlite)
            .And.Contain(DataSourceOptions.Providers.SqlServer,
                "a message that named neither candidate would leave the reader to guess what it saw");
    }

    [Test]
    public void NoConnectionStringAndNoProviderThrows()
    {
        HostApplicationBuilder builder = AspireApplication.Worker();

        Action act = () => builder.AddQuartzPersistentStore("quartz");

        act.Should().Throw<SchedulerConfigException>(
                "there is nothing to infer from, and a store pointing at no database would fail later and "
                + "less clearly")
            .WithMessage("*ConnectionStrings:quartz*");
    }

    [Test]
    public void ANamedProviderIsNeverInferredOver()
    {
        Provider(AspireApplication.Postgres, settings => settings.Provider = DataSourceOptions.Providers.MySqlConnector)
            .Should().Be(DataSourceOptions.Providers.MySqlConnector,
                "the inference is a fallback for a string nobody classified, not a second opinion");
    }

    [Test]
    public void ANamedProviderIsMatchedWithoutRegardToCase()
    {
        Provider(AspireApplication.Postgres, settings => settings.Provider = "npgsql")
            .Should().Be(DataSourceOptions.Providers.Npgsql,
                "driver descriptions are looked up by an ordinal comparison, so a name that differs only "
                + "in case would otherwise fail at the first connection rather than here");
    }

    [Test]
    public void AProviderQuartzShipsNoDescriptionForIsStillUsable()
    {
        Provider(AspireApplication.Postgres, settings => settings.Provider = "MyDatabase")
            .Should().Be("MyDatabase",
                "an unknown name selects the generic dialect and leaves the description to whatever "
                + "DbMetadataFactory the application registered, which is a configuration rather than a mistake");
    }

    [Test]
    public void TheProviderCanBeNamedInConfiguration()
    {
        HostApplicationBuilder builder = AspireApplication.Worker(
            ("ConnectionStrings:quartz", "Data Source=orcl;User Id=scott;Password=tiger"),
            ("Aspire:Quartz:quartz:Provider", DataSourceOptions.Providers.Oracle));

        builder.AddQuartzPersistentStore("quartz");
        builder.AddQuartz();

        using IHost host = builder.Build();

        AspireApplication.DataSourceOf(host.Services).Provider.Should().Be(DataSourceOptions.Providers.Oracle,
            "an application whose connection string the inference cannot place says so in configuration, "
            + "without having to move the whole registration into code");
    }

    private static string Provider(string connectionString, Action<QuartzAspireSettings>? configureSettings = null)
    {
        HostApplicationBuilder builder = AspireApplication.WorkerWith(connectionString);

        builder.AddQuartzPersistentStore("quartz", configureSettings);
        builder.AddQuartz();

        using IHost host = builder.Build();

        return AspireApplication.DataSourceOf(host.Services).Provider;
    }
}
