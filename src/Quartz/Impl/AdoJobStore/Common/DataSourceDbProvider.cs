using System.Data.Common;

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
        if(options.Value.TryGetValue($"quartz.dataSource.{SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName}.provider", out var value))
        {
            if (value is null)
            {
                throw new SchedulerException($"Provider not specified for DataSource: {SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName}");
            }

            return value;
        }

        throw new SchedulerException($"Provider not specified for DataSource: {SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName}");
    }

    public override DbConnection CreateConnection()
    {
        return this.source.CreateConnection();
    }
}
