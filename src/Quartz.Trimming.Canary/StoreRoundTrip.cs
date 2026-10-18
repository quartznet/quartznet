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

using Microsoft.Data.Sqlite;

namespace Quartz.Trimming.Canary;

/// <summary>
/// What every store check ends with, over whichever store it set up: a job scheduled with a job data map,
/// fired, and read back with its trigger through <see cref="IScheduler" />. And the temporary SQLite file
/// each one runs on.
/// </summary>
/// <remarks>
/// The check keeps what is its own: how the store reaches the database, and what it asserts about that
/// before and after. This is the part that is the same for all of them, and a failure here names the check
/// that called it.
/// </remarks>
internal static class StoreRoundTrip
{
    /// <summary>
    /// A path in the temporary directory for one check's database, unique to this run.
    /// </summary>
    public static string NewDatabaseFile(string check) => Path.Combine(Path.GetTempPath(), $"quartz-canary-{check}-{Guid.NewGuid():N}.db");

    /// <summary>
    /// Schedules <see cref="RoundTripJob" /> on a started scheduler, waits for it to fire, and reads the job
    /// and its trigger back, returning <see langword="null" /> when all of that worked and a <c>FAIL</c>
    /// line naming <paramref name="check" /> when it did not.
    /// </summary>
    public static async Task<string?> Run(IScheduler scheduler, string check, string payload)
    {
        JobKey jobKey = new(check, "store");
        TriggerKey triggerKey = new(check, "store");
        Task fired = RoundTripJob.WhenFired(jobKey);

        await scheduler.ScheduleJob(
            JobBuilder.Create<RoundTripJob>()
                .WithIdentity(jobKey)
                .UsingJobData("payload", payload)
                .Build(),
            TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .StartNow()
                // Repeating, so that reading the trigger back afterwards reads a row rather than finding the
                // one a completed one-shot trigger took with it.
                .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
                .Build()).ConfigureAwait(false);

        // Signalled by the job itself. A sleep would pass on a machine slow enough to make it meaningless,
        // and fail on one that is merely busy.
        Task completed = await Task.WhenAny(fired, Task.Delay(TimeSpan.FromSeconds(60))).ConfigureAwait(false);
        if (completed != fired)
        {
            return $"FAIL {check}: the job never fired within a minute, so the store never handed a trigger to the scheduler.";
        }

        IJobDetail? job = await scheduler.GetJobDetail(jobKey).ConfigureAwait(false);
        if (job?.JobDataMap.GetString("payload") != payload)
        {
            return $"FAIL {check}: the job came back as {(job is null ? "nothing" : $"one carrying '{job.JobDataMap.GetString("payload")}'")}.";
        }

        ITrigger? trigger = await scheduler.GetTrigger(triggerKey).ConfigureAwait(false);
        if (trigger is null || !Equals(trigger.JobKey, jobKey))
        {
            return $"FAIL {check}: the trigger came back as {(trigger is null ? "nothing" : $"one pointing at '{trigger.JobKey}'")}.";
        }

        return null;
    }

    /// <summary>
    /// Lets go of a check's database and deletes the file.
    /// </summary>
    public static void DeleteDatabase(string databaseFile)
    {
        SqliteConnection.ClearAllPools();

        try
        {
            File.Delete(databaseFile);
        }
        catch (IOException)
        {
            // A leftover temporary file is not a failure of anything a check asserts.
        }
    }

    /// <summary>
    /// Signals that the store handed it to the scheduler. Its type comes back out of <c>JOB_CLASS_NAME</c>
    /// by name, and one type serves every check: each waits on its own job key.
    /// </summary>
    public sealed class RoundTripJob : IJob
    {
        private static readonly ConcurrentDictionary<JobKey, TaskCompletionSource> firings = new();

        internal static Task WhenFired(JobKey jobKey) =>
            firings.GetOrAdd(jobKey, static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            if (firings.TryGetValue(context.JobDetail.Key, out TaskCompletionSource? firing))
            {
                firing.TrySetResult();
            }

            return default;
        }
    }
}
