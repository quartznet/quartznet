using Quartz.Impl.AdoJobStore.Common;

namespace Quartz;

public static class AdoProviderOptionsExtensions
{
#if NET8_0_OR_GREATER
    /// <summary>
    /// Use a <see cref="DataSourceDbProvider"/>. Requires <see cref="ServiceCollectionExtensions.AddDataSourceProvider"/> to have been called.
    /// </summary>
    public static void UseDataSourceConnectionProvider(this SchedulerBuilder.AdoProviderOptions options)
    {
        options.UseConnectionProvider<DataSourceDbProvider>();
    }
#endif
}