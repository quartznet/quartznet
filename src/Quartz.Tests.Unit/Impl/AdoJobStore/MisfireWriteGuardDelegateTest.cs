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

using System.Data.Common;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// What the shipped delegate sends for a misfire write held to the fire time the row was read with
/// (#4067): the statement, the parameter it binds last, which rows it reports written, and the schedule
/// rows it writes only for those.
/// </summary>
/// <remarks>
/// Through <see cref="StubBatchingConnection" />, because the batched path cannot be reached from SQLite
/// and every provider that batches needs a live server; the stub also says what each statement changed,
/// which is how the delegate tells a row that took from one that moved.
/// </remarks>
public sealed class MisfireWriteGuardDelegateTest
{
    private const string TriggerUpdate = "UPDATE QRTZ_TRIGGERS SET NEXT_FIRE_TIME";
    private const string SimpleTriggerUpdate = "QRTZ_SIMPLE_TRIGGERS";
    private const string Guard = " AND NEXT_FIRE_TIME = @expectedNextFireTime";

    private static readonly DateTimeOffset Missed = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task AGuardedWriteNamesTheFireTimeAsReadLast()
    {
        StubBatchingConnection connection = new() { SupportsBatching = false };
        ConnectionAndTransactionHolder conn = new(connection, null);
        CountingDelegate del = CountingDelegate.Create();
        del.RowsAffected = _ => 1;

        await del.UpdateMisfiredTriggersIfUnchanged(conn, [Update(0, Missed, withOriginal: false), Update(1, Missed, withOriginal: true)]);

        List<StubDbCommand> rows = del.Prepared.Where(x => x.CommandText.StartsWith(TriggerUpdate, StringComparison.Ordinal)).ToList();
        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(x => x.CommandText.EndsWith(Guard, StringComparison.Ordinal),
            "both flavours of the narrow update end with the fire time predicate");
        rows[1].CommandText.Should().Contain("MISFIRE_ORIG_FIRE_TIME", "a fire-now policy's original fire time keeps its own flavour");

        foreach (StubDbCommand row in rows)
        {
            DbParameter last = row.Parameters.Cast<DbParameter>().Last();
            last.ParameterName.Should().Be("@expectedNextFireTime", "bound last, as the predicate is the last thing the statement names, for providers that bind positionally");
            last.Value.Should().Be(Missed.UtcTicks);
        }
    }

    [Test]
    public async Task AnUpdateWithNoExpectationIsWrittenUnguarded()
    {
        StubBatchingConnection connection = new() { SupportsBatching = false };
        ConnectionAndTransactionHolder conn = new(connection, null);
        CountingDelegate del = CountingDelegate.Create();
        del.RowsAffected = _ => 1;

        List<TriggerKey> written = await del.UpdateMisfiredTriggersIfUnchanged(conn, [Update(0, expected: null, withOriginal: false)]);

        written.Should().ContainSingle();
        del.Prepared.Single(x => x.CommandText.StartsWith(TriggerUpdate, StringComparison.Ordinal)).CommandText
            .Should().NotContain("expectedNextFireTime", "no expectation is a write whatever the row holds, as UpdateMisfiredTriggers has always made");
    }

    [Test]
    public async Task ABatchedWriteReportsTheRowsThatTookAndWritesTheirScheduleRowsAlone()
    {
        // The first statement's row moved under the pass; the second took.
        StubBatchingConnection connection = new() { RecordsAffected = i => i == 0 ? 0 : 1 };
        ConnectionAndTransactionHolder conn = new(connection, null);
        CountingDelegate del = CountingDelegate.Create();

        List<TriggerKey> written = await del.UpdateMisfiredTriggersIfUnchanged(conn, [Update(0, Missed, withOriginal: false), Update(1, Missed, withOriginal: false)]);

        written.Select(x => x.Name).Should().Equal(["t1"], "the row whose statement changed nothing was fired meanwhile");

        connection.Batches.Should().HaveCount(2, "the rows go first, and the schedule rows of those that took go after, decided by the counts");
        connection.Batches[0].Commands.Should().HaveCount(2).And.OnlyContain(x => x.CommandText.StartsWith(TriggerUpdate, StringComparison.Ordinal));
        connection.Batches[1].Commands.Should().ContainSingle()
            .Which.CommandText.Should().Contain(SimpleTriggerUpdate, "a schedule row written under a row the round fired would put the misfire's count over the fire's");
        connection.Batches[1].Commands[0].Parameters.Cast<DbParameter>().Should().Contain(p => p.ParameterName == "@triggerName" && (string) p.Value! == "t1");
    }

    [Test]
    public async Task ABatchedWriteWhoseTotalSaysEveryRowTookWritesEveryScheduleRow()
    {
        StubBatchingConnection connection = new();
        ConnectionAndTransactionHolder conn = new(connection, null);
        CountingDelegate del = CountingDelegate.Create();

        List<TriggerKey> written = await del.UpdateMisfiredTriggersIfUnchanged(conn, [Update(0, Missed, withOriginal: false), Update(1, Missed, withOriginal: false), Update(2, Missed, withOriginal: false)]);

        written.Select(x => x.Name).Should().Equal(["t0", "t1", "t2"], "a total equal to the statement count is every row taking, whether or not the provider counts per command");
        connection.Batches.Should().HaveCount(2);
        connection.Batches[1].Commands.Should().HaveCount(3);
    }

    [Test]
    public async Task AnUnbatchedWriteCountsEachStatementItself()
    {
        StubBatchingConnection connection = new() { SupportsBatching = false };
        ConnectionAndTransactionHolder conn = new(connection, null);
        CountingDelegate del = CountingDelegate.Create();
        del.RowsAffected = text => text.Contains("'t0'", StringComparison.Ordinal) ? 0 : 1;

        // The stub binds parameters rather than splicing names into the text, so the row that moved is
        // told apart by what was bound: the first row's statement answers zero.
        int rowStatements = 0;
        del.RowsAffected = text => text.StartsWith(TriggerUpdate, StringComparison.Ordinal) ? (rowStatements++ == 0 ? 0 : 1) : 1;

        List<TriggerKey> written = await del.UpdateMisfiredTriggersIfUnchanged(conn, [Update(0, Missed, withOriginal: false), Update(1, Missed, withOriginal: false)]);

        written.Select(x => x.Name).Should().Equal(["t1"]);
        del.PreparedCommands.Count(x => x.StartsWith(TriggerUpdate, StringComparison.Ordinal)).Should().Be(2);
        del.PreparedCommands.Count(x => x.Contains(SimpleTriggerUpdate, StringComparison.Ordinal)).Should().Be(1, "only the row that took gets its schedule row written");
        connection.Batches.Should().BeEmpty();
    }

    [Test]
    public async Task ABatchThatFailsIsRunOneStatementAtATimeAndStillCounted()
    {
        StubBatchingConnection connection = new() { FailBatchExecution = true };
        ConnectionAndTransactionHolder conn = new(connection, null);
        CountingDelegate del = CountingDelegate.Create();
        del.RowsAffected = _ => 1;

        List<TriggerKey> written = await del.UpdateMisfiredTriggersIfUnchanged(conn, [Update(0, Missed, withOriginal: false), Update(1, Missed, withOriginal: false)]);

        written.Should().HaveCount(2);
        connection.Batches.Should().NotBeEmpty("the batch was attempted");
        del.PreparedCommands.Count(x => x.StartsWith(TriggerUpdate, StringComparison.Ordinal)).Should().Be(2, "and then each row's statement went out on its own, as the unguarded write's fallback does");
    }

    [Test]
    public async Task ATriggerWithNoScheduleRowIsOneBatch()
    {
        StubBatchingConnection connection = new();
        ConnectionAndTransactionHolder conn = new(connection, null);
        CountingDelegate del = CountingDelegate.Create();

        CronTriggerImpl cron = new("cron", "g", "0 0 12 * * ?") { JobKey = new JobKey("j", "jg") };
        cron.NextFireTimeUtc = Missed.AddDays(1);
        MisfiredTriggerUpdate update = new(cron, StoredTriggerState.Waiting, null) { ExpectedNextFireTimeUtc = Missed };

        await del.UpdateMisfiredTriggersIfUnchanged(conn, [update, update]);

        connection.Batches.Should().HaveCount(1, "a cron trigger changes nothing outside its row on a misfire, so there is no second round");
    }

    [Test]
    public async Task NothingIsSentForAnEmptyBatch()
    {
        StubBatchingConnection connection = new();
        ConnectionAndTransactionHolder conn = new(connection, null);
        CountingDelegate del = CountingDelegate.Create();

        List<TriggerKey> written = await del.UpdateMisfiredTriggersIfUnchanged(conn, []);

        written.Should().BeEmpty();
        connection.Batches.Should().BeEmpty();
        del.PreparedCommands.Should().BeEmpty();
    }

    private static MisfiredTriggerUpdate Update(int index, DateTimeOffset? expected, bool withOriginal)
    {
        SimpleTriggerImpl trigger = new()
        {
            Key = new TriggerKey("t" + index, "g"),
            StartTimeUtc = Missed,
            JobKey = new JobKey("j" + index, "jg"),
            RepeatCount = SimpleTriggerImpl.RepeatIndefinitely,
            RepeatInterval = TimeSpan.FromMinutes(1),
        };
        trigger.NextFireTimeUtc = Missed.AddMinutes(1);

        return new MisfiredTriggerUpdate(trigger, StoredTriggerState.Waiting, withOriginal ? Missed : null)
        {
            ExpectedNextFireTimeUtc = expected,
        };
    }
}
