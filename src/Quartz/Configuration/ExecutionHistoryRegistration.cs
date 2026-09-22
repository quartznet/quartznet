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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.Diagnostics;
using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Configuration;

/// <summary>
/// Puts the ADO-backed execution history in place of the in-memory one, for one scheduler.
/// </summary>
/// <remarks>
/// One place rather than two: <c>UsePersistentStore(store =&gt; store.UseExecutionHistory())</c> and the
/// legacy <c>quartz.jobStore.executionHistory</c> key both arrive here, so the two spellings cannot
/// register different things.
/// </remarks>
internal static class ExecutionHistoryRegistration
{
    /// <summary>
    /// Registers the recorder, the bounds and the database-backed store for one scheduler.
    /// </summary>
    /// <param name="services">The collection this scheduler is registered into.</param>
    /// <param name="schedulerKey">
    /// The scheduler's service key, or <see langword="null" /> for the default scheduler, whose parts
    /// are unkeyed.
    /// </param>
    public static void Apply(IServiceCollection services, string? schedulerKey)
    {
        // The recorder and the bounds, unless the application has already asked for them. TryAdd
        // inside, so a store the application registered itself is kept and so is an earlier call.
        services.AddQuartzExecutionHistory();

        if (schedulerKey is null)
        {
            ReplaceDefaultStore(services);
            return;
        }

        // A named scheduler's history is keyed by its name, which is what SchedulerScopedServiceProvider
        // resolves for its recorder and what InProcessQuartzApiClient asks for when a dashboard renders
        // that scheduler. TryAdd, so calling this twice registers one store.
        services.TryAddKeyedSingleton<IExecutionHistoryStore>(
            schedulerKey,
            static (provider, key) => Create(SchedulerScopedServiceProvider.For(provider, key)));
    }

    /// <summary>
    /// Takes the unkeyed slot from the in-memory default, and only from it.
    /// </summary>
    /// <remarks>
    /// The recorder, the dashboard and the HTTP API all resolve <see cref="IExecutionHistoryStore" />
    /// without a key, so the default scheduler's store has to be the unkeyed one. An application that
    /// registered a history store of its own before <c>AddQuartz</c> said what it wanted and keeps it —
    /// the same rule <c>AddQuartzDashboard</c> follows, and the reason the default is registered by
    /// implementation type rather than by a factory nobody can recognise.
    /// </remarks>
    private static void ReplaceDefaultStore(IServiceCollection services)
    {
        for (int i = 0; i < services.Count; i++)
        {
            ServiceDescriptor descriptor = services[i];

            if (descriptor.ServiceType == typeof(IExecutionHistoryStore)
                && !descriptor.IsKeyedService
                && descriptor.ImplementationType == typeof(InMemoryExecutionHistoryStore))
            {
                services[i] = ServiceDescriptor.Singleton<IExecutionHistoryStore>(
                    static provider => Create(SchedulerScopedServiceProvider.For(provider, key: null)));
                return;
            }
        }
    }

    /// <summary>
    /// Builds the store from the scheduler's own parts: its connection provider, its dialect, its clock.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The store is handed a driver delegate of its own rather than the job store's, because it can be
    /// read before the job store has been initialized. A dashboard or the HTTP API resolves this store at
    /// their first request, and nothing about that request builds the scheduler — a registered
    /// scheduler is listed without being built, on purpose. The job store initializes its delegate when
    /// the scheduler is built, and until then a history read threw a
    /// <see cref="NullReferenceException" /> from <c>StdAdoDelegate.PrepareCommand</c>.
    /// </para>
    /// <para>
    /// Of the two ways to have an initialized delegate before the first statement, this is the one that
    /// initializes nothing twice. Initializing the job store from here would be the scheduler's build
    /// done a second time when the scheduler is built — and <see cref="IJobStore.Initialize" /> takes an
    /// identity whose generated instance id only that build settles. Initializing the job store's own
    /// delegate from here would be the same object initialized twice, racing the job store over it. A
    /// copy of the scheduler's delegate, initialized here with what the job store would hand its own —
    /// the table prefix, the connection provider, the command timeout — is the history's alone, and the
    /// job store initializes its delegate exactly once, as before.
    /// </para>
    /// </remarks>
    private static AdoExecutionHistoryStore Create(IServiceProvider provider)
    {
        return ActivatorUtilities.CreateInstance<AdoExecutionHistoryStore>(provider, DelegateOfItsOwn(provider));
    }

    /// <summary>
    /// A copy of the scheduler's driver delegate, initialized for the history's statements.
    /// </summary>
    /// <remarks>
    /// The instance id and the scheduler name are the configured ones rather than the settled ones,
    /// because no history statement reads either off the delegate: every row carries its own, and every
    /// query names the scheduler it reads. No trigger persistence delegates are handed over — the history
    /// reads no trigger — so none of the job store's is ever pointed at the copy.
    /// </remarks>
    private static IDriverDelegate DelegateOfItsOwn(IServiceProvider provider)
    {
        IDriverDelegate schedulerDelegate = provider.GetRequiredService<IDriverDelegate>();

        if (schedulerDelegate is not StdAdoDelegate standard)
        {
            // The store refuses it, with the message that says what to do about it.
            return schedulerDelegate;
        }

        AdoJobStoreOptions storeOptions = provider.GetRequiredService<IOptions<AdoJobStoreOptions>>().Value;
        QuartzSchedulerOptions schedulerOptions = provider.GetRequiredService<IOptions<QuartzSchedulerOptions>>().Value;

        StdAdoDelegate copy = standard.CopyForHistory();
        copy.Initialize(new DriverDelegateContext
        {
            UseProperties = storeOptions.StoreJobDataAsStrings,

            // As AdoJobStoreBase reads it, so the copy and the job store's delegate name the same tables.
            TablePrefix = storeOptions.TablePrefix ?? "",
            SchedulerName = schedulerOptions.InstanceName,
            InstanceId = schedulerOptions.InstanceId,
            DbProvider = provider.GetRequiredService<IDbProvider>(),
            TypeLoader = provider.GetRequiredService<ITypeLoader>(),
            TimeProvider = provider.GetService<TimeProvider>() ?? TimeProvider.System,
            CommandTimeout = storeOptions.CommandTimeout,
            LoggerFactory = provider.GetService<ILoggerFactory>() ?? LogProviderLoggerFactory.Instance,
        });

        return copy;
    }
}
