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

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Quartz.Trimming.Canary;

/// <summary>
/// A scheduler configured entirely from an <see cref="IConfiguration" />, out of a trimmed or natively
/// compiled publish, with every bound value read back off the thing that uses it.
/// </summary>
/// <remarks>
/// <para>
/// This is issue #3430's artefact, and the reason it is here rather than in a unit test is that the
/// reflection binder it replaced passes every unit test there is. Built against that binder and
/// published <em>natively</em>, this check fails: <c>Scheduler:MaxBatchSize</c>,
/// <c>Scheduler:ShutdownJobInterruption</c> and every entry of <c>Scheduler:Context</c> arrive as their
/// defaults, with no error anywhere — which is what the IL3050 the baseline used to record was warning
/// about, and what a configured application would have got out of a native publish. The same build
/// published <c>TrimMode=full</c> passes, so the trimmed leg alone would not have caught it either.
/// </para>
/// <para>
/// Each value is read back through whatever actually uses it rather than off the options object where
/// possible: the scheduler's own name, its thread pool's size, and the scheduler context a job would
/// read. The rest are read from the container's options, which is the same instance the component
/// holding them was given.
/// </para>
/// <para>
/// The store is <see cref="StoreCheck" />'s business, not this one's. A data source is configured here
/// because the per-child <c>DataSource:&lt;name&gt;</c> bind is the sixth intercepted call site and the
/// only one whose options are named after something other than the scheduler, but nothing opens a
/// connection through it — the scheduler runs on the in-memory store.
/// </para>
/// </remarks>
internal static class BindingCheck
{
    // Shared between the section and the assertion because the two are the same text. Anything the
    // binder has to parse - a TimeSpan, an int, an enum - is written out on both sides instead, so that
    // the configured spelling and the value expected of it never come from one place.
    private const string SchedulerName = "BindingCanary";
    private const string ConnectionString = "Data Source=binding-canary.db";
    private const string TablePrefix = "CANARY_";
    private const string ContextValue = "native";

    /// <summary>
    /// Runs the check, returning <see langword="null" /> when it passed and a message when it did not.
    /// </summary>
    public static async Task<string?> Run()
    {
        try
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // Scheduler — a string, a TimeSpan, an int, an enum and a dictionary bound into.
                    ["Quartz:Scheduler:InstanceName"] = SchedulerName,
                    ["Quartz:Scheduler:InstanceId"] = "one",
                    ["Quartz:Scheduler:IdleWaitTime"] = "00:00:20",
                    ["Quartz:Scheduler:MaxBatchSize"] = "3",
                    ["Quartz:Scheduler:ShutdownJobInterruption"] = nameof(ShutdownJobInterruption.Always),
                    ["Quartz:Scheduler:Context:environment"] = ContextValue,

                    ["Quartz:ThreadPool:MaxConcurrency"] = "7",

                    // JobStore — the same section binds the in-memory and the ADO.NET options both.
                    ["Quartz:JobStore:MisfireThreshold"] = "00:02:30",
                    ["Quartz:JobStore:TablePrefix"] = TablePrefix,
                    ["Quartz:JobStore:DataSource"] = "canary",
                    ["Quartz:JobStore:Clustering:CheckinInterval"] = "00:00:11",

                    ["Quartz:DataSource:canary:Provider"] = DataSourceOptions.Providers.Sqlite,
                    ["Quartz:DataSource:canary:ConnectionString"] = ConnectionString,
                })
                .Build();

            ServiceCollection services = new();
            services.AddQuartz(configuration.GetSection("Quartz"));

            ServiceProvider container = services.BuildServiceProvider();
            await using ConfiguredAsyncDisposable containerDisposal = container.ConfigureAwait(false);

            IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler().ConfigureAwait(false);
            SchedulerMetadata metadata = await scheduler.GetMetadata().ConfigureAwait(false);

            List<string> wrong = [];

            Check(wrong, "Scheduler:InstanceName", SchedulerName, scheduler.SchedulerName);
            Check(wrong, "ThreadPool:MaxConcurrency", 7, metadata.ThreadPoolSize);

            // Asked for rather than indexed, so that a context the binder never filled is one more line
            // in the report rather than the exception that ends it.
            scheduler.Context.TryGetValue("environment", out object? contextValue);
            Check(wrong, "Scheduler:Context:environment", ContextValue, contextValue);

            QuartzSchedulerOptions schedulerOptions = container.GetRequiredService<IOptions<QuartzSchedulerOptions>>().Value;
            Check(wrong, "Scheduler:IdleWaitTime", TimeSpan.FromSeconds(20), schedulerOptions.IdleWaitTime);
            Check(wrong, "Scheduler:MaxBatchSize", 3, schedulerOptions.MaxBatchSize);
            Check(wrong, "Scheduler:ShutdownJobInterruption", ShutdownJobInterruption.Always, schedulerOptions.ShutdownJobInterruption);

            Check(wrong, "JobStore:MisfireThreshold", TimeSpan.FromSeconds(150),
                container.GetRequiredService<IOptions<InMemoryJobStoreOptions>>().Value.MisfireThreshold);

            Check(wrong, "JobStore:TablePrefix", TablePrefix,
                container.GetRequiredService<IOptions<AdoJobStoreOptions>>().Value.TablePrefix);

            Check(wrong, "JobStore:Clustering:CheckinInterval", TimeSpan.FromSeconds(11),
                container.GetRequiredService<IOptions<ClusteringOptions>>().Value.CheckinInterval);

            // The per-child bind, named after the data source rather than after the scheduler.
            Check(wrong, "DataSource:canary:ConnectionString", ConnectionString,
                container.GetRequiredService<IOptionsMonitor<DataSourceOptions>>().Get("canary").ConnectionString);

            if (wrong.Count > 0)
            {
                return "FAIL binding: the configuration section was accepted and then not used." + Environment.NewLine
                    + string.Join(Environment.NewLine, wrong);
            }

            await scheduler.Shutdown(waitForJobsToComplete: false).ConfigureAwait(false);

            Console.WriteLine("PASS binding: a scheduler built from an IConfiguration carries every value the section named.");
            return null;
        }
        catch (Exception e)
        {
            return $"FAIL binding: {e.GetType().FullName}: {e.Message}{Environment.NewLine}{e}";
        }
    }

    private static void Check(List<string> wrong, string key, object expected, object? actual)
    {
        if (!Equals(expected, actual))
        {
            wrong.Add($"  {key}: bound as '{actual ?? "null"}', configuration said '{expected}'");
        }
    }
}
