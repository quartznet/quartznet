using System;

// ReSharper disable once CheckNamespace
namespace Quartz
{
    public static class TestConstants
    {
        static TestConstants()
        {
            SqlServerUser = Environment.GetEnvironmentVariable("MSSQL_USER") ?? "sa";
            SqlServerPassword = Environment.GetEnvironmentVariable("MSSQL_PASSWORD") ?? "Quartz!DockerP4ss";
            // we cannot use trusted connection as it's not available for Linux provider
            SqlServerConnectionString = $"Server=localhost;Database=quartznet;User Id={SqlServerUser};Password={SqlServerPassword};";
        }

        public static string SqlServerUser { get; }
        public static string SqlServerPassword { get; }

        public static string SqlServerConnectionString { get; }

#if !BINARY_SERIALIZATION
        public const string DefaultSerializerType = "json";
#else
        public const string DefaultSerializerType = "binary";
#endif

#if NETSTANDARD_DBPROVIDERS
        public const string DefaultSqlServerProvider = "SqlServer-41";
#else
        public const string DefaultSqlServerProvider = "SqlServer-20";
#endif
    }
}