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

namespace Quartz;

/// <summary>
/// A scheduler's runs over time: one <see cref="ExecutionStatisticsBucket" /> per stretch of fire time that
/// holds a run, oldest first.
/// </summary>
/// <seealso cref="Extensibility.IExecutionHistoryStore.QueryExecutionStatistics" />
public sealed record ExecutionStatistics
{
    /// <summary>
    /// The most rows the default implementation of
    /// <see cref="Extensibility.IExecutionHistoryStore.QueryExecutionStatistics" /> reads: 10,000.
    /// </summary>
    public const int DefaultRowLimit = 10_000;

    /// <summary>
    /// How much fire time each bucket covers: the query's <see cref="ExecutionStatisticsQuery.BucketSize" />.
    /// </summary>
    public TimeSpan BucketSize { get; init; }

    /// <summary>
    /// The buckets that hold at least one run, oldest first. A stretch with no run has no bucket.
    /// </summary>
    public List<ExecutionStatisticsBucket> Buckets { get; init; } = [];

    /// <summary>
    /// Whether the store stopped reading before it had counted every matching run, so the oldest buckets
    /// may be short.
    /// </summary>
    /// <remarks>
    /// The default implementation reads newest first and stops at <see cref="DefaultRowLimit" />. The
    /// shipped stores count every run and never set it.
    /// </remarks>
    public bool Truncated { get; init; }
}
