#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

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
            Status: scheduler.GetStatus(),
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
        return new SchedulerThreadPoolDto(metadata.ThreadPoolTypeName, metadata.ThreadPoolSize);
    }
}

internal record SchedulerJobStoreDto(string Type, bool Clustered, bool Persistent)
{
    public static SchedulerJobStoreDto Create(SchedulerMetadata metadata)
    {
        return new SchedulerJobStoreDto(metadata.JobStoreTypeName, metadata.JobStoreClustered, metadata.JobStorePersistent);
    }
}

internal record SchedulerStatisticsDto(string Version, DateTimeOffset? RunningSince, int JobsExecuted)
{
    public static SchedulerStatisticsDto Create(SchedulerMetadata metadata)
    {
        return new SchedulerStatisticsDto(metadata.Version, metadata.RunningSince, metadata.JobsExecuted);
    }
}