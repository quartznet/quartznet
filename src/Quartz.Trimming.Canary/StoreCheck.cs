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

using System.Runtime.CompilerServices;

using Microsoft.Data.Sqlite;

using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Trimming.Canary;

/// <summary>
/// A whole persistent job store, out of a trimmed or natively compiled publish: a real SQLite database
/// with the shipped schema on it, a job scheduled, fired, and read back through
/// <see cref="IScheduler" />.
/// </summary>
/// <remarks>
/// <para>
/// The store is the part of Quartz that a trimmed application could not reach at all before issue
/// #3429. Naming a driver resolves its connection and command types with <c>Type.GetType</c> and then
/// constructs them, and a trimmer that cannot see the call removes the constructor behind it — a
/// <c>TrimMode=full</c> publish of <c>UseSqlite(connectionString)</c> died with "Cannot instantiate
/// type which has no empty constructor". So this registers the database the way a trimmed application
/// is meant to: <c>UseSqlite(SqliteFactory.Instance, …)</c>, which names nothing.
/// </para>
/// <para>
/// Every step here is one a compile cannot stand in for. The schema validation on start reads the
/// database; scheduling writes a job data map and a trigger as blobs, which is where step 6's
/// serializer fix is exercised against a real column; firing goes through acquisition, the fire
/// instance and the job factory, which is where the job's type is resolved from the
/// <c>JOB_CLASS_NAME</c> string; and reading the job and trigger back comes the whole way out again.
/// </para>
/// </remarks>
internal static class StoreCheck
{
    private static readonly TaskCompletionSource fired = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Runs the check, returning <see langword="null" /> when it passed and a message when it did not.
    /// </summary>
    public static async Task<string?> Run()
    {
        string databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-canary-{Guid.NewGuid():N}.db");
        string connectionString = $"Data Source={databaseFile}";

        try
        {
            await CreateSchema(connectionString).ConfigureAwait(false);

            ServiceCollection services = new();
            services.AddQuartz(q =>
            {
                q.ConfigureScheduler(options =>
                {
                    options.InstanceName = "Canary";
                    options.InstanceId = "one";
                });

                q.UsePersistentStore(store =>
                {
                    // The registration this whole check exists for: the driver's own factory, so nothing
                    // is resolved from a type name and nothing is constructed by reflection.
                    store.UseSqlite(SqliteFactory.Instance, connectionString);
                    store.ConfigureStore(options => options.PerformSchemaValidation = true);
                });
            });

            ServiceProvider container = services.BuildServiceProvider();
            await using ConfiguredAsyncDisposable containerDisposal = container.ConfigureAwait(false);

            IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler().ConfigureAwait(false);

            JobKey jobKey = new("canary", "store");
            TriggerKey triggerKey = new("canary", "store");

            await scheduler.ScheduleJob(
                JobBuilder.Create<CanaryJob>()
                    .WithIdentity(jobKey)
                    .UsingJobData("payload", Payload)
                    .Build(),
                TriggerBuilder.Create()
                    .WithIdentity(triggerKey)
                    .StartNow()
                    // Repeating, so that reading the trigger back afterwards reads a row rather than
                    // finding the one a completed one-shot trigger took with it.
                    .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
                    .Build()).ConfigureAwait(false);

            await scheduler.Start().ConfigureAwait(false);

            // Signalled by the job itself. A sleep would pass on a machine slow enough to make it
            // meaningless, and fail on one that is merely busy.
            Task completed = await Task.WhenAny(fired.Task, Task.Delay(TimeSpan.FromSeconds(60))).ConfigureAwait(false);
            if (completed != fired.Task)
            {
                return "FAIL store: the job never fired within a minute, so the store never handed a trigger to the scheduler.";
            }

            IJobDetail? job = await scheduler.GetJobDetail(jobKey).ConfigureAwait(false);
            if (job is null)
            {
                return "FAIL store: the job could not be read back.";
            }

            if (job.JobDataMap.GetString("payload") != Payload)
            {
                return $"FAIL store: the job data map came back as '{job.JobDataMap.GetString("payload")}'.";
            }

            ITrigger? trigger = await scheduler.GetTrigger(triggerKey).ConfigureAwait(false);
            if (trigger is null)
            {
                return "FAIL store: the trigger could not be read back.";
            }

            if (!Equals(trigger.JobKey, jobKey))
            {
                return $"FAIL store: the trigger came back pointing at '{trigger.JobKey}'.";
            }

            await scheduler.Shutdown(waitForJobsToComplete: true).ConfigureAwait(false);

            Console.WriteLine("PASS store: scheduled, fired and read back through a SQLite store reached by its DbProviderFactory.");
            return null;
        }
        catch (Exception e)
        {
            return $"FAIL store: {e.GetType().FullName}: {e.Message}{Environment.NewLine}{e}";
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(databaseFile);
        }
    }

    /// <summary>
    /// A value long enough that it is written as a blob parameter rather than inlined into anything.
    /// </summary>
    private const string Payload = "the store round-trips a job data map out of a trimmed publish";

    /// <summary>
    /// Creates the tables from the schema Quartz ships, embedded so that the executable carries it and
    /// needs no repository beside it.
    /// </summary>
    private static async Task CreateSchema(string connectionString)
    {
        using Stream stream = typeof(StoreCheck).Assembly.GetManifestResourceStream("tables_sqlite.sql")
            ?? throw new InvalidOperationException("The SQLite schema is embedded by Quartz.Trimming.Canary.csproj.");

        using StreamReader reader = new(stream);
        string schema = await reader.ReadToEndAsync().ConfigureAwait(false);

        using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = schema;
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static void TryDelete(string databaseFile)
    {
        try
        {
            File.Delete(databaseFile);
        }
        catch (IOException)
        {
            // A leftover temporary file is not a failure of anything this checks.
        }
    }

    /// <summary>
    /// Signals that the store handed this job to the scheduler. Its type is resolved out of the
    /// <c>JOB_CLASS_NAME</c> column by name, which is the one string-named contract the store path still
    /// has.
    /// </summary>
    public sealed class CanaryJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            fired.TrySetResult();
            return default;
        }
    }
}
