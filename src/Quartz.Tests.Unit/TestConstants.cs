using System;

// ReSharper disable once CheckNamespace
namespace Quartz;

    public static class TestConstants
    {
        public static string SqlServerUser => Environment.GetEnvironmentVariable("MSSQL_USER") ?? "sa";
        public static string SqlServerPassword => Environment.GetEnvironmentVariable("MSSQL_PASSWORD") ?? "Quartz!DockerP4ss";
        
        public static string MySqlConnectionString => Environment.GetEnvironmentVariable("MYSQL_CONNECTION_STRING") ?? "Server=localhost;Database=quartznet;Uid=quartznet;Pwd=quartznet";
        
        public static string SqlServerConnectionString =>
            Environment.GetEnvironmentVariable("MSSQL_CONNECTION_STRING")
            ?? $"Server=localhost;Database=quartznet;User Id={SqlServerUser};Password={SqlServerPassword};";

        public static string SqlServerConnectionStringMOT =>
            Environment.GetEnvironmentVariable("MSSQL_CONNECTION_STRING_MOT")
            ?? $"Server=localhost,1444;Database=quartznet;User Id={SqlServerUser};Password={SqlServerPassword};";

        public static string PostgresUser => Environment.GetEnvironmentVariable("PG_USER") ?? "quartznet";
        public static string PostgresPassword => Environment.GetEnvironmentVariable("PG_PASSWORD") ?? "quartznet";

        public static string PostgresConnectionString =>
            Environment.GetEnvironmentVariable("PG_CONNECTION_STRING")
            ?? $"Server=127.0.0.1;Port=5432;Userid={PostgresUser};Password={PostgresPassword};Pooling=true;MinPoolSize=1;MaxPoolSize=20;Timeout=15;SslMode=Disable;Database=quartznet";

        public const string DefaultSerializerType = "stj";

        public const string DefaultSqlServerProvider = "SqlServer";

        public const string PostgresProvider = "Npgsql";
    }
