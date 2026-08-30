#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

namespace Quartz.Tests.Unit.Aspire;

/// <summary>
/// Where the store's connections come from, once the database is known.
/// </summary>
/// <remarks>
/// <para>
/// The ladder is the one <c>how-tos/aspire.md</c> teaches by hand: a <c>DbDataSource</c> keyed with the
/// connection name, then the container's single unkeyed one, then the connection string. A data source is
/// preferred because whatever it was built with is then in play for Quartz's own statements — its type
/// mappers, its logging, its connection multiplexing — since commands are made by the connection rather
/// than from a driver description.
/// </para>
/// <para>
/// The rung SQL Server does not stand on is the one worth a test of its own.
/// </para>
/// </remarks>
public class ConnectionSelectionTest
{
    [Test]
    public void AKeyedDataSourceUnderTheConnectionNameIsTheMostSpecificAnswer()
    {
        HostApplicationBuilder builder = AspireApplication.WorkerWith(AspireApplication.Postgres);
        builder.Services.AddNpgsqlDataSource(AspireApplication.Postgres);
        builder.Services.AddNpgsqlDataSource(AspireApplication.Postgres, serviceKey: "quartz");

        DataSourceOptions options = Configure(builder);

        options.DataSourceServiceKey.Should().Be("quartz",
            "an application talking to two databases keys them apart, and the connection name is the key "
            + "Aspire's own AddKeyed* registrations use");
        options.UseRegisteredDataSource.Should().BeFalse(
            "a service key implies it, and setting both would say the same thing twice");
    }

    [Test]
    public void TheContainersUnkeyedDataSourceIsTakenWhenNothingIsKeyed()
    {
        HostApplicationBuilder builder = AspireApplication.WorkerWith(AspireApplication.Postgres);
        builder.Services.AddNpgsqlDataSource(AspireApplication.Postgres);

        DataSourceOptions options = Configure(builder);

        options.UseRegisteredDataSource.Should().BeTrue(
            "AddNpgsqlDataSource(\"quartz\") registers an unkeyed DbDataSource, which is the one Quartz asks for");
        options.DataSourceServiceKey.Should().BeNull();
    }

    [Test]
    public void AKeyedDataSourceUnderSomebodyElsesNameIsNotThisStores()
    {
        HostApplicationBuilder builder = AspireApplication.WorkerWith(AspireApplication.Postgres);
        builder.Services.AddNpgsqlDataSource(AspireApplication.Postgres, serviceKey: "reporting");

        DataSourceOptions options = Configure(builder);

        options.DataSourceServiceKey.Should().BeNull();
        options.UseRegisteredDataSource.Should().BeFalse();
        options.ConnectionStringName.Should().Be("quartz",
            "a data source keyed for another connection is another database, so this store falls back to "
            + "the string the AppHost injected rather than reaching into it");
    }

    [Test]
    public void TheConnectionStringIsTheAnswerWhenNoDataSourceIsRegistered()
    {
        DataSourceOptions options = Configure(AspireApplication.WorkerWith(AspireApplication.Postgres));

        options.ConnectionString.Should().Be(AspireApplication.Postgres);
        options.ConnectionStringName.Should().Be("quartz",
            "both are set: the string wins where it is present, and the name still resolves the store when "
            + "configuration supplies it later");
        options.UseRegisteredDataSource.Should().BeFalse();
    }

    /// <summary>
    /// SQL Server never takes the data-source path, whatever is registered.
    /// </summary>
    /// <remarks>
    /// <c>Microsoft.Data.SqlClient</c> ships no <see cref="System.Data.Common.DbDataSource"/>
    /// implementation and Aspire's SQL Server client integration registers a scoped <c>SqlConnection</c>
    /// instead. Resolving "the container's DbDataSource" there finds some other database's, or nothing —
    /// and finds out at the first connection rather than at startup.
    /// </remarks>
    [Test]
    public void SqlServerNeverTakesADataSource()
    {
        HostApplicationBuilder builder = AspireApplication.WorkerWith(AspireApplication.SqlServer);
        builder.Services.AddNpgsqlDataSource(AspireApplication.Postgres);
        builder.Services.AddNpgsqlDataSource(AspireApplication.Postgres, serviceKey: "quartz");

        DataSourceOptions options = Configure(builder);

        options.Provider.Should().Be(DataSourceOptions.Providers.SqlServer);
        options.UseRegisteredDataSource.Should().BeFalse();
        options.DataSourceServiceKey.Should().BeNull(
            "a DbDataSource beside a SQL Server connection belongs to a different database, and taking it "
            + "would point the scheduler at that one");
        options.ConnectionStringName.Should().Be("quartz");
    }

    private static DataSourceOptions Configure(HostApplicationBuilder builder)
    {
        builder.AddQuartzPersistentStore("quartz");
        builder.AddQuartz();

        using IHost host = builder.Build();

        return AspireApplication.DataSourceOf(host.Services);
    }
}
