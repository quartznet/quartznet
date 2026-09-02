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

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The compare-and-swap behind node affinity's auto-pin claim and its steal-on-failover, against a
/// real database.
/// </summary>
/// <remarks>
/// <para>
/// <c>node-affinity.md</c> promises that a claim never clobbers a concurrent re-pin: the write only
/// happens while the row still holds what the claiming node read at acquisition time. That is one
/// statement, and until now it was reached only end to end, on PostgreSQL — six product-code
/// references and no test of its own. What a two-node fixture cannot easily arrange is the losing
/// half, which is the half the promise is about.
/// </para>
/// <para>
/// SQLite is a file, so the statement runs for real here. The rows are written by a scheduler through
/// the ordinary configuration path, so what the swap compares against is what the store actually
/// stores rather than what this test thinks it stores.
/// </para>
/// </remarks>
public sealed class PreferredNodeConditionalUpdateSqliteTest
{
    private const string SchedulerName = "preferred-node-cas";
    private const string FirstNode = "node-1";
    private const string SecondNode = "node-2";

    private static readonly TriggerKey PinnedTrigger = new("pinned", "affinity");

    private string databaseFile = null!;
    private string connectionString = null!;
    private ServiceProvider? container;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-preferred-node-{Guid.NewGuid():N}.db");
        connectionString = $"Data Source={databaseFile}";
    }

    [TearDown]
    public async Task DeleteDatabase()
    {
        if (container is not null)
        {
            await container.DisposeAsync();
            container = null;
        }

        SqliteConnection.ClearAllPools();

        if (File.Exists(databaseFile))
        {
            File.Delete(databaseFile);
        }
    }

    /// <summary>
    /// The claim: a trigger asking for an automatic pin is taken by the node that fires it first, and
    /// the row afterwards names that node and remembers the pin was handed out rather than asked for.
    /// </summary>
    [Test]
    public async Task ANodeClaimsAnAutomaticPinAndTheRowSaysWhoAndHow()
    {
        await GivenATriggerPinned(PreferredNode.Auto);

        int claimed = await Swap(expected: PreferredNode.Auto, next: PreferredNode.ClaimedBy(FirstNode));

        claimed.Should().Be(1, "the row still held the sentinel this node read, so the claim took it");

        (await StoredPin()).Should().Be((FirstNode, true),
            "the node that claimed it is named, and the pin stays flagged automatic so that failover "
            + "can release it when the node stops checking in — an explicit pin is kept instead");
    }

    /// <summary>
    /// And the losing half: a second node whose claim is built on what it read before the first node
    /// won changes nothing, and is told so.
    /// </summary>
    /// <remarks>
    /// Zero rather than an exception is the answer the fire path is written against — the caller leaves
    /// the concurrent value in place and reloads on the next acquisition. An implementation that
    /// dropped the two expected-value predicates would pass every clustered test there is and fail
    /// here, because the loser would simply overwrite the winner.
    /// </remarks>
    [Test]
    public async Task AClaimBuiltOnAStalePinChangesNothingAndSaysSo()
    {
        await GivenATriggerPinned(PreferredNode.Auto);

        await Swap(expected: PreferredNode.Auto, next: PreferredNode.ClaimedBy(FirstNode));

        int lost = await Swap(expected: PreferredNode.Auto, next: PreferredNode.ClaimedBy(SecondNode));

        lost.Should().Be(0,
            "the row no longer holds the sentinel this node read, so its claim is refused rather than "
            + "clobbering the node that got there first");

        (await StoredPin()).Should().Be((FirstNode, true), "and the winner still holds the pin");
    }

    /// <summary>
    /// The steal that does win: a node that read the dead node's automatic pin and swaps against
    /// exactly that takes it, which is what makes sticky failover converge on a live node.
    /// </summary>
    [Test]
    public async Task AStealAgainstThePinItActuallyReadTakesIt()
    {
        await GivenATriggerPinned(PreferredNode.Auto);
        await Swap(expected: PreferredNode.Auto, next: PreferredNode.ClaimedBy(FirstNode));

        int stolen = await Swap(
            expected: PreferredNode.ClaimedBy(FirstNode),
            next: PreferredNode.ClaimedBy(SecondNode));

        stolen.Should().Be(1, "the row holds what this node read, so the steal is the CAS succeeding");
        (await StoredPin()).Should().Be((SecondNode, true));
    }

    /// <summary>
    /// A transition expecting no pin at all matches nothing, whatever the row holds.
    /// </summary>
    /// <remarks>
    /// The expected pin is compared with <c>=</c>, and SQL equality against <see langword="null" /> is
    /// never true — so a caller that built a transition from <see cref="PreferredNode.None" /> gets
    /// zero rows rather than clearing somebody's pin. The claim paths never build one, and this is what
    /// says what would happen if one did.
    /// </remarks>
    [Test]
    public async Task ATransitionExpectingNoPinMatchesNoRow()
    {
        await GivenATriggerPinned(PreferredNode.None);

        int updated = await Swap(expected: PreferredNode.None, next: PreferredNode.ClaimedBy(FirstNode));

        updated.Should().Be(0,
            "an unpinned row is stored as NULL, and no value is equal to NULL — a caller reading this "
            + "as failure rather than as a race is reading it correctly, because it can never succeed");

        (await StoredPin()).Should().Be((null, false), "and the row is untouched");
    }

    /// <summary>
    /// Writes a trigger through an ordinary scheduler, so the row the swap works on is one the store
    /// wrote.
    /// </summary>
    private async Task GivenATriggerPinned(PreferredNode pin)
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = SchedulerName;
                options.InstanceId = FirstNode;
            });

            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, connectionString);
                store.ProvisionSchema();
            });
        });

        container = services.BuildServiceProvider();

        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        await scheduler.ScheduleJob(
            JobBuilder.Create<PinnedJob>().WithIdentity("pinned", "affinity").Build(),
            TriggerBuilder.Create()
                .WithIdentity(PinnedTrigger)
                .StartAt(DateTimeOffset.UtcNow.AddYears(1))
                .WithPreferredNode(pin)
                .Build());
    }

    /// <summary>
    /// Runs the conditional update the fire path runs, through the dialect delegate that writes it.
    /// </summary>
    private async Task<int> Swap(PreferredNode expected, PreferredNode next)
    {
        SQLiteDelegate driverDelegate = new();
        driverDelegate.Initialize(new DriverDelegateContext
        {
            TablePrefix = AdoConstants.DefaultTablePrefix,
            SchedulerName = SchedulerName,
            InstanceId = SecondNode,
            DbProvider = Provider(),
            TypeLoader = new SimpleTypeLoader(),
            ObjectSerializer = new SystemTextJsonObjectSerializer(),
        });

        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync();
        using ConnectionAndTransactionHolder holder = new(connection, transaction: null);

        return await driverDelegate.UpdateTriggerPreferredNodeConditional(
            holder,
            PinnedTrigger,
            new PreferredNodeTransition { Expected = expected, New = next });
    }

    private IDbProvider Provider()
    {
        DbMetadata metadata = DbMetadataResolver.BuiltIn().ResolveWithoutTypes("SQLite-Microsoft");
        return new ProviderFactoryDbProvider(metadata, SqliteFactory.Instance, connectionString);
    }

    /// <summary>
    /// The pair the triggers table holds, read without going through the delegate that wrote it.
    /// </summary>
    private async Task<(string? Node, bool Automatic)> StoredPin()
    {
        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT PREFERRED_NODE, PREFERRED_NODE_AUTO FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = @name AND TRIGGER_GROUP = @group";
        command.Parameters.AddWithValue("@name", PinnedTrigger.Name);
        command.Parameters.AddWithValue("@group", PinnedTrigger.Group);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();

        (await reader.ReadAsync()).Should().BeTrue("the trigger this fixture scheduled is still stored");

        string? node = await reader.IsDBNullAsync(0) ? null : reader.GetString(0);
        bool automatic = !await reader.IsDBNullAsync(1) && reader.GetBoolean(1);

        return (node, automatic);
    }

    /// <summary>
    /// Public with a public constructor, because the store hands the job factory nothing but the type
    /// name it read back out of <c>JOB_CLASS_NAME</c>.
    /// </summary>
    public sealed class PinnedJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
