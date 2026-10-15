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
    /// <summary>
    /// The execution groups the node serves, as its configuration spells them; empty when it serves
    /// every group not refused. Absent on a host older than 4.6, which reads as empty.
    /// </summary>
    /// <remarks>
    /// An <c>init</c> property rather than a positional parameter, so a 4.5 client reading a 4.6 host's
    /// body — and a 4.6 client reading a 4.5 host's — each ignore what the other does not know. The
    /// getter answers the empty list for a member the body did not carry: the generated reader builds
    /// the record without its initializers and leaves such a member <see langword="null" />, where
    /// reflection would have kept the initializer's value.
    /// </remarks>
    public string[] ServedExecutionGroups
    {
        get => field ?? [];
        init;
    } = [];

    /// <summary>
    /// The execution groups the node refuses; empty when it refuses none.
    /// </summary>
    public string[] RefusedExecutionGroups
    {
        get => field ?? [];
        init;
    } = [];

    /// <remarks>
    /// The metadata answers all three of the scheduler's own fields, and answers them in the one call
    /// that was already made: reading the properties instead cost a scheduler in another process two
    /// further round trips, each of them blocking the request thread.
    /// </remarks>
    public static SchedulerDto Create(SchedulerMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        return new SchedulerDto(
            SchedulerInstanceId: metadata.SchedulerInstanceId,
            Name: metadata.SchedulerName,
            Status: metadata.Status,
            ThreadPool: SchedulerThreadPoolDto.Create(metadata),
            JobStore: SchedulerJobStoreDto.Create(metadata),
            Statistics: SchedulerStatisticsDto.Create(metadata)
        )
        {
            ServedExecutionGroups = metadata.ServedExecutionGroups,
            RefusedExecutionGroups = metadata.RefusedExecutionGroups,
        };
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

internal record SchedulerStatisticsDto(string Version, DateTimeOffset? RunningSince, int JobsExecuted, int LocalExecutingJobs)
{
    public static SchedulerStatisticsDto Create(SchedulerMetadata metadata)
    {
        return new SchedulerStatisticsDto(metadata.Version, metadata.RunningSince, metadata.JobsExecuted, metadata.LocalExecutingJobs);
    }
}