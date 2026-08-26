#nullable enable

using System.Data;
using System.Data.Common;

using Microsoft.Data.SqlClient;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Tests.Unit.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// Registering a database by handing over the driver's own <see cref="DbProviderFactory"/> rather than
/// naming it.
/// </summary>
/// <remarks>
/// The name path resolves five types per driver with <c>Type.GetType</c> and constructs two of them,
/// which is what a trimmed application cannot rely on surviving. These registrations name nothing: the
/// provider name still chooses how parameters are spelled, and the factory supplies everything else.
/// </remarks>
public sealed class ProviderFactoryRegistrationTest
{
    private const string ConnectionString = "Server=nowhere;Database=none";

    [Test]
    public void UseSqlServer_WithAFactory_ResolvesAProviderThatNamesNoType()
    {
        using ServiceProvider container = Container(store => store.UseSqlServer(SqlClientFactory.Instance, ConnectionString));

        IDbProvider provider = container.GetRequiredService<IDbProvider>();

        provider.Should().BeOfType<ProviderFactoryDbProvider>();
        provider.ConnectionString.Should().Be(ConnectionString);
        provider.Metadata.GetParameterName("schedName").Should().Be("@schedName",
            "the provider name still chooses the driver description; only the half of it that names types "
            + "is left unread");
        provider.Metadata.ConnectionType.Should().BeNull();
        provider.CreateConnection().Should().BeOfType<SqlConnection>(
            "the factory hands back the driver's own connection, which is the point of handing it over");
    }

    [Test]
    public void UsePostgres_WithAFactory_ResolvesAProviderThatNamesNoType()
    {
        using ServiceProvider container = Container(store => store.UsePostgres(NpgsqlFactory.Instance, ConnectionString));

        IDbProvider provider = container.GetRequiredService<IDbProvider>();

        provider.Should().BeOfType<ProviderFactoryDbProvider>();
        provider.Metadata.GetParameterName("schedName").Should().Be(":schedName");
        provider.Metadata.CommandType.Should().BeNull();
        provider.CreateConnection().Should().BeOfType<NpgsqlConnection>();
    }

    [Test]
    public void TheDriverDelegateIsStillChosenByTheOverload()
    {
        using ServiceProvider container = Container(store => store.UsePostgres(NpgsqlFactory.Instance, ConnectionString));

        container.GetRequiredService<IDriverDelegate>().Should().BeOfType<PostgreSQLDelegate>(
            "handing over a factory says how to reach the database, not which SQL it speaks");
    }

    [Test]
    public void AConnectionStringNameStillResolvesThroughTheFactoryOverloadsSibling()
    {
        using ServiceProvider container = Container(store => store.UsePostgres(options =>
        {
            options.ProviderFactory = NpgsqlFactory.Instance;
            options.ConnectionString = ConnectionString;
        }));

        container.GetRequiredService<IDbProvider>().Should().BeOfType<ProviderFactoryDbProvider>(
            "the factory is an ordinary data source setting, so everything else a data source can say "
            + "still applies to it");
    }

    /// <summary>
    /// The two seams the Oracle overload exists for, checked on a driver made of nothing so that the
    /// plumbing is what is under test rather than ODP.NET.
    /// </summary>
    [Test]
    public void TheSeamsAConfigureCallbackSetsReachTheCommandAndTheBinaryParameter()
    {
        using ServiceProvider container = Container(store => store.UseOracle(
            FakeDbProviderFactory.Instance,
            ConnectionString,
            options =>
            {
                options.ConfigureCommand = command => ((FakeCommand) command).BindByName = false;
                options.ConfigureBinaryParameter = parameter => parameter.Size = -1;
            }));

        IDbProvider provider = container.GetRequiredService<IDbProvider>();

        using DbCommand command = provider.CreateCommand();
        command.Should().BeOfType<FakeCommand>().Which.BindByName.Should().BeFalse(
            "a driver reached through its factory names no command type, so the seam is the only way "
            + "BindByName is set - and on Oracle every statement binds by position without it");

        DbParameter parameter = command.CreateParameter();
        provider.Metadata.ApplyParameterType(parameter, provider.Metadata.BinaryParameterType);
        parameter.Size.Should().Be(-1);
    }

    [Test]
    public void UseGenericDatabase_WithAFactory_TakesTheDescriptionItWasGiven()
    {
        DbMetadata described = new()
        {
            ProductName = "My Database",
            ParameterNamePrefix = "$",
            UseParameterNamePrefixInParameterCollection = true,
            BindByName = true,
        };

        using ServiceProvider container = Container(store => store.UseGenericDatabase(FakeDbProviderFactory.Instance, ConnectionString, described));

        IDbProvider provider = container.GetRequiredService<IDbProvider>();

        provider.Should().BeOfType<ProviderFactoryDbProvider>();
        provider.Metadata.Should().BeSameAs(described,
            "there is no provider name to look a description up by, because the description arrived");
        provider.CreateConnection().Should().BeOfType<FakeConnection>();
        container.GetRequiredService<IDriverDelegate>().Should().BeOfType<StdAdoDelegate>();
    }

    /// <summary>
    /// A binary parameter on a driver that describes no parameter type binds as
    /// <see cref="DbType.Binary" />, which every driver that ships a factory maps for itself.
    /// </summary>
    [Test]
    public void ABlobBindsAsDbTypeBinaryWhenTheDescriptionNamesNoParameterType()
    {
        using ServiceProvider container = Container(store => store.UseSqlServer(SqlClientFactory.Instance, ConnectionString));

        IDbProvider provider = container.GetRequiredService<IDbProvider>();
        using DbCommand command = provider.CreateCommand();
        DbParameter parameter = command.CreateParameter();

        provider.Metadata.ApplyParameterType(parameter, provider.Metadata.BinaryParameterType);

        parameter.DbType.Should().Be(DbType.Binary);
    }

    private static ServiceProvider Container(Action<IPersistentStoreBuilder> configureStore)
    {
        ServiceCollection services = new();
        services.AddQuartz(q => q.UsePersistentStore(configureStore));
        return services.BuildServiceProvider();
    }
}
