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

#nullable enable

using System.Collections.Specialized;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// Which execution history store a scheduler ends up with, and which one it leaves alone.
/// </summary>
/// <remarks>
/// The recorder, the dashboard and the HTTP API all resolve <see cref="IExecutionHistoryStore" />, so
/// what <c>UseExecutionHistory()</c> is doing is taking a slot. Which slot depends on the scheduler:
/// the default scheduler's parts are unkeyed and a named scheduler's are keyed by its name, and
/// getting that wrong is either a store nothing writes to or one scheduler recording into another's
/// database.
/// </remarks>
public sealed class ExecutionHistorySelectionTest
{
    /// <summary>A connection string nothing here opens: no statement is issued by any of these cases.</summary>
    private const string ConnectionString = "Data Source=:memory:";

    [Test]
    public void TheDefaultSchedulerTakesTheUnkeyedSlotFromTheInMemoryDefault()
    {
        ServiceCollection services = new();
        services.AddQuartz(quartz => quartz.UsePersistentStore(store =>
        {
            store.UseSqlite(SqliteFactory.Instance, ConnectionString);
            store.UseExecutionHistory();
        }));

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<IExecutionHistoryStore>().Should().BeOfType<AdoExecutionHistoryStore>(
            "the default scheduler's parts are unkeyed, and that is the slot everything that reads a "
            + "history resolves");

        provider.GetRequiredService<IOptions<AdoJobStoreOptions>>().Value.ExecutionHistory.Should().BeTrue(
            "the schema check has to cover the two history tables now, and this is what tells it to");
    }

    /// <summary>
    /// A named scheduler's history is keyed by its name, and the container's shared one is untouched.
    /// </summary>
    /// <remarks>
    /// Its recorder resolves through <c>SchedulerScopedServiceProvider</c>, which answers a request for
    /// a history store with this scheduler's own when it has one and the container's when it has not —
    /// the fallback being what keeps a named scheduler on the in-memory default recording at all.
    /// </remarks>
    [Test]
    public void ANamedSchedulerTakesAKeyedSlotAndLeavesTheSharedOneAlone()
    {
        ServiceCollection services = new();
        services.AddQuartz();
        services.AddQuartz("reporting", quartz => quartz.UsePersistentStore(store =>
        {
            store.UseSqlite(SqliteFactory.Instance, ConnectionString);
            store.UseExecutionHistory();
        }));

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredKeyedService<IExecutionHistoryStore>("reporting").Should()
            .BeOfType<AdoExecutionHistoryStore>();

        provider.GetRequiredService<IExecutionHistoryStore>().Should().BeOfType<InMemoryExecutionHistoryStore>(
            "the default scheduler asked for nothing, and one scheduler's choice of history is not "
            + "another's");

        SchedulerScopedServiceProvider.For(provider, "reporting").GetService<IExecutionHistoryStore>()
            .Should().BeOfType<AdoExecutionHistoryStore>(
                "a named scheduler's recorder resolves its own store, which is the whole point of "
                + "keying it");

        SchedulerScopedServiceProvider.For(provider, "elsewhere").GetService<IExecutionHistoryStore>()
            .Should().BeOfType<InMemoryExecutionHistoryStore>(
                "a named scheduler that asked for no database-backed history falls back to the "
                + "container's shared store rather than recording nowhere");
    }

    /// <summary>
    /// The shipped in-memory store is the only one replaced.
    /// </summary>
    /// <remarks>
    /// An application that registered a history store of its own said what it wanted, and
    /// <c>AddQuartzExecutionHistory()</c>'s <c>TryAdd</c> already honours that. This has to honour it
    /// too, or turning a persistent store's history on would quietly discard a store somebody wrote.
    /// </remarks>
    [Test]
    public void AStoreTheApplicationRegisteredIsNotReplaced()
    {
        NoOpExecutionHistoryStore applicationStore = new();

        ServiceCollection services = new();
        services.AddSingleton<IExecutionHistoryStore>(applicationStore);
        services.AddQuartz(quartz => quartz.UsePersistentStore(store =>
        {
            store.UseSqlite(SqliteFactory.Instance, ConnectionString);
            store.UseExecutionHistory();
        }));

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<IExecutionHistoryStore>().Should().BeSameAs(applicationStore,
            "only Quartz's own default is taken over — a store the application registered is its "
            + "decision");
    }

    /// <summary>
    /// Calling it twice registers one store, as calling <c>AddQuartzExecutionHistory()</c> twice
    /// installs one recorder.
    /// </summary>
    [Test]
    public void AskingTwiceRegistersOneStore()
    {
        ServiceCollection services = new();
        services.AddQuartz(quartz => quartz.UsePersistentStore(store =>
        {
            store.UseSqlite(SqliteFactory.Instance, ConnectionString);
            store.UseExecutionHistory();
            store.UseExecutionHistory();
        }));

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetServices<IExecutionHistoryStore>().Should().ContainSingle()
            .Which.Should().BeOfType<AdoExecutionHistoryStore>();
    }

    /// <summary>
    /// The 3.x-shaped spelling of the same decision.
    /// </summary>
    /// <remarks>
    /// A new setting is a typed option plus a bridge entry. The bridge registers the store while the
    /// scheduler's own configuration phase is still running, because the recorder is installed through
    /// <c>ConfigureAllQuartzSchedulers</c> and a scheduler mid-registration is carried those delegates
    /// before the registration phase — so the plugin is asserted here rather than only the store.
    /// </remarks>
    [Test]
    public void TheLegacyKeySelectsTheSameStoreAndInstallsTheRecorder()
    {
        NameValueCollection properties = new()
        {
            ["quartz.jobStore.executionHistory"] = "true"
        };

        ServiceCollection services = new();
        services.AddQuartz(properties, quartz => quartz.UsePersistentStore(store =>
            store.UseSqlite(SqliteFactory.Instance, ConnectionString)));

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<IExecutionHistoryStore>().Should().BeOfType<AdoExecutionHistoryStore>(
            "quartz.jobStore.executionHistory is UseExecutionHistory() written the way a 3.x "
            + "configuration file writes things");

        provider.GetRequiredService<IOptions<AdoJobStoreOptions>>().Value.ExecutionHistory.Should().BeTrue();

        provider.GetServices<ISchedulerPlugin>().Should().ContainItemsAssignableTo<ISchedulerPlugin>()
            .And.Contain(plugin => plugin is ExecutionHistoryPlugin,
                "the key has to install the recorder as well, or the scheduler would have a history "
                + "store nothing ever writes to");
    }

    /// <summary>
    /// A store that records nowhere, standing in for one an application wrote.
    /// </summary>
    private sealed class NoOpExecutionHistoryStore : IExecutionHistoryStore
    {
        public ValueTask AddExecution(ExecutionHistoryEntry entry, CancellationToken cancellationToken = default) => default;

        public ValueTask<PagedResult<ExecutionHistoryEntry>> QueryExecutions(ExecutionHistoryQuery query, CancellationToken cancellationToken = default)
            => new(new PagedResult<ExecutionHistoryEntry>([], HasMore: false, 0));

        public ValueTask AddMisfire(MisfireHistoryEntry entry, CancellationToken cancellationToken = default) => default;

        public ValueTask<PagedResult<MisfireHistoryEntry>> QueryMisfires(MisfireHistoryQuery query, CancellationToken cancellationToken = default)
            => new(new PagedResult<MisfireHistoryEntry>([], HasMore: false, 0));

        public ValueTask<int> CountMisfires(string schedulerName, DateTimeOffset since, CancellationToken cancellationToken = default)
            => new(0);
    }
}
