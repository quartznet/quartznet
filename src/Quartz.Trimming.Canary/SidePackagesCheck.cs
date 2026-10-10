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

using Microsoft.Extensions.DependencyInjection;

using Quartz.Jobs;

namespace Quartz.Trimming.Canary;

/// <summary>
/// The three packages a scheduler loads into its own process, each run on a path of its own out of a
/// trimmed or natively compiled publish: <c>Quartz.Jobs</c>, <c>Quartz.Plugins</c> and
/// <c>Quartz.Plugins.TimeZoneConverter</c>.
/// </summary>
/// <remarks>
/// <para>
/// Each path is the one the package's baseline is about, or the one thing the package does. The
/// <see cref="DirectoryScanJob" /> finds its listener by the name in its job data, which used to be the one
/// reflective call site in <c>Quartz.Jobs</c> and is a container lookup now; the XML and JSON schedule-file
/// plugins hand the scheduler a job type spelled as text, which is what <c>Quartz.Plugins</c> records twice;
/// and <c>UseTimeZoneConverter</c> registers the one resolver its package consists of, which is then asked
/// for an id .NET alone cannot resolve. A history plugin rides along, so that one of the plugins that only
/// listens is loaded too.
/// </para>
/// <para>
/// Every one of them ends in a job firing or a zone resolving, which is what a compile cannot stand in
/// for: a listener type the trimmer removed, a job type the file names that did not survive, a resolver
/// that was never registered, each shows up only when the code runs.
/// </para>
/// </remarks>
internal static class SidePackagesCheck
{
    private const string SchedulerName = "SidePackagesCanary";

    private static readonly TaskCompletionSource<IReadOnlyCollection<FileInfo>> filesSeen = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// A zone .NET resolves on its own only through ICU, which this canary publishes without: an IANA id
    /// where the operating system speaks Windows ids, and a Windows id where it speaks IANA ones. Helsinki
    /// either way, at UTC+2 standard time.
    /// </summary>
    private static readonly string ForeignZoneId = OperatingSystem.IsWindows() ? "Europe/Helsinki" : "FLE Standard Time";

    /// <summary>
    /// Runs the check, returning <see langword="null" /> when it passed and a message when it did not.
    /// </summary>
    public static async Task<string?> Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"quartz-canary-{Guid.NewGuid():N}");
        string inbox = Path.Combine(directory, "inbox");

        try
        {
            Directory.CreateDirectory(inbox);

            // A file written a minute ago, so that the scan finds it settled rather than still being written.
            string settled = Path.Combine(inbox, "settled.txt");
            await File.WriteAllTextAsync(settled, "the directory scan job found this file").ConfigureAwait(false);
            File.SetLastWriteTimeUtc(settled, DateTime.UtcNow.AddMinutes(-1));

            string jsonFile = Path.Combine(directory, "quartz_jobs.json");
            await File.WriteAllTextAsync(jsonFile, JsonSchedule()).ConfigureAwait(false);

            string xmlFile = Path.Combine(directory, "quartz_jobs.xml");
            await File.WriteAllTextAsync(xmlFile, XmlSchedule()).ConfigureAwait(false);

            ServiceCollection services = new();

            // The listener the directory scan job names in its job data, found through the container by
            // the name of its type - the lookup that used to sweep every loaded assembly.
            services.AddSingleton<IDirectoryScanListener, CanaryScanListener>();

            services.AddQuartz(quartz =>
            {
                quartz.ConfigureScheduler(options =>
                {
                    options.InstanceName = SchedulerName;
                    options.InstanceId = "one";
                });

                // Quartz.Plugins.TimeZoneConverter, the whole of it: one resolver, registered now.
                quartz.UseTimeZoneConverter();

                // The job types the two schedule files name as strings, registered the way the trimming
                // how-to says, so that the trimmer keeps them for the plugins to resolve.
                quartz.AddJobType<ScheduledByJsonJob>();
                quartz.AddJobType<ScheduledByXmlJob>();

                // Quartz.Plugins: both schedule-file plugins, each reading a file that names a job's type as
                // text, and the structured history plugin, which only listens.
                quartz.UseJsonSchedulingConfiguration(jsonFile);
                quartz.UseXmlSchedulingConfiguration(xmlFile);
                quartz.UseStructuredJobLogging();

                // Quartz.Jobs: a shipped job, configured through its options type and fired at once.
                quartz.AddJob<DirectoryScanJob>(job =>
                {
                    job.WithIdentity("scan", "canary");
                    job.UsingDirectoryScanOptions(new DirectoryScanOptions
                    {
                        Directories = [inbox],
                        ScanListenerName = nameof(CanaryScanListener),
                        MinimumUpdateAge = TimeSpan.FromSeconds(1),
                    });
                });

                quartz.AddTrigger(trigger => trigger
                    .ForJob(new JobKey("scan", "canary"))
                    .WithIdentity("scan", "canary")
                    .StartNow());
            });

            // The resolver is registered by the configuration above, so the zone can be asked for before
            // anything is built. .NET alone has to refuse it first: a run in which it did not would prove
            // nothing about the package, and says so rather than passing.
            string? zoneFailure = CheckTimeZone(out TimeZoneInfo zone);
            if (zoneFailure is not null)
            {
                return zoneFailure;
            }

            ServiceProvider container = services.BuildServiceProvider();
            await using ConfiguredAsyncDisposable containerDisposal = container.ConfigureAwait(false);

            IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler().ConfigureAwait(false);
            await scheduler.Start().ConfigureAwait(false);

            IReadOnlyCollection<FileInfo> files = await Within(filesSeen.Task,
                "the directory scan job never told its listener about the file within a minute, so Quartz.Jobs could not find the listener by its type's name or did not fire.").ConfigureAwait(false);
            if (!files.Any(file => string.Equals(file.FullName, settled, StringComparison.Ordinal)))
            {
                return $"FAIL side-packages: the directory scan job reported {files.Count} file(s), none of them {settled}.";
            }

            await Within(ScheduledByJsonJob.Ran.Task,
                "the job quartz_jobs.json declares never fired within a minute, so the JSON scheduling plugin did not read the file or could not resolve the type it names.").ConfigureAwait(false);

            await Within(ScheduledByXmlJob.Ran.Task,
                "the job quartz_jobs.xml declares never fired within a minute, so the XML scheduling plugin did not read the file or could not resolve the type it names.").ConfigureAwait(false);

            // A trigger in the resolved zone, scheduled and read back: the store serializer writes the zone
            // as its id and resolves it again on the way out.
            ITrigger? stored = await RoundTripZone(scheduler, zone).ConfigureAwait(false);
            if (stored is not ICronTrigger cron)
            {
                return $"FAIL side-packages: the trigger in {zone.Id} came back as '{stored?.GetType().FullName ?? "null"}', not a cron trigger.";
            }

            if (!string.Equals(cron.TimeZone.Id, zone.Id, StringComparison.Ordinal))
            {
                return $"FAIL side-packages: the trigger in {zone.Id} came back in {cron.TimeZone.Id}.";
            }

            DateTimeOffset? nextFire = cron.GetFireTimeAfter(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            DateTimeOffset expected = new(2026, 1, 1, 7, 0, 0, TimeSpan.Zero);
            if (nextFire != expected)
            {
                return $"FAIL side-packages: a trigger at 09:00 in {zone.Id} fires next at {nextFire:O}, not {expected:O}.";
            }

            await scheduler.Shutdown(waitForJobsToComplete: true).ConfigureAwait(false);

            Console.WriteLine($"PASS jobs: DirectoryScanJob scanned {inbox} and told {nameof(CanaryScanListener)}, found in the container by the name in its job data, about {files.Count} file(s).");
            Console.WriteLine($"PASS plugins: the jobs quartz_jobs.json and quartz_jobs.xml declare fired, each from a type the file names as text, with the structured job history plugin listening.");
            Console.WriteLine($"PASS timezone: .NET alone refuses '{ForeignZoneId}' in this publish; UseTimeZoneConverter resolves it to {zone.Id} ({zone.BaseUtcOffset}), and a trigger in it round-trips through the scheduler.");
            return null;
        }
        catch (Exception e)
        {
            return $"FAIL side-packages: {e.GetType().FullName}: {e.Message}{Environment.NewLine}{e}";
        }
        finally
        {
            TryDelete(directory);
        }
    }

    /// <summary>
    /// Asks for <see cref="ForeignZoneId" /> twice: of .NET, which has to refuse it for the second answer
    /// to mean anything, and of Quartz, which answers through the resolver the package registered.
    /// </summary>
    private static string? CheckTimeZone(out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;

        try
        {
            TimeZoneInfo resolvedByDotnet = TimeZoneInfo.FindSystemTimeZoneById(ForeignZoneId);
            return $"FAIL timezone: .NET resolved '{ForeignZoneId}' on its own, as {resolvedByDotnet.Id}, so this run cannot tell whether Quartz.Plugins.TimeZoneConverter was asked. The canary publishes with InvariantGlobalization so that it would be.";
        }
        catch (TimeZoneNotFoundException)
        {
            // What the check needs: an id only the resolver can answer.
        }

        zone = TimeZones.FindById(ForeignZoneId);

        if (zone.BaseUtcOffset != TimeSpan.FromHours(2))
        {
            return $"FAIL timezone: '{ForeignZoneId}' resolved to {zone.Id} at {zone.BaseUtcOffset}, which is not Helsinki.";
        }

        return null;
    }

    /// <summary>
    /// Schedules a cron trigger in <paramref name="zone" /> for the durable job the JSON file stored, and
    /// reads it back: the zone goes into the store as its id and comes back out through the same lookup.
    /// </summary>
    private static async Task<ITrigger?> RoundTripZone(IScheduler scheduler, TimeZoneInfo zone)
    {
        TriggerKey key = new("zoned", "canary");

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity(key)
            .ForJob(new JobKey("json", "canary"))
            .StartAt(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))
            .WithSchedule(CronScheduleBuilder.Create("0 0 9 * * ?").InTimeZone(zone))
            .Build();

        await scheduler.ScheduleJob(trigger).ConfigureAwait(false);

        return await scheduler.GetTrigger(key).ConfigureAwait(false);
    }

    private static async Task<T> Within<T>(Task<T> awaited, string failure)
    {
        Task completed = await Task.WhenAny(awaited, Task.Delay(TimeSpan.FromSeconds(60))).ConfigureAwait(false);
        if (completed != awaited)
        {
            throw new InvalidOperationException(failure);
        }

        return await awaited.ConfigureAwait(false);
    }

    private static async Task Within(Task awaited, string failure)
    {
        Task completed = await Task.WhenAny(awaited, Task.Delay(TimeSpan.FromSeconds(60))).ConfigureAwait(false);
        if (completed != awaited)
        {
            throw new InvalidOperationException(failure);
        }

        await awaited.ConfigureAwait(false);
    }

    /// <summary>
    /// The JSON schedule file: one job, its type spelled as text, on a simple trigger that fires at once.
    /// </summary>
    private static string JsonSchedule()
    {
        string jobType = new JobType(typeof(ScheduledByJsonJob)).FullName;

        return $$"""
            {
              "Schedule": {
                "Jobs": [
                  { "Name": "json", "Group": "canary", "JobType": "{{jobType}}", "Durable": true }
                ],
                "Triggers": [
                  { "Name": "json", "Group": "canary", "JobName": "json", "JobGroup": "canary", "Simple": { "RepeatCount": 0, "Interval": "00:00:01" } }
                ]
              }
            }
            """;
    }

    /// <summary>
    /// The XML schedule file, in the 2.0 schema: the same job and trigger, spelled the way
    /// <c>job_scheduling_data</c> spells them.
    /// </summary>
    private static string XmlSchedule()
    {
        string jobType = new JobType(typeof(ScheduledByXmlJob)).FullName;

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <job-scheduling-data xmlns="http://quartznet.sourceforge.net/JobSchedulingData" version="2.0">
              <schedule>
                <job>
                  <name>xml</name>
                  <group>canary</group>
                  <job-type>{jobType}</job-type>
                  <durable>true</durable>
                  <recover>false</recover>
                </job>
                <trigger>
                  <simple>
                    <name>xml</name>
                    <group>canary</group>
                    <job-name>xml</job-name>
                    <job-group>canary</job-group>
                    <repeat-count>0</repeat-count>
                    <repeat-interval>1000</repeat-interval>
                  </simple>
                </trigger>
              </schedule>
            </job-scheduling-data>
            """;
    }

    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary directory is not a failure of anything this checks.
        }
        catch (UnauthorizedAccessException)
        {
            // Nor is one the file system would not let go of yet.
        }
    }

    /// <summary>
    /// What <see cref="DirectoryScanJob" /> tells about the directory. Registered in the container as an
    /// <see cref="IDirectoryScanListener" /> and named in the job data by the name of this type.
    /// </summary>
    public sealed class CanaryScanListener : IDirectoryScanListener
    {
        public ValueTask FilesUpdatedOrAdded(IReadOnlyCollection<FileInfo> updatedFiles, CancellationToken cancellationToken = default)
        {
            filesSeen.TrySetResult(updatedFiles);
            return default;
        }

        public ValueTask FilesDeleted(IReadOnlyCollection<FileInfo> deletedFiles, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }
}

/// <summary>
/// The job <c>quartz_jobs.json</c> declares, by this type's name as a string.
/// </summary>
public sealed class ScheduledByJsonJob : IJob
{
    internal static TaskCompletionSource Ran { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        Ran.TrySetResult();
        return default;
    }
}

/// <summary>
/// The job <c>quartz_jobs.xml</c> declares, by this type's name as a string.
/// </summary>
public sealed class ScheduledByXmlJob : IJob
{
    internal static TaskCompletionSource Ran { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        Ran.TrySetResult();
        return default;
    }
}
