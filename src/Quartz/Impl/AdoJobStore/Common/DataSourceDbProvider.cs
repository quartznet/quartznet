using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.Options;

namespace Quartz.Impl.AdoJobStore.Common;

internal sealed class DataSourceDbProvider : DbProvider
{
    private readonly DbDataSource source;

    public DataSourceDbProvider(IOptions<QuartzOptions> options, DbDataSource source) : base(GetDbProviderName(options), string.Empty)
    {
        this.source = source;
    }

    private static string GetDbProviderName(IOptions<QuartzOptions> options)
    {
        string dataSourceName = "default";
        if(options.Value.TryGetValue($"quartz.dataSource.{dataSourceName}.provider", out var value))
        {
            if (value is null)
            {
                ThrowInvalidDataSourceNameException();
            }

            return value;
        }

        ThrowInvalidDataSourceNameException();
        return default;

        [DoesNotReturn]
        void ThrowInvalidDataSourceNameException()
        {
            throw new SchedulerException($"Provider not specified for DataSource: {dataSourceName}, DataSourceProvider expects name 'default'");
        }
    }

    public override DbConnection CreateConnection()
    {
        return this.source.CreateConnection();
    }
}
