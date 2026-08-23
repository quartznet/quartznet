using System.Data;
using System.Data.Common;

using FakeItEasy;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Impl.Triggers;
using Quartz.Impl;
using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// Covers the batched misfire write path. SQLite reports CanCreateBatch = false so the integration
/// tests cannot reach it, and the providers that can batch all need a live server.
/// </summary>
public class UpdateMisfiredTriggersBatchTest
{
    [Test]
    public async Task UsesOneBatchForTheWholeUpdate_WhenProviderSupportsBatching()
    {
        var connection = new StubBatchingConnection();
        var conn = new ConnectionAndTransactionHolder(connection, null);
        CountingDelegate del = CreateDelegate();

        await del.UpdateMisfiredTriggers(conn, CreateUpdates(5));

        connection.Batches.Should().HaveCount(1, "the whole batch should go out as one round-trip");
        connection.Batches[0].ExecuteCount.Should().Be(1);

        // One narrow TRIGGERS update plus one SIMPLE_TRIGGERS update per trigger.
        connection.Batches[0].Commands.Should().HaveCount(10);
        connection.Batches[0].Commands.Count(x => x.CommandText.Contains("UPDATE {0}TRIGGERS".Replace("{0}", "QRTZ_"))).Should().Be(5);
        connection.Batches[0].Commands.Count(x => x.CommandText.Contains("QRTZ_SIMPLE_TRIGGERS")).Should().Be(5);

        del.PreparedCommands.Should().BeEmpty("nothing should have been issued as a standalone command");
    }

    [Test]
    public async Task BindsParametersOnBatchCommands_WhenProviderCannotCreateThemItself()
    {
        // DbBatchCommand.CreateParameter throws by default and several providers still have not
        // implemented it, so the delegate has to mint parameters from a command instead.
        var connection = new StubBatchingConnection();
        var conn = new ConnectionAndTransactionHolder(connection, null);
        CountingDelegate del = CreateDelegate();

        await del.UpdateMisfiredTriggers(conn, CreateUpdates(1));

        StubBatchCommand triggerUpdate = connection.Batches[0].Commands[0];
        triggerUpdate.Parameters.Count.Should().BeGreaterThan(0, "the statement must not go out unbound");

        var names = triggerUpdate.Parameters.Cast<DbParameter>().Select(x => x.ParameterName).ToArray();
        names.Should().Contain("@triggerState");
        names.Should().Contain("@triggerName");
        names.Should().Contain("@triggerGroup");
    }

    [Test]
    public async Task FallsBackToIndividualStatements_WhenProviderCannotBatch()
    {
        var connection = new StubBatchingConnection { SupportsBatching = false };
        var conn = new ConnectionAndTransactionHolder(connection, null);
        CountingDelegate del = CreateDelegate();

        await del.UpdateMisfiredTriggers(conn, CreateUpdates(3));

        connection.Batches.Should().BeEmpty();
        del.PreparedCommands.Should().HaveCount(6, "each trigger still needs its two statements");
    }

    /// <summary>
    /// A batch fails as a unit, so one bad trigger would otherwise block the whole recovery pass.
    /// </summary>
    [Test]
    public async Task FallsBackToIndividualStatements_WhenBatchExecutionFails()
    {
        var connection = new StubBatchingConnection { FailBatchExecution = true };
        var conn = new ConnectionAndTransactionHolder(connection, null);
        CountingDelegate del = CreateDelegate();

        await del.UpdateMisfiredTriggers(conn, CreateUpdates(3));

        connection.Batches.Should().HaveCount(1, "the batch should have been attempted");
        del.PreparedCommands.Should().HaveCount(6, "every statement should still have been issued after the batch failed");
    }

    /// <summary>
    /// Full recovery runs unbounded, so a pass can produce thousands of statements. They must not all be
    /// handed to the provider as one batch.
    /// </summary>
    [Test]
    public async Task ChunksLargeBatches()
    {
        var connection = new StubBatchingConnection();
        var conn = new ConnectionAndTransactionHolder(connection, null);
        CountingDelegate del = CreateDelegate();

        // 250 triggers is 500 statements, which has to span more than one batch.
        await del.UpdateMisfiredTriggers(conn, CreateUpdates(250));

        connection.Batches.Should().HaveCountGreaterThan(1);
        connection.Batches.Should().OnlyContain(x => x.Commands.Count <= 100);
        connection.Batches.Sum(x => x.Commands.Count).Should().Be(500, "every statement should still be issued exactly once");
        del.PreparedCommands.Should().BeEmpty();
    }

    [Test]
    public async Task DoesNothingForAnEmptyBatch()
    {
        var connection = new StubBatchingConnection();
        var conn = new ConnectionAndTransactionHolder(connection, null);
        CountingDelegate del = CreateDelegate();

        await del.UpdateMisfiredTriggers(conn, []);

        connection.Batches.Should().BeEmpty();
        del.PreparedCommands.Should().BeEmpty();
    }

    private static List<MisfiredTriggerUpdate> CreateUpdates(int count)
    {
        var updates = new List<MisfiredTriggerUpdate>();
        for (var i = 0; i < count; i++)
        {
            var trigger = new SimpleTriggerImpl
            {
                Key = new TriggerKey("t" + i, "g"),
                StartTimeUtc = DateTimeOffset.UtcNow,
                JobKey = new JobKey("j" + i, "jg"),
                RepeatCount = SimpleTriggerImpl.RepeatIndefinitely,
                RepeatInterval = TimeSpan.FromMinutes(1)
            };
            trigger.NextFireTimeUtc = DateTimeOffset.UtcNow.AddMinutes(1);

            updates.Add(new MisfiredTriggerUpdate(trigger, StoredTriggerState.Waiting, null));
        }

        return updates;
    }

    private static CountingDelegate CreateDelegate() => CountingDelegate.Create();
}
