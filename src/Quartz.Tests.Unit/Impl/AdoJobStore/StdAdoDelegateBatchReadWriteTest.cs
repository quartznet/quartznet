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

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The set-shaped delegate members an acquisition round uses, and what each costs.
/// </summary>
/// <remarks>
/// The batched write path cannot be reached from the integration tests — SQLite is the only database
/// those run without Docker and it reports <c>CanCreateBatch = false</c> — so the provider here is the
/// shared stub, and the fallback arm is asserted beside the batched one.
/// </remarks>
public class StdAdoDelegateBatchReadWriteTest
{
    [Test]
    public async Task TheRoundsFiredTriggerRowsGoOutInOneBatch()
    {
        StubBatchingConnection connection = new();
        ConnectionAndTransactionHolder conn = new(connection, null);
        CountingDelegate del = CountingDelegate.Create();

        await del.InsertFiredTriggers(conn, [Trigger("t1"), Trigger("t2"), Trigger("t3")], StoredTriggerState.Acquired, null);

        connection.Batches.Should().ContainSingle("a round's rows have no reason to take a round trip each");
        connection.Batches[0].Commands.Should().HaveCount(3);
        connection.Batches[0].Commands.Should().OnlyContain(
            command => command.CommandText.StartsWith("INSERT INTO QRTZ_FIRED_TRIGGERS", StringComparison.Ordinal));
        del.PreparedCommands.Should().BeEmpty("nothing should have been issued as a standalone command");
    }

    [Test]
    public async Task AProviderThatCannotBatchIssuesExactlyTheStatementsItAlwaysDid()
    {
        StubBatchingConnection connection = new() { SupportsBatching = false };
        ConnectionAndTransactionHolder conn = new(connection, null);
        CountingDelegate del = CountingDelegate.Create();

        await del.InsertFiredTriggers(conn, [Trigger("t1"), Trigger("t2")], StoredTriggerState.Acquired, null);

        connection.Batches.Should().BeEmpty();
        del.PreparedCommands.Should().HaveCount(2,
            "a provider without DbBatch is no worse off than before the batch existed, and no better");
    }

    [Test]
    public async Task ARowWrittenInABatchIsTheRowASingleInsertWouldHaveWritten()
    {
        StubBatchingConnection batching = new();
        CountingDelegate del = CountingDelegate.Create();

        await del.InsertFiredTriggers(new ConnectionAndTransactionHolder(batching, null), [Trigger("t1"), Trigger("t2")], StoredTriggerState.Acquired, null);

        await del.InsertFiredTrigger(new ConnectionAndTransactionHolder(new StubBatchingConnection(), null), Trigger("t1"), StoredTriggerState.Acquired, null);

        batching.Batches[0].Commands[0].CommandText.Should().Be(del.PreparedCommands.Single(),
            "both go through the one builder, so the statement cannot drift between them");
    }

    /// <summary>
    /// A batch of one is one round trip either way, and assembling it costs more than issuing the
    /// command — which matters because the scheduler's default acquisition batch size is one.
    /// </summary>
    [Test]
    public async Task ARoundOfOneIsIssuedAsACommandRatherThanAssembledIntoABatch()
    {
        StubBatchingConnection connection = new();
        CountingDelegate del = CountingDelegate.Create();

        await del.InsertFiredTriggers(new ConnectionAndTransactionHolder(connection, null), [Trigger("t1")], StoredTriggerState.Acquired, null);

        connection.Batches.Should().BeEmpty();
        del.PreparedCommands.Should().ContainSingle()
            .Which.Should().StartWith("INSERT INTO QRTZ_FIRED_TRIGGERS");
    }

    /// <summary>
    /// And the read side of the same round: one key is one statement either way, and the set read pays
    /// for a key-set predicate, a deduplication and a re-sort to answer what the single read answers
    /// directly.
    /// </summary>
    [Test]
    public async Task AKeySetOfOneIsReadAsASingleTrigger()
    {
        SingleKeyReadDelegate del = new();

        List<IOperableTrigger> triggers = await del.SelectTriggers(
            new ConnectionAndTransactionHolder(new StubBatchingConnection(), null),
            [new TriggerKey("t1", "g1")]);

        triggers.Should().ContainSingle().Which.Key.Should().Be(new TriggerKey("t1", "g1"));
        del.SingleTriggerReads.Should().Be(1);
    }

    private static IOperableTrigger Trigger(string name)
    {
        SimpleTriggerImpl trigger = new()
        {
            Key = new TriggerKey(name, "g1"),
            JobKey = new JobKey("j1", "jg1"),
            StartTimeUtc = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero),
            RepeatCount = SimpleTriggerImpl.RepeatIndefinitely,
            RepeatInterval = TimeSpan.FromMinutes(1),
            FireInstanceId = name + "-fire",
        };
        trigger.NextFireTimeUtc = new DateTimeOffset(2026, 3, 1, 13, 0, 0, TimeSpan.Zero);
        return trigger;
    }

    /// <summary>
    /// Counts the single-trigger reads a key-set read makes, leaving <c>SelectTriggers</c> itself as the
    /// shipped one, so that its one-key path is what is under test.
    /// </summary>
    private sealed class SingleKeyReadDelegate : StdAdoDelegate
    {
        public int SingleTriggerReads { get; private set; }

        public override ValueTask<IOperableTrigger> SelectTrigger(
            ConnectionAndTransactionHolder conn,
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            SingleTriggerReads++;
            return new ValueTask<IOperableTrigger>(Trigger(triggerKey.Name));
        }
    }
}
