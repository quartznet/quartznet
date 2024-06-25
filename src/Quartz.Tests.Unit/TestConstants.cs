namespace Quartz;

public static class TestConstants
{
    static TestConstants()
    {
        SqlServerUser = Environment.GetEnvironmentVariable("MSSQL_USER") ?? "sa";
        SqlServerPassword = Environment.GetEnvironmentVariable("MSSQL_PASSWORD") ?? "Quartz!DockerP4ss";
        // we cannot use trusted connection as it's not available for Linux provider
        SqlServerConnectionString = $"Server=localhost;Database=quartznet;User Id={SqlServerUser};Password={SqlServerPassword};";
        SqlServerConnectionStringMOT = $"Server=localhost,1444;Database=quartznet;User Id={SqlServerUser};Password={SqlServerPassword};";

        PostgresUser = Environment.GetEnvironmentVariable("PG_USER") ?? "quartznet";
        PostgresPassword = Environment.GetEnvironmentVariable("PG_PASSWORD") ?? "quartznet";
        PostgresConnectionString = $"Server=127.0.0.1;Port=5432;Userid={PostgresUser};Password={PostgresPassword};Pooling=true;MinPoolSize=1;MaxPoolSize=20;Timeout=15;SslMode=Disable;Database=quartznet";
    }

    public static string SqlServerUser { get; }
    public static string SqlServerPassword { get; }

    public static string SqlServerConnectionString { get; }
    public static string SqlServerConnectionStringMOT { get; }

    public static string PostgresUser { get; }
    public static string PostgresPassword { get; }
    public static string PostgresConnectionString { get; }

    public const string DefaultSerializerType = "stj";

    public const string DefaultSqlServerProvider = "SqlServer";

    public const string PostgresProvider = "Npgsql";
}