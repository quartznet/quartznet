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
using System.Runtime.InteropServices;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Impl;

namespace Quartz.Trimming.Canary;

/// <summary>
/// Job data coerced onto a job's properties with <c>Quartz.JobData.IsTypeConverterCoercionSupported</c>
/// off, which is what a trimmed or natively compiled publish has.
/// </summary>
/// <remarks>
/// <para>
/// First, that the switch really is off: a run with it on proves nothing about a publish without
/// <c>TypeDescriptor</c>. Then a job whose <see cref="int" />, enum, <see cref="Guid" /> and
/// <see cref="TimeSpan" /> properties are set from string job data fires on a scheduler, which is the
/// conversion every job factory makes and the one the fast paths exist for. A missing value for a
/// <see cref="Nullable{T}" /> is <see langword="null" /> and for a struct its zeroed default, with no
/// <c>Activator</c> behind either. And a conversion only a <c>TypeConverter</c> could make — a
/// <see cref="Version" /> from a string — is refused with the message that names the property, both types,
/// the switch and the MSBuild property that turns it back on. That last line is what shows the fallback is
/// gone rather than merely unused: with the switch on, <c>VersionConverter</c> would read it.
/// </para>
/// </remarks>
internal static class JobDataCheck
{
    private static readonly Guid CanaryId = new("6f1c3b2a-8d4e-4f5a-9b6c-7d8e9f0a1b2c");

    private static readonly TaskCompletionSource<CoercedJob> fired = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Runs the check, returning <see langword="null" /> when it passed and a message when it did not.
    /// </summary>
    public static async Task<string?> Run()
    {
        try
        {
            List<string> failures = [];

            CheckSwitch(failures);
            await CheckFiredJob(failures).ConfigureAwait(false);
            CheckDefaults(failures);
            CheckRefusal(failures);

            if (failures.Count > 0)
            {
                return "FAIL job-data: " + string.Join(Environment.NewLine + "  ", failures);
            }

            Console.WriteLine("PASS job-data: the TypeConverter switch is off; a job's int, enum, Guid and TimeSpan properties bind from string job data, missing values take their defaults, and a Version is refused naming the switch.");
            return null;
        }
        catch (Exception e)
        {
            return $"FAIL job-data: {e.GetType().FullName}: {e.Message}{Environment.NewLine}{e}";
        }
    }

    private static void CheckSwitch(List<string> failures)
    {
        if (!AppContext.TryGetSwitch(FeatureSwitches.TypeConverterCoercionSwitch, out bool enabled))
        {
            failures.Add($"{FeatureSwitches.TypeConverterCoercionSwitch} is not set at all, so the trimmer was never told to remove the code it guards. Publish with PublishTrimmed=true and the Quartz.targets import.");
        }
        else if (enabled)
        {
            failures.Add($"{FeatureSwitches.TypeConverterCoercionSwitch} is true, so this run proves nothing about a publish without TypeDescriptor.");
        }

        if (FeatureSwitches.IsTypeConverterCoercionSupported)
        {
            failures.Add("FeatureSwitches reads the TypeConverter switch as on, so the guarded code is still reachable.");
        }
    }

    private static async Task CheckFiredJob(List<string> failures)
    {
        ServiceCollection services = new();
        services.AddQuartz();

        ServiceProvider container = services.BuildServiceProvider();
        await using ConfiguredAsyncDisposable containerDisposal = container.ConfigureAwait(false);

        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler().ConfigureAwait(false);

        // Every value a string, as a schedule file, a request body or a configuration section writes it.
        await scheduler.ScheduleJob(
            JobBuilder.Create<CoercedJob>()
                .WithIdentity("coerced", "job-data")
                .UsingJobData(nameof(CoercedJob.Count), "42")
                .UsingJobData(nameof(CoercedJob.Colour), "green")
                .UsingJobData(nameof(CoercedJob.Id), CanaryId.ToString())
                .UsingJobData(nameof(CoercedJob.Delay), "00:01:30")
                .Build(),
            TriggerBuilder.Create()
                .WithIdentity("coerced", "job-data")
                .StartNow()
                .Build()).ConfigureAwait(false);

        await scheduler.Start().ConfigureAwait(false);

        Task completed = await Task.WhenAny(fired.Task, Task.Delay(TimeSpan.FromSeconds(60))).ConfigureAwait(false);
        await scheduler.Shutdown(waitForJobsToComplete: true).ConfigureAwait(false);

        if (completed != fired.Task)
        {
            failures.Add("the job with string job data never fired within a minute.");
            return;
        }

        CoercedJob job = await fired.Task.ConfigureAwait(false);
        Expect(failures, job.Count == 42, $"Count is {job.Count}, not 42: a string did not bind onto an int property");
        Expect(failures, job.Colour == CanaryColour.Green, $"Colour is {job.Colour}, not Green: 'green' did not bind onto an enum property case-insensitively");
        Expect(failures, job.Id == CanaryId, $"Id is {job.Id}, not {CanaryId}: a string did not bind onto a Guid property");
        Expect(failures, job.Delay == TimeSpan.FromSeconds(90), $"Delay is {job.Delay}, not 00:01:30: a string did not bind onto a TimeSpan property");
    }

    private static void CheckDefaults(List<string> failures)
    {
        PropertySettingJobFactory factory = new() { PropertyMismatchBehavior = PropertyMismatchBehavior.Throw };
        CoercedJob job = new();

        factory.SetObjectProperties(job, new JobDataMap
        {
            [nameof(CoercedJob.Limit)] = null!,
            [nameof(CoercedJob.Origin)] = null!,
        });

        Expect(failures, job.Limit is null, $"Limit is {job.Limit}, not null: a missing value for a Nullable<int> did not come out as null");
        Expect(failures, job.Origin == default, $"Origin is {job.Origin}, not the zeroed default: a missing value for a struct did not come out as default");
    }

    private static void CheckRefusal(List<string> failures)
    {
        PropertySettingJobFactory factory = new() { PropertyMismatchBehavior = PropertyMismatchBehavior.Throw };

        try
        {
            factory.SetObjectProperties(new CoercedJob(), new JobDataMap { [nameof(CoercedJob.Release)] = "1.2.3" });
            failures.Add("a Version property bound from '1.2.3', so a TypeConverter is still reachable in this publish.");
        }
        catch (SchedulerException e) when (e.Message.Contains($"'{nameof(CoercedJob.Release)}'", StringComparison.Ordinal)
                                           && e.Message.Contains(typeof(string).FullName!, StringComparison.Ordinal)
                                           && e.Message.Contains(typeof(Version).FullName!, StringComparison.Ordinal)
                                           && e.Message.Contains(FeatureSwitches.TypeConverterCoercionSwitch, StringComparison.Ordinal)
                                           && e.Message.Contains(FeatureSwitches.TypeConverterCoercionProperty, StringComparison.Ordinal))
        {
            // Refused, naming the property, the two types, the switch and the MSBuild property.
        }
        catch (SchedulerException e)
        {
            failures.Add($"a Version property was refused without naming the property, both types, the switch and the MSBuild property: {e.Message}");
        }
    }

    private static void Expect(List<string> failures, bool condition, string failure)
    {
        if (!condition)
        {
            failures.Add(failure);
        }
    }

    /// <summary>
    /// An enum of the canary's own, so its member names are this executable's to keep rather than the
    /// framework's.
    /// </summary>
    public enum CanaryColour
    {
        Red,
        Green,
        Blue,
    }

    /// <summary>
    /// A struct of the canary's own, whose default nothing but a zeroed box can make without reflection.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    public readonly record struct CanaryPoint(int X, int Y);

    /// <summary>
    /// Takes its job data onto properties of every type the check is about, and reports itself.
    /// </summary>
    public sealed class CoercedJob : IJob
    {
        public int Count { get; set; }

        public CanaryColour Colour { get; set; }

        public Guid Id { get; set; }

        public TimeSpan Delay { get; set; }

        public int? Limit { get; set; } = 7;

        public CanaryPoint Origin { get; set; } = new(3, 4);

        public Version? Release { get; set; }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            fired.TrySetResult(this);
            return default;
        }
    }
}
