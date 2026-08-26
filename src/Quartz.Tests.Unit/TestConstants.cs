namespace Quartz;

public static class TestConstants
{
    public static string SqlServerUser => Environment.GetEnvironmentVariable("MSSQL_USER") ?? "sa";
    public static string SqlServerPassword => Environment.GetEnvironmentVariable("MSSQL_PASSWORD") ?? "Quartz!DockerP4ss";

    // we cannot use trusted connection as it's not available for Linux provider
    public static string SqlServerConnectionString => Environment.GetEnvironmentVariable("MSSQL_CONNECTION_STRING")
        ?? $"Server=localhost;Database=quartznet;User Id={SqlServerUser};Password={SqlServerPassword};TrustServerCertificate=true;";
    public static string SqlServerConnectionStringMOT => Environment.GetEnvironmentVariable("MSSQL_CONNECTION_STRING_MOT")
        ?? $"Server=localhost,1444;Database=quartznet;User Id={SqlServerUser};Password={SqlServerPassword};TrustServerCertificate=true;";

    public static string PostgresUser => Environment.GetEnvironmentVariable("PG_USER") ?? "quartznet";
    public static string PostgresPassword => Environment.GetEnvironmentVariable("PG_PASSWORD") ?? "quartznet";
    public static string PostgresConnectionString => Environment.GetEnvironmentVariable("PG_CONNECTION_STRING")
        ?? $"Server=127.0.0.1;Port=5432;Userid={PostgresUser};Password={PostgresPassword};Pooling=true;MinPoolSize=1;MaxPoolSize=20;Timeout=15;SslMode=Disable;Database=quartznet";

    public static string MySqlConnectionString => Environment.GetEnvironmentVariable("MYSQL_CONNECTION_STRING")
        ?? "Server = localhost; Database = quartznet; Uid = quartznet; Pwd = quartznet";

    public static string OracleConnectionString => Environment.GetEnvironmentVariable("ORACLE_CONNECTION_STRING")
        ?? "Data Source=(DESCRIPTION=(ADDRESS_LIST=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT=1521)))(CONNECT_DATA=(SERVICE_NAME=xe)));User Id=system;Password=oracle;";

    public static string FirebirdConnectionString => Environment.GetEnvironmentVariable("FIREBIRD_CONNECTION_STRING")
        ?? "User=SYSDBA;Password=masterkey;Database=/firebird/data/quartz.fdb;DataSource=localhost;Port=3050;Dialect=3;Charset=NONE;Role=;Connection lifetime=15;Pooling=true;MinPoolSize=0;MaxPoolSize=50;Packet Size=8192;ServerType=0;";

    public const string DefaultSerializerType = "stj";

    public const string DefaultSqlServerProvider = "SqlServer";

    public const string PostgresProvider = "Npgsql";

    public const string MySqlProvider = "MySqlConnector";

    public const string OracleProvider = "OracleODPManaged";

    public const string FirebirdProvider = "Firebird";
}
