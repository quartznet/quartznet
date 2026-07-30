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
    public static SchedulerDto Create(IScheduler scheduler, SchedulerMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(scheduler);

        ArgumentNullException.ThrowIfNull(metadata);

        return new SchedulerDto(
            SchedulerInstanceId: scheduler.SchedulerInstanceId,
            Name: scheduler.SchedulerName,
            Status: SchedulerHeaderDto.TranslateStatus(scheduler),
            ThreadPool: SchedulerThreadPoolDto.Create(metadata),
            JobStore: SchedulerJobStoreDto.Create(metadata),
            Statistics: SchedulerStatisticsDto.Create(metadata)
        );
    }
}

internal record SchedulerThreadPoolDto(string Type, int Size)
{
    public static SchedulerThreadPoolDto Create(SchedulerMetadata metadata)
    {
        return new SchedulerThreadPoolDto(metadata.ThreadPoolType.AssemblyQualifiedNameWithoutVersion(), metadata.ThreadPoolSize);
    }
}

internal record SchedulerJobStoreDto(string Type, bool Clustered, bool Persistent)
{
    public static SchedulerJobStoreDto Create(SchedulerMetadata metadata)
    {
        return new SchedulerJobStoreDto(metadata.JobStoreType.AssemblyQualifiedNameWithoutVersion(), metadata.JobStoreClustered, metadata.JobStoreSupportsPersistence);
    }
}

internal record SchedulerStatisticsDto(string Version, DateTimeOffset? RunningSince, int JobsExecuted)
{
    public static SchedulerStatisticsDto Create(SchedulerMetadata metadata)
    {
        return new SchedulerStatisticsDto(metadata.Version, metadata.RunningSince, metadata.JobsExecuted);
    }
}