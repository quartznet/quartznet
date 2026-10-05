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

using System.Text.Json;

namespace Quartz.HttpApiContract;

/// <summary>
/// One finished execution on the wire.
/// </summary>
/// <remarks>
/// The scheduler's own name is not repeated: the route names the scheduler, and every row in the answer
/// belongs to it. The <em>node's</em> id is repeated on every row, because a cluster's history is
/// several nodes' and a reader has to be able to tell them apart.
/// </remarks>
internal sealed record ExecutionHistoryEntryDto(
    string SchedulerInstanceId,
    string JobGroup,
    string JobName,
    string TriggerGroup,
    string TriggerName,
    DateTimeOffset FiredAtUtc,
    TimeSpan Duration,
    bool Succeeded,
    string? ExceptionMessage)
{
    /// <summary>
    /// Which attempt at the occurrence this was, and whether another one was scheduled.
    /// </summary>
    /// <remarks>
    /// Non-positional, so the constructor is unchanged: a host that predates them sends neither, and
    /// they read back as <c>0</c> and <see langword="false" /> — which is what an execution with no
    /// retry policy behind it is anyway.
    /// </remarks>
    public int RetryAttempt { get; init; }

    /// <inheritdoc cref="RetryAttempt" />
    public bool RetryScheduled { get; init; }

    /// <summary>
    /// The row's key, which <c>GET …/history/executions/{entryId}</c> reads it back by.
    /// </summary>
    /// <remarks>
    /// Non-positional, as the two above are: a 4.2 host sends none, and its rows cannot be asked for one
    /// at a time.
    /// </remarks>
    public string? EntryId { get; init; }

    /// <summary>
    /// The lines the job logged, on the single-entry route only: the listing leaves every row's log out,
    /// so a page of history costs what it did before capture existed.
    /// </summary>
    public string? Log { get; init; }

    /// <summary>
    /// What the run achieved, or <see langword="null" /> on a row written before 4.4.
    /// </summary>
    /// <remarks>
    /// This and the four below are non-positional, as the ones above are: a 4.3 host sends none of them,
    /// and a 4.3 reader skips them.
    /// </remarks>
    public JobRunResult? Result { get; init; }

    /// <summary>
    /// The job's own one line about the run.
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    /// What the run measured, as the JSON object the history keeps, rather than as a string holding one.
    /// </summary>
    /// <remarks>
    /// A row whose kept text is not a JSON object — a store of an application's own may keep anything —
    /// travels without it rather than failing the page it is on.
    /// </remarks>
    public JsonElement? Metrics { get; init; }

    /// <summary>
    /// Whether the run was asked for with <c>TriggerJob</c> rather than fired by a schedule.
    /// </summary>
    public bool Manual { get; init; }

    /// <summary>
    /// The firing's fire instance id, which links the row to the firing's span and log scope.
    /// </summary>
    public string? FireInstanceId { get; init; }

    /// <summary>
    /// The input the run was given, on the single-entry route only, as <see cref="Log" /> is: what
    /// <em>Run again</em> fetches when it is asked for, so a page of history does not carry every row's.
    /// </summary>
    /// <remarks>
    /// This and the flag below are non-positional, as the members above are: a 4.3 host sends neither, so
    /// a run read from it has no input to pass back, and a 4.3 reader skips them.
    /// </remarks>
    public string? Input { get; init; }

    /// <summary>
    /// Whether the run had an input over the history's cap, which was then not recorded.
    /// </summary>
    public bool InputTooLarge { get; init; }

    /// <param name="entry">The row.</param>
    /// <param name="includeDetails">
    /// Whether the captured log and the recorded input go on the wire — the single-entry route's answer,
    /// and no listing's.
    /// </param>
    public static ExecutionHistoryEntryDto Create(ExecutionHistoryEntry entry, bool includeDetails = false)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new ExecutionHistoryEntryDto(
            SchedulerInstanceId: entry.SchedulerInstanceId,
            JobGroup: entry.JobGroup,
            JobName: entry.JobName,
            TriggerGroup: entry.TriggerGroup,
            TriggerName: entry.TriggerName,
            FiredAtUtc: entry.FiredAtUtc,
            Duration: entry.Duration,
            Succeeded: entry.Succeeded,
            ExceptionMessage: entry.ExceptionMessage
        )
        {
            RetryAttempt = entry.RetryAttempt,
            RetryScheduled = entry.RetryScheduled,
            EntryId = entry.EntryId,
            Log = includeDetails ? entry.Log : null,
            Result = entry.Result,
            Summary = entry.Summary,
            Metrics = MetricsObject(entry.MetricsJson),
            Manual = entry.Manual,
            FireInstanceId = entry.FireInstanceId,
            Input = includeDetails ? entry.Input : null,
            InputTooLarge = entry.InputTooLarge
        };
    }

    /// <summary>
    /// The kept metrics as a JSON object, or <see langword="null" /> when there are none or the text is
    /// not one.
    /// </summary>
    private static JsonElement? MetricsObject(string? metricsJson)
    {
        if (string.IsNullOrWhiteSpace(metricsJson))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(metricsJson);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <param name="schedulerName">
    /// The scheduler the row belongs to, which the reader knows from the route it asked on.
    /// </param>
    public ExecutionHistoryEntry AsExecutionHistoryEntry(string schedulerName)
    {
        return new ExecutionHistoryEntry(
            schedulerName,
            SchedulerInstanceId,
            JobGroup,
            JobName,
            TriggerGroup,
            TriggerName,
            FiredAtUtc,
            Duration,
            Succeeded,
            ExceptionMessage
        )
        {
            RetryAttempt = RetryAttempt,
            RetryScheduled = RetryScheduled,
            EntryId = EntryId,
            Log = Log,
            Result = Result,
            Summary = Summary,

            // The object's text as it arrived, which is the text the recorder wrote: the server writes
            // the object back with the encoder the recorder used, so nothing is re-spelled.
            MetricsJson = Metrics is { ValueKind: JsonValueKind.Object } metrics ? metrics.GetRawText() : null,
            Manual = Manual,
            FireInstanceId = FireInstanceId,
            Input = Input,
            InputTooLarge = InputTooLarge
        };
    }
}

/// <summary>
/// One firing that was missed, on the wire.
/// </summary>
/// <remarks>
/// <inheritdoc cref="ExecutionHistoryEntryDto" path="/remarks" />
/// </remarks>
internal sealed record MisfireHistoryEntryDto(
    string SchedulerInstanceId,
    string TriggerGroup,
    string TriggerName,
    KeyDto? JobKey,
    DateTimeOffset MisfiredAtUtc,
    DateTimeOffset? ScheduledFireTimeUtc,
    MisfireReason Reason = MisfireReason.Missed)
{
    public static MisfireHistoryEntryDto Create(MisfireHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new MisfireHistoryEntryDto(
            SchedulerInstanceId: entry.SchedulerInstanceId,
            TriggerGroup: entry.TriggerGroup,
            TriggerName: entry.TriggerName,
            JobKey: entry.JobKey is null ? null : KeyDto.Create(entry.JobKey),
            MisfiredAtUtc: entry.MisfiredAtUtc,
            ScheduledFireTimeUtc: entry.ScheduledFireTimeUtc,
            Reason: entry.Reason
        );
    }

    /// <inheritdoc cref="ExecutionHistoryEntryDto.AsExecutionHistoryEntry" />
    public MisfireHistoryEntry AsMisfireHistoryEntry(string schedulerName)
    {
        return new MisfireHistoryEntry(
            schedulerName,
            SchedulerInstanceId,
            TriggerGroup,
            TriggerName,
            JobKey?.AsJobKey(),
            MisfiredAtUtc,
            ScheduledFireTimeUtc
        )
        {
            // A body from a host that predates the reason carries none, which is what its rows were.
            Reason = Reason
        };
    }
}

/// <summary>
/// One job's run status on the wire: <see cref="JobRunStatus" /> without the scheduler's name, which the
/// route says.
/// </summary>
/// <remarks>
/// <para>
/// Positional throughout: the type is internal, so its constructor is no public signature to keep
/// additive, and the parameters are the JSON members in the order they are written. A body that lacks
/// one — a member added after the reader was built, read by a reader that predates it — binds its
/// default, and a member the reader does not know is skipped.
/// </para>
/// <para>
/// Each member is <see cref="JobRunStatus" />'s of the same name.
/// </para>
/// </remarks>
internal sealed record JobRunStatusDto(
    KeyDto Job,
    DateTimeOffset LastFiredAtUtc,
    JobRunResult LastResult,
    TimeSpan LastDuration,
    string? LastSchedulerInstanceId,
    string? LastEntryId,
    string? LastSummary,
    DateTimeOffset? LastSucceededAtUtc,
    DateTimeOffset? LastFailedAtUtc,
    string? LastFailureMessage,
    int ConsecutiveFailures,
    long RunCount,
    long FailureCount,
    DateTimeOffset FirstFiredAtUtc)
{
    public static JobRunStatusDto Create(JobRunStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return new JobRunStatusDto(
            KeyDto.Create(status.Job),
            status.LastFiredAtUtc,
            status.LastResult,
            status.LastDuration,
            status.LastSchedulerInstanceId,
            status.LastEntryId,
            status.LastSummary,
            status.LastSucceededAtUtc,
            status.LastFailedAtUtc,
            status.LastFailureMessage,
            status.ConsecutiveFailures,
            status.RunCount,
            status.FailureCount,
            status.FirstFiredAtUtc);
    }

    /// <inheritdoc cref="ExecutionHistoryEntryDto.AsExecutionHistoryEntry" />
    public JobRunStatus AsJobRunStatus(string schedulerName)
    {
        return new JobRunStatus(schedulerName, Job.AsJobKey(), LastFiredAtUtc, LastResult)
        {
            LastDuration = LastDuration,
            LastSchedulerInstanceId = LastSchedulerInstanceId,
            LastEntryId = LastEntryId,
            LastSummary = LastSummary,
            LastSucceededAtUtc = LastSucceededAtUtc,
            LastFailedAtUtc = LastFailedAtUtc,
            LastFailureMessage = LastFailureMessage,
            ConsecutiveFailures = ConsecutiveFailures,
            RunCount = RunCount,
            FailureCount = FailureCount,
            FirstFiredAtUtc = FirstFiredAtUtc
        };
    }
}

/// <summary>
/// A scheduler's runs over time on the wire: <see cref="ExecutionStatistics" />.
/// </summary>
/// <remarks>
/// Positional, as <see cref="JobRunStatusDto" /> is and for its reason: a member added later binds its
/// default in a reader that predates it, and a member the reader does not know is skipped.
/// </remarks>
internal sealed record ExecutionStatisticsDto(
    TimeSpan BucketSize,
    ExecutionStatisticsBucketDto[] Buckets,
    bool Truncated)
{
    public static ExecutionStatisticsDto Create(ExecutionStatistics statistics)
    {
        ArgumentNullException.ThrowIfNull(statistics);

        return new ExecutionStatisticsDto(
            statistics.BucketSize,
            statistics.Buckets.Select(ExecutionStatisticsBucketDto.Create).ToArray(),
            statistics.Truncated);
    }

    public ExecutionStatistics AsExecutionStatistics()
    {
        return new ExecutionStatistics
        {
            BucketSize = BucketSize,
            Buckets = (Buckets ?? []).Select(static bucket => bucket.AsExecutionStatisticsBucket()).ToList(),
            Truncated = Truncated
        };
    }
}

/// <summary>
/// One bucket of <see cref="ExecutionStatisticsDto" />: <see cref="ExecutionStatisticsBucket" />, with
/// <c>runCount</c> written out for a reader that does not add the four counts itself.
/// </summary>
/// <remarks>
/// A result a later version adds is counted in <c>runCount</c> and in a member of its own, which a reader
/// that predates it skips.
/// </remarks>
internal sealed record ExecutionStatisticsBucketDto(
    DateTimeOffset StartUtc,
    long RunCount,
    long SucceededCount,
    long FailedCount,
    long CancelledCount,
    long SkippedCount,
    TimeSpan P50Duration,
    TimeSpan P95Duration,
    TimeSpan MaxDuration)
{
    public static ExecutionStatisticsBucketDto Create(ExecutionStatisticsBucket bucket)
    {
        ArgumentNullException.ThrowIfNull(bucket);

        return new ExecutionStatisticsBucketDto(
            bucket.StartUtc,
            bucket.RunCount,
            bucket.SucceededCount,
            bucket.FailedCount,
            bucket.CancelledCount,
            bucket.SkippedCount,
            bucket.P50Duration,
            bucket.P95Duration,
            bucket.MaxDuration);
    }

    public ExecutionStatisticsBucket AsExecutionStatisticsBucket()
    {
        return new ExecutionStatisticsBucket(StartUtc)
        {
            SucceededCount = SucceededCount,
            FailedCount = FailedCount,
            CancelledCount = CancelledCount,
            SkippedCount = SkippedCount,
            P50Duration = P50Duration,
            P95Duration = P95Duration,
            MaxDuration = MaxDuration
        };
    }
}

/// <summary>
/// How many misfires a scheduler has recorded since an instant.
/// </summary>
/// <remarks>
/// A body rather than a bare number, so the answer can gain a member — a window, a breakdown by node —
/// without every reader of it breaking.
/// </remarks>
internal sealed record MisfireCountResponse(int Count);
