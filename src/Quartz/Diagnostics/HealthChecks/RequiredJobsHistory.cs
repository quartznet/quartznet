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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Extensibility;

namespace Quartz;

/// <summary>
/// Where the health check reads <see cref="QuartzHealthCheckOptions.RequiredJobs" /> from, and what it says
/// when there is nothing there to read.
/// </summary>
/// <remarks>
/// One place for the validator that refuses a misconfiguration at startup and the check that meets one at
/// run time, so the two cannot name different stores or give different advice.
/// </remarks>
internal static class RequiredJobsHistory
{
    /// <summary>
    /// The store holding the scheduler's history, found as the dashboard and the HTTP API find it, or
    /// <see langword="null" /> with <paramref name="refusal" /> saying why there is none.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="schedulerName">The scheduler's registration name, or <see langword="null" /> for the default one.</param>
    /// <param name="refusal">Why there is no store, when there is none.</param>
    public static IExecutionHistoryStore? Find(IServiceProvider services, string? schedulerName, out string? refusal)
    {
        IExecutionHistoryStore? shared = services.GetService<IExecutionHistoryStore>();
        if (shared is not null)
        {
            return ExecutionHistoryLookup.Find(services, attachedStores: null, shared, schedulerName, out refusal);
        }

        // A scheduler AddQuartzHttpClient registered has a history of its own, keyed by its name, in a
        // container that may keep no shared one.
        if (schedulerName is not null
            && services is IKeyedServiceProvider keyed
            && keyed.GetKeyedService(typeof(IExecutionHistoryStore), schedulerName) is IExecutionHistoryStore own)
        {
            refusal = null;
            return own;
        }

        refusal =
            $"The health check of {Describe(schedulerName)} requires jobs to have succeeded recently "
            + $"({nameof(QuartzHealthCheckOptions.RequiredJobs)}), and reads that from the execution history, which "
            + "this container does not keep. Call services.AddQuartzExecutionHistory() to keep it in memory, or "
            + "UsePersistentStore(store => store.UseExecutionHistory()) to keep it in the scheduler's database.";
        return null;
    }

    /// <summary>
    /// What to say when the store throws <see cref="NotSupportedException" /> from its status read.
    /// </summary>
    public static string KeepsNoStatus(string? schedulerName, IExecutionHistoryStore store, NotSupportedException exception)
    {
        return $"The health check of {Describe(schedulerName)} requires jobs to have succeeded recently "
               + $"({nameof(QuartzHealthCheckOptions.RequiredJobs)}), and reads that from each job's run status, which "
               + $"its execution history store, {store.GetType().Name}, does not keep: {exception.Message} Keep the "
               + "history in a store that keeps a status per job - services.AddQuartzExecutionHistory() keeps one in "
               + "memory - or remove the requirements.";
    }

    private static string Describe(string? schedulerName)
    {
        return schedulerName is null ? "the default Quartz scheduler" : $"Quartz scheduler '{schedulerName}'";
    }
}

/// <summary>
/// Refuses <see cref="QuartzHealthCheckOptions.RequiredJobs" /> on a scheduler whose history cannot answer them.
/// </summary>
/// <remarks>
/// <para>
/// Declared with <c>ValidateOnStart</c> by the check's registration, so a host fails at startup rather than
/// reporting every required job as late for want of a history nobody configured. Without a host it runs
/// when the health-check service is first built, which reads these options.
/// </para>
/// <para>
/// The store is asked for the statuses of no jobs at all. That is the whole question — can it read a status
/// — and a store that keeps statuses answers it without going anywhere: an empty set lists nothing. Any
/// failure other than <see cref="NotSupportedException" /> is not a misconfiguration; a store that cannot be
/// reached yet is reported by the check when it runs.
/// </para>
/// </remarks>
internal sealed class RequiredJobsHistoryValidator : IValidateOptions<QuartzHealthCheckOptions>
{
    private readonly IServiceProvider services;

    public RequiredJobsHistoryValidator(IServiceProvider services)
    {
        this.services = services;
    }

    public ValidateOptionsResult Validate(string? name, QuartzHealthCheckOptions options)
    {
        if (options.RequiredJobs.Count == 0)
        {
            return ValidateOptionsResult.Success;
        }

        string? schedulerName = string.IsNullOrEmpty(name) ? null : name;
        IExecutionHistoryStore? store = RequiredJobsHistory.Find(services, schedulerName, out string? refusal);
        if (store is null)
        {
            return ValidateOptionsResult.Fail(refusal!);
        }

        try
        {
            store.QueryJobRunStatuses(new JobRunStatusQuery
                {
                    SchedulerName = schedulerName ?? QuartzSchedulerOptions.DefaultInstanceName,
                    Jobs = [],
                    Take = 0
                })
                .AsTask().GetAwaiter().GetResult();
        }
        catch (NotSupportedException e)
        {
            return ValidateOptionsResult.Fail(RequiredJobsHistory.KeepsNoStatus(schedulerName, store, e));
        }
        catch (Exception)
        {
            // Unreachable is not unsupported: the check reports what it finds when it runs.
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Success;
    }
}
