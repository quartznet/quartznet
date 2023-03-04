using System;
using System.Collections.Specialized;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Util;

namespace Quartz.Tests.Integration.Utils;

public static class DatabaseHelper
{
    public static void RegisterDatabaseSettingsForProvider(string provider, out string driverDelegateType)
    {
        switch (provider)
        {
            case TestConstants.DefaultSqlServerProvider:
                driverDelegateType = typeof(SqlServerDelegate).AssemblyQualifiedName;
                DBConnectionManager.Instance.AddConnectionProvider("default", new DbProvider(TestConstants.DefaultSqlServerProvider, TestConstants.SqlServerConnectionString));
                break;
            case TestConstants.PostgresProvider:
                driverDelegateType = typeof(PostgreSQLDelegate).AssemblyQualifiedName;
                DBConnectionManager.Instance.AddConnectionProvider("default", new DbProvider("Npgsql", TestConstants.PostgresConnectionString));
                break;
            default:
                throw new ArgumentOutOfRangeException("Unknown database type " + provider);
        }
    }

    public static NameValueCollection CreatePropertiesForProvider(string provider)
    {
        var properties = new NameValueCollection
        {
            ["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz",
            ["quartz.jobStore.dataSource"] = "default",
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
            ["quartz.dataSource.default.provider"] = provider,
            ["quartz.dataSource.default.connectionString"] = provider switch
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