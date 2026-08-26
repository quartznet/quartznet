#nullable enable

using Microsoft.Data.SqlClient;

using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore.Common;

/// <summary>
/// The two checks that need to know what kind of connection a provider produces keep working when the
/// driver description names no type.
/// </summary>
/// <remarks>
/// One refuses an enlisted connection that came from a different driver — which would otherwise fail as
/// a cast error deep inside the first statement — and the other warns about a SQL Server connection
/// paired with a delegate that speaks generic SQL. Both read the description before, and a description
/// behind a factory has nothing to read, so both would silently have stopped checking.
/// </remarks>
public sealed class DbProviderConnectionsTest
{
    private static readonly DbMetadata TypeFree = new()
    {
        ProductName = "Fake",
        ParameterNamePrefix = "@",
        BindByName = true,
    };

    [Test]
    public void AFactoryProviderAnswersWithWhatTheFactoryHandsOut()
    {
        IDbProvider provider = new ProviderFactoryDbProvider(TypeFree, SqlClientFactory.Instance, "Server=nowhere");

        provider.ExpectedConnectionType().Should().Be<SqlConnection>(
            "the SqlClient sniff has to keep recognising SQL Server when it was registered as "
            + "UseSqlServer(SqlClientFactory.Instance, ...)");
    }

    [Test]
    public void ADataSourceProviderAnswersWithWhatTheDataSourceHandsOut()
    {
        using FakeDataSource dataSource = new();
        IDbProvider provider = new DataSourceDbProvider(TypeFree, dataSource);

        provider.ExpectedConnectionType().Should().Be<FakeConnection>();
    }

    [Test]
    public void ADescribedTypeIsStillTheAnswerWhenThereIsOne()
    {
        DbMetadata described = TypeFree with
        {
            ConnectionType = typeof(FakeConnection),
            CommandType = typeof(FakeCommand),
        };

        IDbProvider provider = new DbProvider(described, "irrelevant");

        provider.ExpectedConnectionType().Should().Be<FakeConnection>(
            "the description is the cheaper answer and the one the name path has always used; asking a "
            + "provider for a connection is what the other paths do instead");
    }

    [Test]
    public void AProviderQuartzDidNotWriteAndADescriptionThatSaysNothingAnswerNothing()
    {
        IDbProvider provider = new ProviderThatSaysNothing();

        provider.ExpectedConnectionType().Should().BeNull(
            "a check that cannot be made is dropped rather than guessed at");
    }

    private sealed class ProviderThatSaysNothing : IDbProvider
    {
        public System.Data.Common.DbCommand CreateCommand() => throw new NotSupportedException();

        public System.Data.Common.DbConnection CreateConnection() => throw new NotSupportedException();

        public string ConnectionString => "";

        public DbMetadata Metadata => TypeFree;

        public void Shutdown()
        {
        }
    }
}
