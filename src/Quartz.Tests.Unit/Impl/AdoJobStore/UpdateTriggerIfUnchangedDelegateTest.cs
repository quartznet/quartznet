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

using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

using FakeItEasy;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// What the shipped delegate sends for a trigger update held to the fire time the row was read with
/// (#4067): the same statement <c>UpdateTrigger</c> sends with the predicate appended and its parameter
/// bound last, a row read with no fire time held with <c>IS NULL</c>, and the schedule row written only
/// when the trigger's row took the update.
/// </summary>
public sealed class UpdateTriggerIfUnchangedDelegateTest
{
    private const string TriggerUpdate = "UPDATE QRTZ_TRIGGERS SET";
    private const string SimpleTriggerUpdate = "QRTZ_SIMPLE_TRIGGERS";

    private static readonly DateTimeOffset AsRead = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task TheStatementIsTheUpdateHeldToTheFireTimeRead()
    {
        RecordingDelegate del = RecordingDelegate.Create(rowsAffected: 1);
        ConnectionAndTransactionHolder conn = new(new StubBatchingConnection { SupportsBatching = false }, null);

        int updated = await del.UpdateTriggerIfUnchanged(conn, Trigger(), StoredTriggerState.Waiting, A.Fake<IJobDetail>(), AsRead);

        updated.Should().Be(1);
        ReadingStubCommand update = del.Commands.Single(x => x.CommandText.StartsWith(TriggerUpdate, StringComparison.Ordinal));
        update.CommandText.Should().EndWith(" AND NEXT_FIRE_TIME = @expectedNextFireTime");

        DbParameter last = update.Parameters.Cast<DbParameter>().Last();
        last.ParameterName.Should().Be("@expectedNextFireTime", "bound last, as the predicate is the last thing the statement names, for providers that bind positionally");
        last.Value.Should().Be(AsRead.UtcTicks);

        del.Commands.Should().Contain(x => x.CommandText.Contains(SimpleTriggerUpdate, StringComparison.Ordinal),
            "the row took the update, so the trigger's own table is written as UpdateTrigger writes it");
    }

    [Test]
    public async Task ARowReadWithNoFireTimeIsHeldWithIsNull()
    {
        RecordingDelegate del = RecordingDelegate.Create(rowsAffected: 1);
        ConnectionAndTransactionHolder conn = new(new StubBatchingConnection { SupportsBatching = false }, null);

        await del.UpdateTriggerIfUnchanged(conn, Trigger(), StoredTriggerState.Complete, A.Fake<IJobDetail>(), expectedNextFireTimeUtc: null);

        ReadingStubCommand update = del.Commands.Single(x => x.CommandText.StartsWith(TriggerUpdate, StringComparison.Ordinal));
        update.CommandText.Should().EndWith(" AND NEXT_FIRE_TIME IS NULL", "a bound NULL would never match the row the editor read");
        update.Parameters.Cast<DbParameter>().Should().NotContain(p => p.ParameterName == "@expectedNextFireTime");
    }

    [Test]
    public async Task ARowThatMovedTakesNothingAndItsScheduleRowIsLeftAlone()
    {
        RecordingDelegate del = RecordingDelegate.Create(rowsAffected: 0);
        ConnectionAndTransactionHolder conn = new(new StubBatchingConnection { SupportsBatching = false }, null);

        int updated = await del.UpdateTriggerIfUnchanged(conn, Trigger(), StoredTriggerState.Waiting, A.Fake<IJobDetail>(), AsRead);

        updated.Should().Be(0, "the row no longer holds the fire time it was read with: a firing moved it, or it went");
        del.Commands.Should().NotContain(x => x.CommandText.Contains(SimpleTriggerUpdate, StringComparison.Ordinal),
            "a schedule row written under a row that moved would put the edit's repeat count over the fire's");
    }

    [Test]
    public async Task ATriggerWithNoRowAtAllIsNotWritten()
    {
        RecordingDelegate del = RecordingDelegate.Create(rowsAffected: 1, triggerType: null);
        ConnectionAndTransactionHolder conn = new(new StubBatchingConnection { SupportsBatching = false }, null);

        int updated = await del.UpdateTriggerIfUnchanged(conn, Trigger(), StoredTriggerState.Waiting, A.Fake<IJobDetail>(), AsRead);

        updated.Should().Be(0);
        del.Commands.Should().NotContain(x => x.CommandText.StartsWith(TriggerUpdate, StringComparison.Ordinal), "as UpdateTrigger, there is nothing to update");
    }

    private static SimpleTriggerImpl Trigger()
    {
        SimpleTriggerImpl trigger = new()
        {
            Key = new TriggerKey("t", "g"),
            StartTimeUtc = AsRead,
            JobKey = new JobKey("j", "jg"),
            RepeatCount = SimpleTriggerImpl.RepeatIndefinitely,
            RepeatInterval = TimeSpan.FromMinutes(1),
        };
        trigger.NextFireTimeUtc = AsRead;
        trigger.JobDataMap.ClearDirtyFlag();
        return trigger;
    }

    /// <summary>
    /// The shipped delegate over commands made of nothing: the type read answers a stored type, and every
    /// write answers the count it is told to.
    /// </summary>
    private sealed class RecordingDelegate : StdAdoDelegate
    {
        private int rowsAffected;
        private string? triggerType;

        public List<ReadingStubCommand> Commands { get; } = [];

        public static RecordingDelegate Create(int rowsAffected, string? triggerType = AdoConstants.TriggerTypeSimple)
        {
            IDbProvider dbProvider = A.Fake<IDbProvider>();
            A.CallTo(() => dbProvider.Metadata).Returns(new DbMetadata { ParameterNamePrefix = "@", BindByName = true });

            RecordingDelegate del = new() { rowsAffected = rowsAffected, triggerType = triggerType };
            del.Initialize(new DriverDelegateContext
            {
                TablePrefix = "QRTZ_",
                InstanceId = "node",
                SchedulerName = "TESTSCHED",
                TypeLoader = new SimpleTypeLoader(),
                DbProvider = dbProvider,
                ObjectSerializer = A.Fake<IObjectSerializer>(),
                TimeProvider = TimeProvider.System,
            });

            return del;
        }

        public override DbCommand PrepareCommand(ConnectionAndTransactionHolder cth, string commandText)
        {
            ReadingStubCommand command = new()
            {
                CommandText = commandText,
                RowsAffected = rowsAffected,
                Reader = commandText.Contains("SELECT TRIGGER_TYPE", StringComparison.Ordinal) ? TypeReader() : null,
            };

            Commands.Add(command);
            cth.Attach(command);
            return command;
        }

        private DbDataReader TypeReader()
        {
            DbDataReader reader = A.Fake<DbDataReader>();
            A.CallTo(() => reader.ReadAsync(A<CancellationToken>._)).Returns(triggerType is not null);
            A.CallTo(() => reader[AdoConstants.ColumnTriggerType]).Returns(triggerType!);
            return reader;
        }
    }

    /// <summary>A command made of nothing that answers a reader or a row count, and keeps what was bound.</summary>
    private sealed class ReadingStubCommand : DbCommand
    {
        private readonly RecordingParameterCollection parameters = new();

        public DbDataReader? Reader { get; init; }

        public int RowsAffected { get; init; }

        [AllowNull]
        public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection => parameters;
        protected override DbTransaction? DbTransaction { get; set; }
        public override bool DesignTimeVisible { get; set; }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery() => RowsAffected;

        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken) => Task.FromResult(RowsAffected);

        public override object? ExecuteScalar() => null;

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter() => new StubDbParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => Reader ?? throw new NotSupportedException(CommandText);

        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
            => Task.FromResult(Reader ?? throw new NotSupportedException(CommandText));
    }
}
