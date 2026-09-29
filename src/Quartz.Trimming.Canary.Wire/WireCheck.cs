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

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;

namespace Quartz.Trimming.Canary.Wire;

/// <summary>
/// The steps, each one a round trip over the HTTP API through the <see cref="IScheduler" /> and the
/// <see cref="IExecutionHistoryStore" /> that <c>Quartz.HttpClient</c> registers.
/// </summary>
/// <remarks>
/// The steps run in order and stop at the first failure, because each one leans on the one before it:
/// there is no trigger to pause once scheduling has failed.
/// </remarks>
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

    /// <summary>
    /// How long the job may take to run, and its history row to be listed, once asked for.
    /// </summary>
    private const int PatienceSeconds = 60;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(PatienceSeconds);

    private static readonly TimeProvider Clock = TimeProvider.System;

    /// <summary>
    /// Runs every step, writing a line for each, and says whether all of them passed.
    /// </summary>
    public static async Task<bool> Run(IServiceProvider client, CancellationToken cancellationToken)
    {
        IScheduler scheduler = client.GetRequiredService<IScheduler>();
        IExecutionHistoryStore history = client.GetRequiredKeyedService<IExecutionHistoryStore>(Program.SchedulerName);

        (string Name, Func<Task<string>> Step)[] steps =
        [
            ("metadata", () => Metadata(scheduler, cancellationToken)),
            ("schedule", () => Schedule(scheduler, cancellationToken)),
            ("read-back", () => ReadBack(scheduler, cancellationToken)),
            ("not-found", () => NotFound(scheduler, cancellationToken)),
            ("pause", () => Pause(scheduler, cancellationToken)),
            ("resume", () => Resume(scheduler, cancellationToken)),
            ("trigger", () => Trigger(scheduler, cancellationToken)),
            ("history", () => History(history, cancellationToken)),
            ("delete", () => Delete(scheduler, cancellationToken)),
        ];

        foreach ((string name, Func<Task<string>> step) in steps)
        {
            try
            {
                string passed = await step().ConfigureAwait(false);
                Console.WriteLine($"PASS {name}: {passed}");
            }
            catch (CanaryFailedException failure)
            {
                Console.WriteLine($"FAIL {name}: {failure.Message}");
                return false;
            }
            catch (Exception e)
            {
                Console.WriteLine($"FAIL {name}: {e.GetType().FullName}: {e.Message}{Environment.NewLine}{e}");
                return false;
            }
        }

        return true;
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

        WireCanaryRun run;
        try
        {
            run = await WireCanaryJob.Ran.Task.WaitAsync(Patience, Clock, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new CanaryFailedException($"the job never ran within {PatienceSeconds} seconds, so the server could not resolve or build the type the request named.");
        }

        Expect(run.Payload == Payload, $"the job ran with the payload '{run.Payload}'.");
        Expect(run.FiredBy == FiredBy, $"the job ran with fired-by '{run.FiredBy}'.");

        return $"{JobKey} ran on the server, with the data it was scheduled with and the data it was triggered with";
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

    private static async Task<string> Delete(IScheduler scheduler, CancellationToken cancellationToken)
    {
        bool deleted = await scheduler.DeleteJob(JobKey, cancellationToken).ConfigureAwait(false);
        Expect(deleted, "the server says there was no job to delete.");

        Expect(await scheduler.GetJobDetail(JobKey, cancellationToken).ConfigureAwait(false) is null, "the job is still there after deleting it.");
        Expect(await scheduler.GetTrigger(TriggerKey, cancellationToken).ConfigureAwait(false) is null, "the trigger outlived its job.");

        return $"{JobKey} and its trigger are gone";
    }

    private static void Expect([DoesNotReturnIf(false)] bool condition, string failure)
    {
        if (!condition)
        {
            throw new CanaryFailedException(failure);
        }
    }

    /// <summary>
    /// A step's own check failing, as opposed to something it called throwing: the message is the whole
    /// story, and a stack trace would only say which line of this file noticed.
    /// </summary>
    /// <remarks>
    /// Private on purpose. It never leaves <see cref="Run" />, which catches it to write the step's line, and
    /// an executable has no caller that could catch it by type.
    /// </remarks>
    private sealed class CanaryFailedException(string message) : Exception(message);
}
