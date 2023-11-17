using System.Data;
using System.Data.Common;

using Microsoft.Data.SqlClient;

using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Examples.AspNetCore;

public class CustomSqlServerConnectionProvider : IDbProvider
{
    private readonly ILogger<CustomSqlServerConnectionProvider> logger;
    private readonly IConfiguration configuration;

    public CustomSqlServerConnectionProvider(
        ILogger<CustomSqlServerConnectionProvider> logger,
        IConfiguration configuration)
    {
        this.logger = logger;
        this.configuration = configuration;
        Metadata = new DbMetadata
        {
            AssemblyName = typeof(SqlConnection).AssemblyQualifiedName,
            BindByName = true,
            CommandType = typeof(SqlCommand),
            ConnectionType = typeof(SqlConnection),
            DbBinaryTypeName = "VarBinary",
            ExceptionType = typeof(SqlException),
            ParameterDbType = typeof(SqlDbType),
            ParameterDbTypePropertyName = "SqlDbType",
            ParameterNamePrefix = "@",
            ParameterType = typeof(SqlParameter),
            UseParameterNamePrefixInParameterCollection = true
        };
        Metadata.Init();
    }

    public void Initialize()
    {
        logger.LogInformation("Initializing");
    }

    public DbCommand CreateCommand()
    {
        return new SqlCommand();
    }

    public DbConnection CreateConnection()
    {
        return new SqlConnection(ConnectionString);
    }

    public string ConnectionString
    {
        get => configuration.GetConnectionString("Quartz")!;
        set => throw new NotImplementedException();
    }

    public DbMetadata Metadata { get; }

    public void Shutdown()
    {
        logger.LogInformation("Shutting down");
    }
}