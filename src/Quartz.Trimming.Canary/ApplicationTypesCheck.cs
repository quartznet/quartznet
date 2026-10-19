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
using System.Text.Json.Serialization;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Impl;

namespace Quartz.Trimming.Canary;

/// <summary>
/// An application's own type, as a job data value and as a typed job's input, through a SQLite store, out
/// of a trimmed or natively compiled publish — declared once, with <c>AddJsonTypeInfoResolver</c>.
/// </summary>
/// <remarks>
/// <para>
/// The two halves are written by two serializers: the store's writes the job data map into its blob, and
/// the job input serializer writes the input into the string the trigger carries. Before
/// <c>AddJsonTypeInfoResolver</c>, the documented registration — <c>UseSystemTextJsonSerializer</c>'s
/// callback — reached the first and not the second. This check registers nothing else: no container
/// registry, no store callback. Each half working says the one call reached that serializer.
/// </para>
/// <para>
/// Last, a type nothing declared is refused, which is what shows reflection is off rather than unset, and
/// that the refusal names the type and the call.
/// </para>
/// </remarks>
internal static class ApplicationTypesCheck
{
    private static readonly CanaryReport Report = new("quarterly", "finance@example.com");

    /// <summary>
    /// The job's and the trigger's name, and the job data key the report is stored under.
    /// </summary>
    private const string Name = "report";

    private const string Group = "application-types";

    private static readonly TaskCompletionSource<(CanaryReport Input, object? JobData)> fired = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Runs the check, returning <see langword="null" /> when it passed and a message when it did not.
    /// </summary>
    public static async Task<string?> Run()
    {
        string databaseFile = StoreRoundTrip.NewDatabaseFile(Group);

        try
        {
            ServiceCollection services = new();
            services.AddQuartz(q =>
            {
                q.ConfigureScheduler(options =>
                {
                    options.InstanceName = "CanaryApplicationTypes";
                    options.InstanceId = "one";
                });

                q.UsePersistentStore(store =>
                {
                    store.UseSqlite(SqliteFactory.Instance, $"Data Source={databaseFile}");
                    store.ProvisionSchema();
                });

                // The registration under test, and the only one: both of the scheduler's serializers read it.
                q.AddJsonTypeInfoResolver(ApplicationTypesJsonContext.Default);
            });

            ServiceProvider container = services.BuildServiceProvider();
            await using ConfiguredAsyncDisposable containerDisposal = container.ConfigureAwait(false);

            IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler().ConfigureAwait(false);

            JobKey jobKey = new(Name, Group);

            await scheduler.ScheduleJob(
                JobBuilder.Create<ReportJob>()
                    .WithIdentity(jobKey)
                    .StoreDurably()
                    .UsingJobData(Name, Report)
                    .Build(),
                TriggerBuilder.Create<ReportJob>(scheduler.TimeProvider)
                    .WithIdentity(Name, Group)
                    .StartNow()
                    .UsingInput(Report)
                    .Build()).ConfigureAwait(false);

            await scheduler.Start().ConfigureAwait(false);

            Task completed = await Task.WhenAny(fired.Task, Task.Delay(TimeSpan.FromSeconds(60), scheduler.TimeProvider)).ConfigureAwait(false);
            if (completed != fired.Task)
            {
                await scheduler.Shutdown(waitForJobsToComplete: true).ConfigureAwait(false);
                return "FAIL application-types: the job never fired within a minute.";
            }

            (CanaryReport input, object? jobData) = await fired.Task.ConfigureAwait(false);
            IJobDetail? stored = await scheduler.GetJobDetail(jobKey).ConfigureAwait(false);
            await scheduler.Shutdown(waitForJobsToComplete: true).ConfigureAwait(false);

            if (input != Report)
            {
                return $"FAIL application-types: the typed job was handed '{input}' rather than '{Report}'.";
            }

            if (!IsReport(jobData))
            {
                return $"FAIL application-types: the job read its data back as '{Describe(jobData)}' rather than the report's two members.";
            }

            if (!IsReport(stored?.JobDataMap[Name]))
            {
                return $"FAIL application-types: the job read back through IScheduler carried '{Describe(stored?.JobDataMap[Name])}'.";
            }

            string? refusal = CheckRefusal();
            if (refusal is not null)
            {
                return refusal;
            }

            Console.WriteLine($"PASS application-types: a {nameof(CanaryReport)} declared once with AddJsonTypeInfoResolver went into a SQLite store as job data and came back as its members, and as an IJob<{nameof(CanaryReport)}> input came back as itself.");
            Console.WriteLine("PASS application-types: an input type nothing declared is refused with a NotSupportedException naming it and AddJsonTypeInfoResolver.");
            return null;
        }
        catch (Exception e)
        {
            return $"FAIL application-types: {e.GetType().FullName}: {e.Message}{Environment.NewLine}{e}";
        }
        finally
        {
            StoreRoundTrip.DeleteDatabase(databaseFile);
        }
    }

    /// <summary>
    /// Whether a job data value read back is the report. The store format reads an object back as a
    /// <see cref="Dictionary{TKey, TValue}" /> of strings, which is why both of its members are strings.
    /// </summary>
    private static bool IsReport(object? value)
    {
        return value is Dictionary<string, string> members
               && members.Count == 2
               && members.GetValueOrDefault(nameof(CanaryReport.Title)) == Report.Title
               && members.GetValueOrDefault(nameof(CanaryReport.Recipient)) == Report.Recipient;
    }

    private static string Describe(object? value)
    {
        return value is Dictionary<string, string> members
            ? string.Join(", ", members.Select(pair => $"{pair.Key}={pair.Value}"))
            : value?.ToString() ?? "nothing";
    }

    /// <summary>
    /// Asks a serializer with no application resolvers to write a type nothing declared.
    /// </summary>
    private static string? CheckRefusal()
    {
        try
        {
            new SystemTextJsonJobInputSerializer().Serialize(new UndeclaredReport("nobody declared this"));
            return "FAIL application-types: an input type nothing declared was written, so this publish still serializes by reflection.";
        }
        catch (NotSupportedException e) when (e.Message.Contains(typeof(UndeclaredReport).FullName!, StringComparison.Ordinal)
                                              && e.Message.Contains("AddJsonTypeInfoResolver(", StringComparison.Ordinal))
        {
            return null;
        }
        catch (NotSupportedException e)
        {
            return $"FAIL application-types: an input type nothing declared was refused without naming the type and AddJsonTypeInfoResolver: {e.Message}";
        }
    }

    /// <summary>
    /// Takes the report as its input, and reads the same report back out of its job data.
    /// </summary>
    public sealed class ReportJob : IJob<CanaryReport>
    {
        public ValueTask Execute(IJobExecutionContext context, CanaryReport input, CancellationToken cancellationToken = default)
        {
            fired.TrySetResult((input, context.MergedJobDataMap[Name]));
            return default;
        }
    }
}

/// <summary>
/// A type of the application's own, stored as job data and passed as a job's input.
/// </summary>
public sealed record CanaryReport(string Title, string Recipient);

/// <summary>
/// A type nothing declares, which a publish without reflection has to refuse.
/// </summary>
public sealed record UndeclaredReport(string Note);

/// <summary>
/// The metadata the scheduler is handed once, for both of its serializers.
/// </summary>
[JsonSerializable(typeof(CanaryReport))]
internal sealed partial class ApplicationTypesJsonContext : JsonSerializerContext;
