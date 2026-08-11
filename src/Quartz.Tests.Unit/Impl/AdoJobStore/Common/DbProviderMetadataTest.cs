#nullable enable

using System.Collections.Specialized;
using System.Data;

using Microsoft.Data.SqlClient;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore.Common;

/// <summary>
/// Covers describing an ADO.NET driver Quartz ships no description for.
/// </summary>
/// <remarks>
/// This used to be the one thing only a <c>quartz.config</c> file could say, which is why the file was
/// still being read. Both routes — a metadata callback in code and <c>quartz.dbprovider.*</c> keys
/// arriving through the container — have to end in a working <see cref="IDbProvider"/>.
/// </remarks>
public class DbProviderMetadataTest
{
    /// <summary>
    /// A name Quartz ships no description for, so nothing but the registration under test can satisfy it.
    /// </summary>
    private const string FictionalProvider = "MyFictionalDatabase";

    private const string ConnectionString = "Server=nowhere;Database=none";

    /// <summary>
    /// Describes the fictional provider using a real driver's types, so the description is one that can
    /// actually build a command rather than one that only survives being stored.
    /// </summary>
    private static DbMetadata DescribeFictionalProvider() => new()
    {
        ProductName = "My Fictional Database",
        AssemblyName = typeof(SqlConnection).Assembly.FullName,
        ConnectionType = typeof(SqlConnection),
        CommandType = typeof(SqlCommand),
        ParameterType = typeof(SqlParameter),
        ParameterDbType = typeof(SqlDbType),
        ParameterDbTypePropertyName = nameof(SqlParameter.SqlDbType),
        ParameterNamePrefix = "@",
        ExceptionType = typeof(SqlException),
        UseParameterNamePrefixInParameterCollection = true,
        BindByName = true,
        DbBinaryTypeName = "VarBinary",
    };

    private static NameValueCollection FictionalProviderProperties(string parameterNamePrefix = "@")
    {
        var prefix = $"quartz.dbprovider.{FictionalProvider}.";
        return new NameValueCollection
        {
            [prefix + "productName"] = "My Fictional Database",
            [prefix + "assemblyName"] = typeof(SqlConnection).Assembly.FullName,
            [prefix + "connectionType"] = typeof(SqlConnection).AssemblyQualifiedName,
            [prefix + "commandType"] = typeof(SqlCommand).AssemblyQualifiedName,
            [prefix + "parameterType"] = typeof(SqlParameter).AssemblyQualifiedName,
            [prefix + "parameterDbType"] = typeof(SqlDbType).AssemblyQualifiedName,
            [prefix + "parameterDbTypePropertyName"] = nameof(SqlParameter.SqlDbType),
            [prefix + "parameterNamePrefix"] = parameterNamePrefix,
            [prefix + "exceptionType"] = typeof(SqlException).AssemblyQualifiedName,
            [prefix + "useParameterNamePrefixInParameterCollection"] = "true",
            [prefix + "bindByName"] = "true",
            [prefix + "dbBinaryTypeName"] = "VarBinary",
        };
    }

    [Test]
    public void AProviderDescribedInCodeResolvesADbProvider()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store =>
            store.UseGenericDatabase(FictionalProvider, ConnectionString, DescribeFictionalProvider)));

        using var provider = services.BuildServiceProvider();

        var dbProvider = provider.GetRequiredService<IDbProvider>();

        dbProvider.ConnectionString.Should().Be(ConnectionString);
        dbProvider.Metadata.ProductName.Should().Be("My Fictional Database");
        dbProvider.Metadata.GetParameterName("schedName").Should().Be("@schedName");

        // Init has to have been called, or the store cannot bind a binary parameter at all.
        dbProvider.Metadata.DbBinaryType.Should().Be(SqlDbType.VarBinary);
        dbProvider.Metadata.ParameterDbTypeProperty.Should().NotBeNull();

        // The point of the description is that commands and connections can be built from it.
        dbProvider.CreateCommand().Should().BeOfType<SqlCommand>();
        dbProvider.CreateConnection().Should().BeOfType<SqlConnection>();
    }

    [Test]
    public void AProviderDescribedInCodeCanUseANamedConnectionString()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:fictional"] = ConnectionString,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddQuartz(q => q.UsePersistentStore(store => store.UseGenericDatabase(
            FictionalProvider,
            options => options.ConnectionStringName = "fictional",
            DescribeFictionalProvider)));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IDbProvider>().ConnectionString.Should().Be(ConnectionString);
    }

    [Test]
    public void AProviderDescribedByPropertiesResolvesADbProvider()
    {
        var services = new ServiceCollection();
        services.AddQuartz(
            FictionalProviderProperties(),
            q => q.UsePersistentStore(store => store.UseGenericDatabase(FictionalProvider, ConnectionString)));

        using var provider = services.BuildServiceProvider();

        var dbProvider = provider.GetRequiredService<IDbProvider>();

        dbProvider.Metadata.ProductName.Should().Be("My Fictional Database");
        dbProvider.Metadata.DbBinaryType.Should().Be(SqlDbType.VarBinary);
        dbProvider.CreateCommand().Should().BeOfType<SqlCommand>();
    }

    [Test]
    public void AProviderDescribedInAppSettingsResolvesADbProvider()
    {
        // The same quartz.dbprovider.* keys, written where an application actually keeps them now that
        // there is no file to put them in.
        var values = new Dictionary<string, string?>();
        var properties = FictionalProviderProperties();
        foreach (var key in properties.AllKeys)
        {
            values["Quartz:" + key] = properties[key];
        }

        var section = new ConfigurationBuilder().AddInMemoryCollection(values).Build().GetSection("Quartz");

        var services = new ServiceCollection();
        services.AddQuartz(
            section,
            q => q.UsePersistentStore(store => store.UseGenericDatabase(FictionalProvider, ConnectionString)));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IDbProvider>().Metadata.ProductName.Should().Be("My Fictional Database");
    }

    [Test]
    public void TwoContainersCanDescribeTheSameProviderNameDifferently()
    {
        static ServiceProvider Container(string parameterNamePrefix)
        {
            var services = new ServiceCollection();
            services.AddQuartz(q => q.UsePersistentStore(store => store.UseGenericDatabase(
                FictionalProvider,
                ConnectionString,
                () => DescribeFictionalProvider() with { ParameterNamePrefix = parameterNamePrefix })));

            return services.BuildServiceProvider();
        }

        using var first = Container("@");
        using var second = Container(":");

        // A cache shared between containers would hand the second one the first one's description, which
        // is exactly what a process-wide metadata lookup used to do.
        first.GetRequiredService<IDbProvider>().Metadata.ParameterNamePrefix.Should().Be("@");
        second.GetRequiredService<IDbProvider>().Metadata.ParameterNamePrefix.Should().Be(":");
    }

    [Test]
    public void ADescriptionInCodeBeatsOneQuartzShips()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store => store.UseGenericDatabase(
            "SqlServer",
            ConnectionString,
            () => DescribeFictionalProvider() with { ProductName = "Something Else Entirely" })));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IDbProvider>().Metadata.ProductName.Should().Be("Something Else Entirely");
    }

    [Test]
    public void ADescriptionInCodeBeatsOneQuartzShipsEvenWhenAnotherSchedulerRegisteredFirst()
    {
        var services = new ServiceCollection();

        // The first call pulls the built-in descriptions into the container, so the second call's
        // description registers after them. It still has to win.
        services.AddQuartz(q => q.UseInMemoryStore());
        services.AddQuartz("reporting", q => q.UsePersistentStore(store => store.UseGenericDatabase(
            "SqlServer",
            ConnectionString,
            () => DescribeFictionalProvider() with { ProductName = "Something Else Entirely" })));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredKeyedService<IDbProvider>("reporting").Metadata.ProductName
            .Should().Be("Something Else Entirely");
    }

    [Test]
    public void AProviderNobodyDescribedIsRejectedNamingTheOnesThatAreKnown()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store =>
            store.UseGenericDatabase("NoSuchProvider", ConnectionString)));

        using var provider = services.BuildServiceProvider();

        var resolve = () => provider.GetRequiredService<IDbProvider>();

        resolve.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*There is no metadata information for provider 'NoSuchProvider'*")
            .And.Message.Should().Contain("SqlServer", "the error has to say which names would work");
    }

    [Test]
    public void ADescriptionThatCannotWorkIsRejectedWhileConfiguring()
    {
        // dbBinaryTypeName cannot be resolved without knowing the parameter's db type enum, and finding
        // that out when the first command is built would be far too late.
        var configure = () => new ServiceCollection().AddQuartz(q => q.UsePersistentStore(store =>
            store.UseGenericDatabase(FictionalProvider, ConnectionString, () => new DbMetadata
            {
                ProductName = "Half Described",
                ConnectionType = typeof(SqlConnection),
                CommandType = typeof(SqlCommand),
                DbBinaryTypeName = "VarBinary",
            })));

        configure.Should().Throw<ArgumentException>();
    }
}
