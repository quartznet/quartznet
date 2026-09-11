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

using Quartz.Extensibility;

namespace Quartz.Dashboard.Services;

/// <summary>
/// Turns one history feed's records into the other's.
/// </summary>
/// <remarks>
/// <see cref="IDashboardHistoryStore" /> is the dashboard's 4.0 seam and
/// <see cref="IExecutionHistoryStore" /> is the one Quartz itself now keeps history behind. They carry
/// the same rows under different names — the dashboard's misfire names its job with a
/// <see cref="JobKeyDto" /> where Quartz names it with a <see cref="Quartz.JobKey" />, and the
/// dashboard's queries spell their two filters <c>JobFilter</c> and <c>TriggerFilter</c> where Quartz's
/// spell them <c>JobContains</c> and <c>TriggerContains</c> — so an adapter each way is a rename and a
/// copy, and nothing an application registered against either seam stops working.
/// </remarks>
internal static class DashboardHistoryMapping
{
    public static ExecutionHistoryEntry AsExecutionHistoryEntry(this DashboardHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new ExecutionHistoryEntry(
            entry.SchedulerName,
            entry.SchedulerInstanceId,
            entry.JobGroup,
            entry.JobName,
            entry.TriggerGroup,
            entry.TriggerName,
            entry.FiredAtUtc,
            entry.Duration,
            entry.Succeeded,
            entry.ExceptionMessage);
    }

    public static DashboardHistoryEntry AsDashboardHistoryEntry(this ExecutionHistoryEntry entry)
    {
        return new DashboardHistoryEntry(
            entry.SchedulerName,
            entry.SchedulerInstanceId,
            entry.JobGroup,
            entry.JobName,
            entry.TriggerGroup,
            entry.TriggerName,
            entry.FiredAtUtc,
            entry.Duration,
            entry.Succeeded,
            entry.ExceptionMessage);
    }

    public static MisfireHistoryEntry AsMisfireHistoryEntry(this DashboardMisfireEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new MisfireHistoryEntry(
            entry.SchedulerName,
            entry.SchedulerInstanceId,
            entry.TriggerGroup,
            entry.TriggerName,
            entry.JobKey is null ? null : new JobKey(entry.JobKey.Name, entry.JobKey.Group),
            entry.MisfiredAtUtc,
            entry.ScheduledFireTimeUtc);
    }

    public static DashboardMisfireEntry AsDashboardMisfireEntry(this MisfireHistoryEntry entry)
    {
        return new DashboardMisfireEntry(
            entry.SchedulerName,
            entry.SchedulerInstanceId,
            entry.TriggerGroup,
            entry.TriggerName,
            entry.JobKey is null ? null : new JobKeyDto(entry.JobKey.Group, entry.JobKey.Name),
            entry.MisfiredAtUtc,
            entry.ScheduledFireTimeUtc);
    }

    public static ExecutionHistoryQuery AsExecutionHistoryQuery(this DashboardHistoryQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        return new ExecutionHistoryQuery
        {
            SchedulerName = query.SchedulerName,
            SchedulerInstanceId = query.SchedulerInstanceId,
            JobContains = query.JobFilter,
            TriggerContains = query.TriggerFilter,
            Skip = query.Skip,
            Take = query.Take,
            IncludeTotalCount = query.IncludeTotalCount
        };
    }

    public static DashboardHistoryQuery AsDashboardHistoryQuery(this ExecutionHistoryQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        return new DashboardHistoryQuery
        {
            SchedulerName = query.SchedulerName,
            SchedulerInstanceId = query.SchedulerInstanceId,
            JobFilter = query.JobContains,
            TriggerFilter = query.TriggerContains,
            Skip = query.Skip,
            Take = query.Take,
            IncludeTotalCount = query.IncludeTotalCount
        };
    }

    public static MisfireHistoryQuery AsMisfireHistoryQuery(this DashboardMisfireQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        return new MisfireHistoryQuery
        {
            SchedulerName = query.SchedulerName,
            SchedulerInstanceId = query.SchedulerInstanceId,
            TriggerContains = query.TriggerFilter,
            Skip = query.Skip,
            Take = query.Take,
            IncludeTotalCount = query.IncludeTotalCount
        };
    }

    public static DashboardMisfireQuery AsDashboardMisfireQuery(this MisfireHistoryQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        return new DashboardMisfireQuery
        {
            SchedulerName = query.SchedulerName,
            SchedulerInstanceId = query.SchedulerInstanceId,
            TriggerFilter = query.TriggerContains,
            Skip = query.Skip,
            Take = query.Take,
            IncludeTotalCount = query.IncludeTotalCount
        };
    }

    public static PagedResult<TTo> Map<TFrom, TTo>(PagedResult<TFrom> page, Func<TFrom, TTo> convert)
    {
        List<TTo> items = new(page.Items.Count);
        foreach (TFrom item in page.Items)
        {
            items.Add(convert(item));
        }

        return new PagedResult<TTo>(items, page.HasMore, page.TotalCount);
    }
}

/// <summary>
/// The dashboard's history seam, answered by the history Quartz keeps.
/// </summary>
/// <remarks>
/// What an application resolving <see cref="IDashboardHistoryStore" /> gets when it registered none of
/// its own, so the documented 4.0 type still resolves and still answers — over the one store the
/// recorder writes to, the HTTP API serves, and a store of an application's own replaces.
/// </remarks>
internal sealed class DashboardHistoryStoreOverExecutionHistory : IDashboardHistoryStore
{
    private readonly IExecutionHistoryStore inner;

    public DashboardHistoryStoreOverExecutionHistory(IExecutionHistoryStore inner)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public ValueTask AddExecution(DashboardHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        return inner.AddExecution(entry.AsExecutionHistoryEntry(), cancellationToken);
    }

    public async ValueTask<PagedResult<DashboardHistoryEntry>> QueryExecutions(DashboardHistoryQuery query, CancellationToken cancellationToken = default)
    {
        PagedResult<ExecutionHistoryEntry> page = await inner
            .QueryExecutions(query.AsExecutionHistoryQuery(), cancellationToken)
            .ConfigureAwait(false);

        return DashboardHistoryMapping.Map(page, static entry => entry.AsDashboardHistoryEntry());
    }

    public ValueTask AddMisfire(DashboardMisfireEntry entry, CancellationToken cancellationToken = default)
    {
        return inner.AddMisfire(entry.AsMisfireHistoryEntry(), cancellationToken);
    }

    public async ValueTask<PagedResult<DashboardMisfireEntry>> QueryMisfires(DashboardMisfireQuery query, CancellationToken cancellationToken = default)
    {
        PagedResult<MisfireHistoryEntry> page = await inner
            .QueryMisfires(query.AsMisfireHistoryQuery(), cancellationToken)
            .ConfigureAwait(false);

        return DashboardHistoryMapping.Map(page, static entry => entry.AsDashboardMisfireEntry());
    }

    public ValueTask<int> CountMisfires(string schedulerName, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        return inner.CountMisfires(schedulerName, since, cancellationToken);
    }
}

/// <summary>
/// Quartz's history seam, answered by a store the application registered against the dashboard's.
/// </summary>
/// <remarks>
/// The other direction, and the one that keeps the 4.0 recipe whole: an application that registered its
/// own <see cref="IDashboardHistoryStore" /> — to keep history in a database, say — has the recorder's
/// rows written into it and the HTTP API's history routes answered out of it, without changing a line.
/// </remarks>
internal sealed class ExecutionHistoryStoreOverDashboardStore : IExecutionHistoryStore
{
    private readonly IDashboardHistoryStore inner;

    public ExecutionHistoryStoreOverDashboardStore(IDashboardHistoryStore inner)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public ValueTask AddExecution(ExecutionHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return inner.AddExecution(entry.AsDashboardHistoryEntry(), cancellationToken);
    }

    public async ValueTask<PagedResult<ExecutionHistoryEntry>> QueryExecutions(ExecutionHistoryQuery query, CancellationToken cancellationToken = default)
    {
        PagedResult<DashboardHistoryEntry> page = await inner
            .QueryExecutions(query.AsDashboardHistoryQuery(), cancellationToken)
            .ConfigureAwait(false);

        return DashboardHistoryMapping.Map(page, static entry => entry.AsExecutionHistoryEntry());
    }

    public ValueTask AddMisfire(MisfireHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return inner.AddMisfire(entry.AsDashboardMisfireEntry(), cancellationToken);
    }

    public async ValueTask<PagedResult<MisfireHistoryEntry>> QueryMisfires(MisfireHistoryQuery query, CancellationToken cancellationToken = default)
    {
        PagedResult<DashboardMisfireEntry> page = await inner
            .QueryMisfires(query.AsDashboardMisfireQuery(), cancellationToken)
            .ConfigureAwait(false);

        return DashboardHistoryMapping.Map(page, static entry => entry.AsMisfireHistoryEntry());
    }

    public ValueTask<int> CountMisfires(string schedulerName, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        return inner.CountMisfires(schedulerName, since, cancellationToken);
    }
}
