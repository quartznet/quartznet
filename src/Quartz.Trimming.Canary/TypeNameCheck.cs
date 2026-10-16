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

using System.Collections.Specialized;
using System.Runtime.CompilerServices;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Jobs;
using Quartz.Util;

namespace Quartz.Trimming.Canary;

/// <summary>
/// The two type-name feature switches, off, and what that does to a type name and to a flat <c>quartz.*</c> key
/// in a trimmed or natively compiled publish.
/// </summary>
/// <remarks>
/// <para>
/// The switches are set by the targets file the canary imports, and the first thing checked is that
/// they really are off: a run with them on proves nothing about the publish. Then a name the host
/// registered resolves through the container's type loader with no reflection left to resolve it; a
/// shipped job registered with <c>AddJobType&lt;NoOpJob&gt;()</c> resolves under both its 4.x spelling
/// and the 3.x one a database may still hold; and a type that exists in this executable but was never
/// registered is refused with the exception that names the registration to add. That last line is the
/// one that shows reflection is off rather than merely unset — with it on, the runtime would find the
/// type.
/// </para>
/// <para>
/// The second switch is shown the same way: <c>AddQuartz</c> given a property bag naming Quartz's own
/// in-memory store builds a scheduler on it, a bag naming a third-party store is refused naming the
/// code form, and a value key binds as it always has.
/// </para>
/// </remarks>
internal static class TypeNameCheck
{
    /// <summary>
    /// Runs the check, returning <see langword="null" /> when it passed and a message when it did not.
    /// </summary>
    public static async Task<string?> Run()
    {
        try
        {
            List<string> failures = [];

            CheckSwitches(failures);
            await CheckTypeNames(failures).ConfigureAwait(false);
            await CheckComponentKeys(failures).ConfigureAwait(false);

            if (failures.Count > 0)
            {
                return "FAIL type-names: " + string.Join(Environment.NewLine + "  ", failures);
            }

            Console.WriteLine("PASS type-names: both switches are off; a registered job and NoOpJob under both spellings resolve, an unregistered type is refused by name, and a quartz.* key naming Quartz's own store builds a scheduler while one naming another is refused.");
            return null;
        }
        catch (Exception e)
        {
            return $"FAIL type-names: {e.GetType().FullName}: {e.Message}{Environment.NewLine}{e}";
        }
    }

    private static void CheckSwitches(List<string> failures)
    {
        foreach (string name in new[] { FeatureSwitches.TypeNameResolutionSwitch, FeatureSwitches.LegacyComponentKeysSwitch })
        {
            if (!AppContext.TryGetSwitch(name, out bool enabled))
            {
                failures.Add($"{name} is not set at all, so the trimmer was never told to remove the code it guards. Publish with PublishTrimmed=true and the Quartz.targets import.");
            }
            else if (enabled)
            {
                failures.Add($"{name} is true, so this run proves nothing about a publish without reflection.");
            }
        }

        if (FeatureSwitches.IsTypeNameResolutionSupported || FeatureSwitches.IsLegacyComponentKeysSupported)
        {
            failures.Add("FeatureSwitches reads a switch as on, so the guarded code is still reachable.");
        }
    }

    private static async Task CheckTypeNames(List<string> failures)
    {
        ServiceCollection services = new();
        services.AddQuartz(quartz =>
        {
            quartz.AddJobType<RegisteredCanaryJob>();

            // A shipped job, registered the way the how-to says a trimmed application registers every job
            // it runs. Nothing in Quartz.Jobs registers itself.
            quartz.AddJobType<NoOpJob>();
        });

        ServiceProvider container = services.BuildServiceProvider();
        await using ConfiguredAsyncDisposable containerDisposal = container.ConfigureAwait(false);

        ITypeLoader loader = container.GetRequiredService<ITypeLoader>();

        string registered = typeof(RegisteredCanaryJob).AssemblyQualifiedNameWithoutVersion();
        Expect(failures, loader.LoadType(registered) == typeof(RegisteredCanaryJob),
            $"the registered job's name '{registered}' did not resolve to the registered type");

        Expect(failures, loader.LoadType("Quartz.Jobs.NoOpJob, Quartz.Jobs") == typeof(NoOpJob),
            "NoOpJob did not resolve under its 4.x spelling once AddJobType<NoOpJob>() registered it");

        Expect(failures, loader.LoadType("Quartz.Job.NoOpJob, Quartz") == typeof(NoOpJob),
            "NoOpJob did not resolve under the 3.x spelling a database may still hold, so the rename walk is not applied to the registered types");

        // The line that proves reflection is off: the type exists in this executable - the typeof keeps
        // it - and nothing registered it, so a resolution that succeeds here went through Type.GetType.
        string unregistered = typeof(NeverRegisteredCanaryJob).AssemblyQualifiedNameWithoutVersion();
        try
        {
            Type? resolved = loader.LoadType(unregistered);
            failures.Add($"'{unregistered}' resolved to {resolved?.FullName ?? "null"} although nothing registered it, so the publish still resolves type names by reflection.");
        }
        catch (TypeLoadException e) when (e.Message.Contains("AddJob<T>()", StringComparison.Ordinal)
                                          && e.Message.Contains(FeatureSwitches.TypeNameResolutionSwitch, StringComparison.Ordinal))
        {
            // Refused, naming the registration to add and the switch that is off.
        }
        catch (TypeLoadException e)
        {
            failures.Add($"'{unregistered}' was refused without naming AddJob<T>() and the switch: {e.Message}");
        }

        // The same refusal from a name-constructed JobType, which is what a stored row becomes. (TryResolve
        // is not asked here: its annotated out parameter is an IL2062 for ILLink whatever it is handed, and
        // the unit tests cover it.)
        JobType stored = new(unregistered);
        try
        {
            _ = stored.Type;
            failures.Add("a JobType over an unregistered name settled a type, so the store's fallback reflected.");
        }
        catch (InvalidOperationException e) when (e.Message.Contains("AddJob<T>()", StringComparison.Ordinal))
        {
            // The hint the first fire of such a job would carry.
        }
    }

    private static async Task CheckComponentKeys(List<string> failures)
    {
        // Quartz's own spelling of the store, in the 3.x namespace a configuration file carried over from
        // 3.x still has, and a value key beside it.
        ServiceCollection accepted = new();
        accepted.AddQuartz(new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = "TypeNameCanary",
            ["quartz.jobStore.type"] = "Quartz.Simpl.RAMJobStore, Quartz",
            ["quartz.threadPool.type"] = "Quartz.Simpl.SimpleThreadPool, Quartz",
            ["quartz.serializer.type"] = "stj",
        });

        ServiceProvider container = accepted.BuildServiceProvider();
        await using ConfiguredAsyncDisposable containerDisposal = container.ConfigureAwait(false);

        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler().ConfigureAwait(false);
        Expect(failures, scheduler.SchedulerName == "TypeNameCanary", $"quartz.scheduler.instanceName did not bind: the scheduler is '{scheduler.SchedulerName}'");
        Expect(failures, container.GetRequiredService<IJobStore>() is RAMJobStore, "quartz.jobStore.type naming RAMJobStore did not register the in-memory store");
        Expect(failures, container.GetRequiredService<IThreadPool>() is DefaultThreadPool, "quartz.threadPool.type naming SimpleThreadPool did not register the default thread pool");
        Expect(failures, container.GetRequiredService<IObjectSerializer>() is SystemTextJsonObjectSerializer, "quartz.serializer.type = stj did not register the built-in serializer");
        await scheduler.Shutdown().ConfigureAwait(false);

        // A store Quartz does not ship, which only reflection could build.
        ServiceCollection refused = new();
        try
        {
            refused.AddQuartz(new NameValueCollection
            {
                ["quartz.jobStore.type"] = "Acme.Scheduling.DocumentJobStore, Acme.Scheduling",
            });
            failures.Add("quartz.jobStore.type naming a third-party store was accepted, so the publish still builds components from names.");
        }
        catch (SchedulerConfigException e) when (e.Message.Contains("UseJobStore<T>()", StringComparison.Ordinal)
                                                 && e.Message.Contains(FeatureSwitches.LegacyComponentKeysSwitch, StringComparison.Ordinal))
        {
            // Refused at AddQuartz, naming the code form and the switch.
        }
        catch (SchedulerConfigException e)
        {
            failures.Add($"quartz.jobStore.type naming a third-party store was refused without naming UseJobStore<T>() and the switch: {e.Message}");
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
    /// A job the host registers, whose name a store, a schedule file or a request may then carry.
    /// </summary>
    public sealed class RegisteredCanaryJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>
    /// A job that exists and that nothing registers: never passed to <c>AddJobType</c>, never built by
    /// <c>JobBuilder.Create&lt;T&gt;()</c>, never wrapped in a <see cref="JobType" /> from its type.
    /// </summary>
    public sealed class NeverRegisteredCanaryJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
