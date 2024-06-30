using Quartz.Util;

namespace Quartz.HttpApiContract;

internal record SchedulerDto(
    string SchedulerInstanceId,
    string Name,
    SchedulerStatus Status,
    SchedulerThreadPoolDto ThreadPool,
    SchedulerJobStoreDto JobStore,
    SchedulerStatisticsDto Statistics
)
{
    public static SchedulerDto Create(IScheduler scheduler, SchedulerMetaData metaData)
    {
        ArgumentNullException.ThrowIfNull(scheduler);

        ArgumentNullException.ThrowIfNull(metaData);

        return new SchedulerDto(
            SchedulerInstanceId: scheduler.SchedulerInstanceId,
            Name: scheduler.SchedulerName,
            Status: SchedulerHeaderDto.TranslateStatus(scheduler),
            ThreadPool: SchedulerThreadPoolDto.Create(metaData),
            JobStore: SchedulerJobStoreDto.Create(metaData),
            Statistics: SchedulerStatisticsDto.Create(metaData)
        );
    }
}

internal record SchedulerThreadPoolDto(string Type, int Size)
{
    public static SchedulerThreadPoolDto Create(SchedulerMetaData metaData)
    {
        return new SchedulerThreadPoolDto(metaData.ThreadPoolType.AssemblyQualifiedNameWithoutVersion(), metaData.ThreadPoolSize);
    }
}

internal record SchedulerJobStoreDto(string Type, bool Clustered, bool Persistent)
{
    public static SchedulerJobStoreDto Create(SchedulerMetaData metaData)
    {
        return new SchedulerJobStoreDto(metaData.JobStoreType.AssemblyQualifiedNameWithoutVersion(), metaData.JobStoreClustered, metaData.JobStoreSupportsPersistence);
    }
}

internal record SchedulerStatisticsDto(string Version, DateTimeOffset? RunningSince, int NumberOfJobsExecuted)
{
    public static SchedulerStatisticsDto Create(SchedulerMetaData metaData)
    {
        return new SchedulerStatisticsDto(metaData.Version, metaData.RunningSince, metaData.NumberOfJobsExecuted);
    }
}