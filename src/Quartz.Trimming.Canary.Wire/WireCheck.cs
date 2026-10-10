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

using System.Collections.Concurrent;
using System.Globalization;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;
using Quartz.HttpApiContract;

using static Quartz.Trimming.Canary.Wire.CanarySteps;

namespace Quartz.Trimming.Canary.Wire;

/// <summary>
/// The steps, each one a round trip over the HTTP API through the <see cref="IScheduler" />, the
/// <see cref="IExecutionHistoryStore" /> and the <see cref="ISchedulerEventSource" /> that
/// <c>Quartz.HttpClient</c> registers.
/// </summary>
internal static class WireCheck
{
    private static readonly JobKey JobKey = new("wire", "canary");
    private static readonly TriggerKey TriggerKey = new("wire", "canary");

    /// <summary>
    /// Noon on the first of January 2099, so the trigger fires only when the client says so.
    /// </summary>
    private const string Cron = "0 0 12 1 1 ? 2099";

    private static readonly DateTimeOffset FirstFireTime = new(2099, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private const string Payload = "the job data map crosses the wire in both directions";

    private const string FiredBy = "Quartz.HttpClient";

    private static readonly TimeProvider Clock = TimeProvider.System;

    /// <summary>
    /// Runs every step, writing a line for each, and says whether all of them passed.
    /// </summary>
    /// <param name="client">The client of the scheduler the host serves.</param>
    /// <param name="newerHost">A client of the canned answers <see cref="NewerHost" /> serves.</param>
    /// <param name="cancellationToken">Bounds the whole run.</param>
    public static async Task<bool> Run(IServiceProvider client, IServiceProvider newerHost, CancellationToken cancellationToken)
    {
        IScheduler scheduler = client.GetRequiredService<IScheduler>();
        IExecutionHistoryStore history = client.GetRequiredKeyedService<IExecutionHistoryStore>(Program.SchedulerName);
        ISchedulerEventSource events = client.GetRequiredKeyedService<ISchedulerEventSource>(Program.SchedulerName);

        using EventSubscription subscription = new(events, cancellationToken);

        (string Name, Func<Task<string>> Step)[] steps =
        [
            ("metadata", () => Metadata(scheduler, cancellationToken)),
            ("schedule", () => Schedule(scheduler, cancellationToken)),
            ("read-back", () => ReadBack(scheduler, cancellationToken)),
            ("not-found", () => NotFound(scheduler, cancellationToken)),
            ("events-open", () => OpenEvents(scheduler, subscription, cancellationToken)),
            ("pause", () => Pause(scheduler, cancellationToken)),
            ("resume", () => Resume(scheduler, cancellationToken)),
            ("trigger", () => Trigger(scheduler, cancellationToken)),
            ("events", () => Events(subscription, cancellationToken)),
            ("history", () => History(history, cancellationToken)),
            ("statistics", () => Statistics(history, cancellationToken)),
            ("delete", () => Delete(scheduler, cancellationToken)),
            ("newer-host", () => NewerHostListing(newerHost.GetRequiredService<IScheduler>(), cancellationToken)),
        ];

        return await CanarySteps.Run("wire", steps).ConfigureAwait(false);
    }

    private static async Task<string> Metadata(IScheduler scheduler, CancellationToken cancellationToken)
    {
        SchedulerMetadata metadata = await scheduler.GetMetadata(cancellationToken).ConfigureAwait(false);

        Expect(metadata.SchedulerName == Program.SchedulerName, $"the scheduler answered as '{metadata.SchedulerName}'.");
        Expect(metadata.Status == SchedulerStatus.Running, $"the scheduler is {metadata.Status}, not running.");

        return $"'{metadata.SchedulerName}' is {metadata.Status} on {metadata.JobStoreTypeName}";
    }

    private static async Task<string> Schedule(IScheduler scheduler, CancellationToken cancellationToken)
    {
        IJobDetail job = JobBuilder.Create<WireCanaryJob>()
            .WithIdentity(JobKey)
            .UsingJobData(WireCanaryRun.PayloadKey, Payload)
            .Build();

        ITrigger trigger = TriggerBuilder.Create(Clock)
            .WithIdentity(TriggerKey)
            .WithSchedule(CronScheduleBuilder.Create(Cron).InTimeZone(TimeZoneInfo.Utc))
            .Build();

        DateTimeOffset firstFireTime = await scheduler.ScheduleJob(job, trigger, cancellationToken: cancellationToken).ConfigureAwait(false);

        Expect(firstFireTime == FirstFireTime, $"the server says the trigger first fires at {firstFireTime:O}, not {FirstFireTime:O}.");

        return $"{JobKey} with a cron trigger, first firing at {firstFireTime:O}";
    }

    private static async Task<string> ReadBack(IScheduler scheduler, CancellationToken cancellationToken)
    {
        IJobDetail? job = await scheduler.GetJobDetail(JobKey, cancellationToken).ConfigureAwait(false);
        Expect(job is not null, "the job could not be read back.");

        string jobType = new JobType(typeof(WireCanaryJob)).FullName;
        Expect(job.JobType.FullName == jobType, $"the job came back as '{job.JobType.FullName}', not '{jobType}'.");
        Expect(job.JobDataMap.GetString(WireCanaryRun.PayloadKey) == Payload, $"the job data came back as '{job.JobDataMap.GetString(WireCanaryRun.PayloadKey)}'.");

        ITrigger? trigger = await scheduler.GetTrigger(TriggerKey, cancellationToken).ConfigureAwait(false);
        if (trigger is not ICronTrigger cron)
        {
            throw new CanaryFailedException($"the trigger came back as '{trigger?.GetType().FullName ?? "null"}', not a cron trigger.");
        }

        Expect(cron.CronExpressionString == Cron, $"the trigger came back with the expression '{cron.CronExpressionString}'.");
        Expect(Equals(cron.JobKey, JobKey), $"the trigger came back pointing at '{cron.JobKey}'.");
        Expect(cron.NextFireTimeUtc == FirstFireTime, $"the trigger came back firing next at {cron.NextFireTimeUtc?.ToString("O", CultureInfo.InvariantCulture)}.");

        List<JobKey> jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(JobKey.Group), cancellationToken).ConfigureAwait(false);
        Expect(jobKeys.Contains(JobKey), $"the job is missing from its group's listing, which holds {jobKeys.Count} key(s).");

        return $"the job as {job.JobType.FullName} with its data, its cron trigger, and the job in its group's listing";
    }

    private static async Task<string> NotFound(IScheduler scheduler, CancellationToken cancellationToken)
    {
        JobKey absent = new("absent", JobKey.Group);
        IJobDetail? job = await scheduler.GetJobDetail(absent, cancellationToken).ConfigureAwait(false);

        // Null only when the 404 carried problem details the client could read: without them the client
        // throws what HttpClient throws for any failure status.
        Expect(job is null, $"a job that was never scheduled came back as {job?.Key}.");

        return $"{absent} reads back as null, out of a 404 with problem details";
    }

    /// <summary>
    /// Opens the client's subscription to the scheduler's event stream and waits until it is live: a
    /// pause and a resume until the resume's event arrives. The route subscribes before it writes its
    /// first frame, so an event raised once the stream has answered is never missed — but the reader
    /// opens the stream on its own time, and nothing outside it can see when that was.
    /// </summary>
    private static async Task<string> OpenEvents(IScheduler scheduler, EventSubscription subscription, CancellationToken cancellationToken)
    {
        subscription.Start();

        long started = Clock.GetTimestamp();
        int probes = 0;
        while (!subscription.Received.Any(static e => e.Kind == SchedulerEventKind.TriggerResumed))
        {
            Expect(Clock.GetElapsedTime(started) < Patience, $"no event arrived over the stream within {PatienceSeconds} seconds, after {probes} pause-and-resume probes.");

            probes++;
            await scheduler.PauseTrigger(TriggerKey, cancellationToken).ConfigureAwait(false);
            await scheduler.ResumeTrigger(TriggerKey, cancellationToken).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromMilliseconds(100), Clock, cancellationToken).ConfigureAwait(false);
        }

        SchedulerEvent first = subscription.Received.First(static e => e.Kind == SchedulerEventKind.TriggerResumed);
        Expect(first.SchedulerName == Program.SchedulerName, $"the event names the scheduler '{first.SchedulerName}'.");
        Expect(first.TriggerKey is { } key && key.Name == TriggerKey.Name && key.Group == TriggerKey.Group, $"the event is about the trigger '{first.TriggerKey?.Group}.{first.TriggerKey?.Name}'.");

        return $"the server-sent event stream is live: a {first.Kind} for {TriggerKey} arrived after {probes} probe(s)";
    }

    private static async Task<string> Pause(IScheduler scheduler, CancellationToken cancellationToken)
    {
        bool applied = await scheduler.PauseTrigger(TriggerKey, cancellationToken).ConfigureAwait(false);
        Expect(applied, "the server says pausing the trigger changed nothing.");

        TriggerState state = await scheduler.GetTriggerState(TriggerKey, cancellationToken).ConfigureAwait(false);
        Expect(state == TriggerState.Paused, $"the trigger is {state} after pausing it.");

        return $"{TriggerKey} is {state}";
    }

    private static async Task<string> Resume(IScheduler scheduler, CancellationToken cancellationToken)
    {
        bool applied = await scheduler.ResumeTrigger(TriggerKey, cancellationToken).ConfigureAwait(false);
        Expect(applied, "the server says resuming the trigger changed nothing.");

        TriggerState state = await scheduler.GetTriggerState(TriggerKey, cancellationToken).ConfigureAwait(false);
        Expect(state == TriggerState.Normal, $"the trigger is {state} after resuming it.");

        return $"{TriggerKey} is {state}";
    }

    private static async Task<string> Trigger(IScheduler scheduler, CancellationToken cancellationToken)
    {
        await scheduler.TriggerJob(JobKey, new JobDataMap { { WireCanaryRun.FiredByKey, FiredBy } }, cancellationToken).ConfigureAwait(false);

        WireCanaryRun run = await Await(WireCanaryJob.Ran.Task,
            $"the job never ran within {PatienceSeconds} seconds, so the server could not resolve or build the type the request named.",
            cancellationToken).ConfigureAwait(false);

        Expect(run.Payload == Payload, $"the job ran with the payload '{run.Payload}'.");
        Expect(run.FiredBy == FiredBy, $"the job ran with fired-by '{run.FiredBy}'.");

        return $"{JobKey} ran on the server, with the data it was scheduled with and the data it was triggered with";
    }

    /// <summary>
    /// What the stream carried while the steps above ran: the pause, the resume and the execution, each
    /// read out of a server-sent event frame through the contract's generated metadata.
    /// </summary>
    private static async Task<string> Events(EventSubscription subscription, CancellationToken cancellationToken)
    {
        SchedulerEvent executed = await Await(subscription.JobExecuted,
            $"the job ran, and no JobExecuted event for it arrived over the stream within {PatienceSeconds} seconds.",
            cancellationToken).ConfigureAwait(false);

        Expect(executed.SchedulerName == Program.SchedulerName, $"the event names the scheduler '{executed.SchedulerName}'.");
        Expect(executed.Vetoed != true, "the event says the job was vetoed.");
        Expect(executed.ExceptionMessage is null, $"the event says the job threw: {executed.ExceptionMessage}");
        Expect(!string.IsNullOrEmpty(executed.FireInstanceId), "the event names no firing.");
        Expect(!string.IsNullOrEmpty(executed.SchedulerInstanceId), "the event names no node.");

        foreach (SchedulerEventKind kind in new[] { SchedulerEventKind.TriggerPaused, SchedulerEventKind.TriggerResumed, SchedulerEventKind.TriggerFired, SchedulerEventKind.JobExecuting })
        {
            Expect(subscription.Received.Any(e => e.Kind == kind), $"no {kind} arrived over the stream, though the steps before this one caused one.");
        }

        subscription.Stop();

        return $"the stream carried the pause, the resume, the firing and the {executed.Kind} of {JobKey}, {subscription.Received.Count} events in all";
    }

    private static async Task<string> History(IExecutionHistoryStore history, CancellationToken cancellationToken)
    {
        ExecutionHistoryQuery query = new() { SchedulerName = Program.SchedulerName, JobContains = JobKey.Name };

        // The row is written once the job has returned, which is a moment after it signalled.
        long started = Clock.GetTimestamp();
        ExecutionHistoryEntry? entry = null;
        while (entry is null)
        {
            PagedResult<ExecutionHistoryEntry> page = await history.QueryExecutions(query, cancellationToken).ConfigureAwait(false);
            entry = page.Items.FirstOrDefault(x => x.JobGroup == JobKey.Group && x.JobName == JobKey.Name);

            if (entry is null)
            {
                Expect(Clock.GetElapsedTime(started) < Patience, $"no execution of {JobKey} was listed within {PatienceSeconds} seconds of it running.");
                await Task.Delay(TimeSpan.FromMilliseconds(100), Clock, cancellationToken).ConfigureAwait(false);
            }
        }

        Expect(entry.Succeeded, $"the execution is recorded as failed: {entry.ExceptionMessage}");
        Expect(entry.EntryId is not null, "the execution was listed without an entry id to read it back by.");

        ExecutionHistoryEntry? single = await history.GetExecution(Program.SchedulerName, entry.EntryId, cancellationToken).ConfigureAwait(false);
        Expect(single is not null, $"the execution {entry.EntryId} could not be read back on its own.");
        Expect(single.Log?.Contains(WireCanaryJob.LogLine, StringComparison.Ordinal) == true, $"the execution came back with the log '{single.Log}'.");

        return $"{JobKey} is listed as succeeded, and read back on its own with the line it logged";
    }

    private static async Task<string> Statistics(IExecutionHistoryStore history, CancellationToken cancellationToken)
    {
        ExecutionStatistics statistics = await history.QueryExecutionStatistics(new ExecutionStatisticsQuery
        {
            SchedulerName = Program.SchedulerName,
            Job = JobKey,
            BucketSize = TimeSpan.FromDays(1)
        }, cancellationToken).ConfigureAwait(false);

        long runs = statistics.Buckets.Sum(bucket => bucket.RunCount);
        Expect(runs >= 1, $"no run of {JobKey} was counted, though the history lists one.");
        Expect(statistics.Buckets.Sum(bucket => bucket.SucceededCount) == runs, "a run of the canary job was counted as anything but a success.");

        return $"{JobKey} is counted as {runs} successful run(s) in buckets of a day";
    }

    private static async Task<string> Delete(IScheduler scheduler, CancellationToken cancellationToken)
    {
        bool deleted = await scheduler.DeleteJob(JobKey, cancellationToken).ConfigureAwait(false);
        Expect(deleted, "the server says there was no job to delete.");

        Expect(await scheduler.GetJobDetail(JobKey, cancellationToken).ConfigureAwait(false) is null, "the job is still there after deleting it.");
        Expect(await scheduler.GetTrigger(TriggerKey, cancellationToken).ConfigureAwait(false) is null, "the trigger outlived its job.");

        return $"{JobKey} and its trigger are gone";
    }

    /// <summary>
    /// A listing from a host newer than this client, carrying names this client has no member for.
    /// </summary>
    private static async Task<string> NewerHostListing(IScheduler scheduler, CancellationToken cancellationToken)
    {
        PagedResult<TriggerHeader> page = await scheduler.QueryTriggers(new TriggerQuery { Take = 10 }, cancellationToken).ConfigureAwait(false);

        Expect(page.Items.Count == 1 && page.Items[0].Key.Name == "known",
            $"the listing came back with {page.Items.Count} trigger(s): {string.Join(", ", page.Items.Select(x => x.Key))}; only the trigger in a state this client knows belongs.");
        Expect(page.Items[0].OverlapPolicy == OverlapPolicy.Default,
            $"an overlap policy this client does not know came back as {page.Items[0].OverlapPolicy}, not Default.");
        Expect(page.TotalCount == 2, $"the host counted 2 triggers, and the listing says {page.TotalCount}.");

        return "a trigger in the state 'Hibernating' is left out, and the overlap policy 'Staggered' reads as Default";
    }

    /// <summary>
    /// One subscription to the scheduler's events through <c>Quartz.HttpClient</c>'s reader, pulled on a
    /// task of its own so the steps can go on while it reads, and remembering everything it was handed.
    /// </summary>
    private sealed class EventSubscription(ISchedulerEventSource source, CancellationToken cancellationToken) : IDisposable
    {
        private readonly CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        private readonly ConcurrentQueue<SchedulerEvent> received = new();
        private readonly TaskCompletionSource<SchedulerEvent> jobExecuted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task? reading;

        /// <summary>Every event the stream delivered, in order.</summary>
        public ConcurrentQueue<SchedulerEvent> Received => received;

        /// <summary>The first <see cref="SchedulerEventKind.JobExecuted" /> for the canary's job.</summary>
        public Task<SchedulerEvent> JobExecuted => jobExecuted.Task;

        public void Start()
        {
            reading ??= Task.Run(Read, stop.Token);
        }

        private async Task Read()
        {
            await foreach (SchedulerEvent schedulerEvent in source.Subscribe(Program.SchedulerName, stop.Token).ConfigureAwait(false))
            {
                received.Enqueue(schedulerEvent);

                if (schedulerEvent is { Kind: SchedulerEventKind.JobExecuted, JobKey: { } key } && key.Name == JobKey.Name && key.Group == JobKey.Group)
                {
                    jobExecuted.TrySetResult(schedulerEvent);
                }
            }
        }

        public void Stop()
        {
            stop.Cancel();
        }

        public void Dispose()
        {
            stop.Cancel();
            stop.Dispose();
        }
    }
}
