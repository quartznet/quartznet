using System.Collections.Specialized;
using System.Data.Common;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Util;

namespace Quartz.Tests.Integration.Utils;

public static class DatabaseHelper
{
    public static string GetDataSourceName(string provider)
    {
        return provider switch
        {
            TestConstants.DefaultSqlServerProvider => "default-sqlserver",
            TestConstants.PostgresProvider => "default-postgres",
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown provider")
        };
    }

    /// <summary>
    /// A provider for the given database. Providers are per-scheduler container registrations now, so a
    /// test that wants to look at the database itself builds its own rather than borrowing a scheduler's.
    /// </summary>
    public static IDbProvider CreateDbProvider(string provider)
    {
        return provider switch
        {
            TestConstants.DefaultSqlServerProvider => new DbProvider(TestConstants.DefaultSqlServerProvider, TestConstants.SqlServerConnectionString),
            TestConstants.PostgresProvider => new DbProvider("Npgsql", TestConstants.PostgresConnectionString),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown provider")
        };
    }

    /// <summary>
    /// An unopened connection to the given database, for tests that assert against the stored rows.
    /// </summary>
    public static DbConnection CreateConnection(string provider)
    {
        return CreateDbProvider(provider).CreateConnection();
    }

    public static NameValueCollection CreatePropertiesForProvider(string provider)
    {
        string dataSourceName = GetDataSourceName(provider);

        var properties = new NameValueCollection
        {
            ["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.LocalTransactionJobStore, Quartz",
            ["quartz.jobStore.dataSource"] = dataSourceName,
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
            [$"quartz.dataSource.{dataSourceName}.provider"] = provider,
            [$"quartz.dataSource.{dataSourceName}.connectionString"] = provider switch
            {
                TestConstants.DefaultSqlServerProvider => TestConstants.SqlServerConnectionString,
                TestConstants.PostgresProvider => TestConstants.PostgresConnectionString,
                _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown provider")
            },
            ["quartz.jobStore.driverDelegateType"] = provider switch
            {
                TestConstants.DefaultSqlServerProvider => typeof(SqlServerDelegate).AssemblyQualifiedNameWithoutVersion(),
                TestConstants.PostgresProvider => typeof(PostgreSQLDelegate).AssemblyQualifiedNameWithoutVersion(),
                _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown provider")
            }
        };
        return properties;
    }
}
