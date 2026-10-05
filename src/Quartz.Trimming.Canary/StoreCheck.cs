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

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

using Microsoft.Data.Sqlite;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.Serialization.SystemTextJson;

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
    /// What the typed job was handed, which is the half of the input round trip that a compile cannot
    /// stand in for: it is written as <see cref="object" /> and read back as its own type, both through
    /// metadata the application declared rather than through reflection there is none of.
    /// </summary>
    private static readonly TaskCompletionSource<CanaryInput> typedInput = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Signals that the job nothing registered by hand fired: <see cref="DeclaredCanaryJob" /> is
    /// declared with <c>[QuartzJob]</c> and <c>[CronTrigger]</c> and reaches the store through the
    /// registration the source generator wrote. Generated or not, that code calls the same
    /// <c>AddJob&lt;T&gt;</c> the rest of this file calls, so a publish with no reflection left has to
    /// be able to run it.
    /// </summary>
    private static readonly TaskCompletionSource declaredFired = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// The same for <see cref="DeclaredIntervalCanaryJob" />, declared with <c>[SimpleTrigger]</c>: its
    /// registration carries the declared interval as ticks and parses the configured one, both in code
    /// the source generator wrote.
    /// </summary>
    private static readonly TaskCompletionSource declaredIntervalFired = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// What the delegate job was handed, and how it was called: its handler is a lambda, so the source
    /// generator intercepted the call and bound it at compile time (#3882). The job comes back out of
    /// <c>JOB_CLASS_NAME</c> as <c>Quartz.Impl.DelegateJob</c>.
    /// </summary>
    private static readonly TaskCompletionSource<string> delegateFired = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// The same for a handler passed as a <see cref="Delegate" />, which the generator leaves alone: its
    /// parameters are read off <see cref="Delegate.Method" /> and it is invoked through
    /// <see cref="System.Reflection.MethodBase.Invoke(object?, object?[])" />, which is the half of phase A
    /// of #3867 that only a native publish can prove.
    /// </summary>
    private static readonly TaskCompletionSource<string> reflectedDelegateFired = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// The policy <see cref="RetryingCanaryJob" />'s retry ran under. Its trigger names none, so the retry
    /// happening at all says the scheduler read the job type's <see cref="RetryPolicyAttribute" />, which
    /// is a custom attribute instantiated from metadata in a publish with no reflection left.
    /// </summary>
    private static readonly TaskCompletionSource<string> retriedByAttribute = new(TaskCreationOptions.RunContinuationsAsynchronously);

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

            // How an application with no reflection left declares a type of its own — here the typed
            // job's payload. The same registry answers for job data values, so there is one place to
            // declare a type and not two.
            SystemTextJsonSerializerRegistry registry = new();
            registry.AddTypeInfoResolver(CanaryJsonContext.Default);
            services.AddSingleton(registry);

            // What DeclaredCanaryJob's ConfigurationKey reads. Its attribute's own expression fires in
            // 2099, so the job firing within the minute is the configured value having been read by the
            // generated registration, in a publish with no reflection left to read it with.
            // DeclaredIntervalCanaryJob's key is read the same way; the trigger read back afterwards
            // carrying this interval rather than the attribute's day is what says it was.
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
                .AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>(DeclaredCronKey, "0/1 * * * * ?"),
                    new KeyValuePair<string, string?>(DeclaredIntervalKey, DeclaredInterval.ToString("c", CultureInfo.InvariantCulture)),
                ])
                .Build());

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
                    store.ConfigureStore(options => options.SchemaProvisioning = SchemaProvisioning.Validate);
                });

                // Every job this assembly declares with [QuartzJob], which is two: the call is
                // generated from the attributes and is the only registration DeclaredCanaryJob and
                // DeclaredIntervalCanaryJob get.
                q.AddDeclaredJobs();

                // A job that is a lambda, its dependencies its parameters: a service, the firing and its
                // token, bound by the code the source generator wrote for this call.
                q.ScheduleJob("canary-delegate", static (ILogger<CanaryJob> log, IJobExecutionContext context, CancellationToken cancellationToken) =>
                {
                    log.LogInformation("Delegate job {JobKey} fired", context.JobDetail.Key);
                    delegateFired.TrySetResult(
                        $"{context.JobDetail.JobType.FullName} with a logger, the firing and {(cancellationToken.CanBeCanceled ? "its token" : "no token")}, {HowCalled()}");
                    return Task.CompletedTask;
                }, trigger => trigger.WithIdentity("canary-delegate", "store").StartNow());

                // The same handler as a Delegate, which the generator cannot see through, so it is bound by
                // reading the delegate's own parameters and invoked through reflection.
                Delegate reflected = static (ILogger<CanaryJob> log, IJobExecutionContext context, CancellationToken cancellationToken) =>
                {
                    log.LogInformation("Delegate job {JobKey} fired", context.JobDetail.Key);
                    reflectedDelegateFired.TrySetResult(
                        $"{context.JobDetail.JobType.FullName} with a logger, the firing and {(cancellationToken.CanBeCanceled ? "its token" : "no token")}, {HowCalled()}");
                    return Task.CompletedTask;
                };
                q.ScheduleJob("canary-delegate-reflected", reflected, trigger => trigger.WithIdentity("canary-delegate-reflected", "store").StartNow());
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

            // A job that declares the type of its input, scheduled with a payload on the trigger. The
            // payload goes into the database as the string the scheduler's IJobInputSerializer wrote,
            // and comes back out as a CanaryInput — all of it out of a publish with no reflection.
            await scheduler.ScheduleJob(
                JobBuilder.Create<TypedCanaryJob>()
                    .WithIdentity("typed", "store")
                    .Build(),
                TriggerBuilder.Create<TypedCanaryJob>()
                    .WithIdentity("typed", "store")
                    .StartNow()
                    .UsingInput(new CanaryInput("the typed input round-trips out of a trimmed publish", 7))
                    .Build()).ConfigureAwait(false);

            // A job whose type declares its retry policy, on a trigger that declares none.
            await scheduler.ScheduleJob(
                JobBuilder.Create<RetryingCanaryJob>()
                    .WithIdentity("retrying", "store")
                    .Build(),
                TriggerBuilder.Create()
                    .WithIdentity("retrying", "store")
                    .StartNow()
                    .Build()).ConfigureAwait(false);

            await scheduler.Start().ConfigureAwait(false);

            // Signalled by the job itself. A sleep would pass on a machine slow enough to make it
            // meaningless, and fail on one that is merely busy.
            Task completed = await Task.WhenAny(fired.Task, Task.Delay(TimeSpan.FromSeconds(60))).ConfigureAwait(false);
            if (completed != fired.Task)
            {
                return "FAIL store: the job never fired within a minute, so the store never handed a trigger to the scheduler.";
            }

            Task typed = await Task.WhenAny(typedInput.Task, Task.Delay(TimeSpan.FromSeconds(60))).ConfigureAwait(false);
            if (typed != typedInput.Task)
            {
                return "FAIL store: the typed-input job never fired within a minute.";
            }

            Task declared = await Task.WhenAny(declaredFired.Task, Task.Delay(TimeSpan.FromSeconds(60))).ConfigureAwait(false);
            if (declared != declaredFired.Task)
            {
                return "FAIL store: the job declared with [QuartzJob] never fired within a minute, so the generated registration did not reach the scheduler or did not read its ConfigurationKey.";
            }

            Task declaredInterval = await Task.WhenAny(declaredIntervalFired.Task, Task.Delay(TimeSpan.FromSeconds(60))).ConfigureAwait(false);
            if (declaredInterval != declaredIntervalFired.Task)
            {
                return "FAIL store: the job declared with [SimpleTrigger] never fired within a minute, so the generated registration did not reach the scheduler.";
            }

            if (await scheduler.GetTrigger(new TriggerKey("declared-interval", "store")).ConfigureAwait(false) is not ISimpleTrigger interval
                || interval.RepeatInterval != DeclaredInterval)
            {
                return $"FAIL store: the trigger declared with [SimpleTrigger] did not come back on the configured interval of {DeclaredInterval}, so its ConfigurationKey was not read.";
            }

            Task delegated = await Task.WhenAny(delegateFired.Task, Task.Delay(TimeSpan.FromSeconds(60))).ConfigureAwait(false);
            if (delegated != delegateFired.Task)
            {
                return "FAIL store: the delegate job never fired within a minute, so its handler could not be bound or invoked.";
            }

            string delegateRun = await delegateFired.Task.ConfigureAwait(false);
            if (delegateRun != "Quartz.Impl.DelegateJob, Quartz with a logger, the firing and its token, bound at compile time")
            {
                return $"FAIL store: the delegate job ran as '{delegateRun}'.";
            }

            Task reflectedDelegated = await Task.WhenAny(reflectedDelegateFired.Task, Task.Delay(TimeSpan.FromSeconds(60))).ConfigureAwait(false);
            if (reflectedDelegated != reflectedDelegateFired.Task)
            {
                return "FAIL store: the delegate job passed as a Delegate never fired within a minute, so its handler could not be bound or invoked by reflection.";
            }

            string reflectedDelegateRun = await reflectedDelegateFired.Task.ConfigureAwait(false);
            if (reflectedDelegateRun != "Quartz.Impl.DelegateJob, Quartz with a logger, the firing and its token, bound by reflection")
            {
                return $"FAIL store: the delegate job passed as a Delegate ran as '{reflectedDelegateRun}'.";
            }

            Task retried = await Task.WhenAny(retriedByAttribute.Task, Task.Delay(TimeSpan.FromSeconds(60))).ConfigureAwait(false);
            if (retried != retriedByAttribute.Task)
            {
                return "FAIL store: the job declaring [RetryPolicy] failed and was never retried within a minute, so its type's attribute was not read.";
            }

            string retryPolicy = await retriedByAttribute.Task.ConfigureAwait(false);
            if (retryPolicy != "fixed;1;00:00:00.2000000")
            {
                return $"FAIL store: the job declaring [RetryPolicy] was retried under '{retryPolicy}'.";
            }

            CanaryInput received = await typedInput.Task.ConfigureAwait(false);
            CanaryInput expected = new("the typed input round-trips out of a trimmed publish", 7);
            if (received != expected)
            {
                return $"FAIL store: the typed job was handed '{received}' rather than '{expected}'.";
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

            Console.WriteLine($"PASS delegate: {delegateRun}");
            Console.WriteLine($"PASS delegate: {reflectedDelegateRun}");
            Console.WriteLine($"PASS retry: a job declaring [RetryPolicy] was retried under {retryPolicy} on a trigger that names no policy.");
            Console.WriteLine("PASS store: scheduled, fired and read back through a SQLite store reached by its DbProviderFactory, typed job input, jobs declared with [CronTrigger] and [SimpleTrigger] on configured schedules and two delegate jobs, one bound at compile time and one by reflection, included.");
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

    /// <summary>
    /// How the running handler was called, read off the stack it is running on: through the interceptor
    /// the source generator wrote into <c>Quartz.Generated</c>, or through a reflective invoke.
    /// </summary>
    /// <remarks>
    /// The two paths are built to be indistinguishable from inside the handler in every other way, so the
    /// frames are the only witness. The native binary keeps its stack trace data by default, which is
    /// what makes the names readable there.
    /// </remarks>
    private static string HowCalled()
    {
        string stack = Environment.StackTrace;
        bool generated = stack.Contains("Quartz.Generated.", StringComparison.Ordinal);
        bool reflective = stack.Contains("System.Reflection.", StringComparison.Ordinal);

        return (generated, reflective) switch
        {
            (true, false) => "bound at compile time",
            (false, true) => "bound by reflection",
            _ => $"called through a stack that says neither or both: {stack}",
        };
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

    /// <summary>
    /// A job that declares the type of its input, so the payload arrives as a parameter. The dispatch is
    /// the default implementation of <see cref="IJob{TInput}" />, which is the whole reason there is no
    /// <c>MakeGenericMethod</c> anywhere in the feature and therefore nothing here for ILCompiler to
    /// report.
    /// </summary>
    public sealed class TypedCanaryJob : IJob<CanaryInput>
    {
        public ValueTask Execute(IJobExecutionContext context, CanaryInput input, CancellationToken cancellationToken = default)
        {
            typedInput.TrySetResult(input);
            return default;
        }
    }

    /// <summary>
    /// Fails its first attempt and reports the policy its retry ran under. The policy is declared here
    /// and nowhere else, so a retry is the scheduler having read this attribute out of the type's metadata.
    /// </summary>
    [RetryPolicy(1, "00:00:00.200")]
    public sealed class RetryingCanaryJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            if (context.RetryAttempt == 0)
            {
                throw new InvalidOperationException("The canary fails its first attempt, so that its type's [RetryPolicy] is what retries it.");
            }

            retriedByAttribute.TrySetResult(context.RetryPolicy?.ToStoredString() ?? "no policy");
            return default;
        }
    }

    /// <summary>
    /// The configuration key <see cref="DeclaredCanaryJob" />'s schedule is read from.
    /// </summary>
    private const string DeclaredCronKey = "Canary:DeclaredCron";

    /// <summary>
    /// A job that says when it runs where it is written. Nothing registers it by hand: the source
    /// generator reads the two attributes and writes the <c>AddJob</c> and <c>AddTrigger</c> calls
    /// that <c>AddDeclaredJobs</c> above runs, the second reading its expression from configuration.
    /// </summary>
    [QuartzJob(Name = "declared", Group = "store", Description = "declared with an attribute rather than registered by hand")]
    [CronTrigger("0 0 0 1 1 ? 2099", ConfigurationKey = DeclaredCronKey)]
    public sealed class DeclaredCanaryJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            declaredFired.TrySetResult();
            return default;
        }
    }

    /// <summary>
    /// The configuration key <see cref="DeclaredIntervalCanaryJob" />'s interval is read from.
    /// </summary>
    private const string DeclaredIntervalKey = "Canary:DeclaredInterval";

    /// <summary>
    /// What <see cref="DeclaredIntervalKey" /> is set to, which the attribute's own day is not.
    /// </summary>
    private static readonly TimeSpan DeclaredInterval = TimeSpan.FromMinutes(7);

    /// <summary>
    /// A job declared on an interval. A simple trigger with no start time fires as the scheduler starts,
    /// so the firing says the generated registration ran; the interval read back says it read the key.
    /// </summary>
    [QuartzJob(Name = "declared-interval", Group = "store")]
    [SimpleTrigger("1.00:00:00", ConfigurationKey = DeclaredIntervalKey)]
    public sealed class DeclaredIntervalCanaryJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            declaredIntervalFired.TrySetResult();
            return default;
        }
    }
}

/// <summary>
/// The typed job's payload: a type Quartz has never heard of, which is the case the input serializer
/// has to answer for.
/// </summary>
public sealed record CanaryInput(string Note, int Attempt);

/// <summary>
/// The metadata for <see cref="CanaryInput" />, handed to the scheduler's registry. This is what an
/// application published without reflection writes for its own payload types.
/// </summary>
[JsonSerializable(typeof(CanaryInput))]
internal sealed partial class CanaryJsonContext : JsonSerializerContext;
