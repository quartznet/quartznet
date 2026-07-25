using System.Data.Common;

namespace Quartz.Impl.AdoJobStore.Common;

/// <summary>
/// Connects through a <see cref="DbDataSource"/> registered in the container rather than through a
/// connection string Quartz holds.
/// </summary>
/// <remarks>
/// The provider name is still needed, because it selects the SQL dialect metadata, but the connection
/// itself comes from the data source. It is passed in rather than looked up from configuration, so this
/// works the same whether the scheduler was configured in code or from a file.
/// </remarks>
internal sealed class DataSourceDbProvider : DbProvider
{
    private readonly DbDataSource source;

    public DataSourceDbProvider(string providerName, DbDataSource source)
        : base(providerName, string.Empty)
    {
        this.source = source;
    }

    public override DbConnection CreateConnection()
    {
        return source.CreateConnection();
    }
}
