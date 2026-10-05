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

using FakeItEasy;

using JasperFx;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

using Quartz.Weasel;
using Quartz.Weasel.SQLite;

using Weasel.Core;

namespace Quartz.Tests.Unit.Weasel;

/// <summary>
/// The apply of the dialects that take no lock (#4002): only a failure a lost race raises is read again and
/// retried, and whatever ends an apply surfaces as a <see cref="SchedulerException" /> naming the scheduler
/// and the database. Run on SQLite files, through SQLite's own classifier.
/// </summary>
public sealed class LockFreeApplyTest
{
    private const int SchemaApplyRetrying = 10008;
    private const int SchemaAppliedByAnotherProcess = 10009;

    private SqliteTestDatabase database = null!;
    private FakeLoggerProvider logs = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        database = new SqliteTestDatabase("weasel-lock-free");
        logs = new FakeLoggerProvider();
    }

    [TearDown]
    public void DeleteDatabase()
    {
        logs.Dispose();
        database.Dispose();
    }

    /// <summary>
    /// A rebuild whose restored foreign key a row rejects fails the same way on every attempt, so it is not
    /// one to read the schema again for, let alone to apply again after a pause.
    /// </summary>
    [Test]
    public async Task AFailureNoRaceCausesFailsAtOnceAsASchedulerException()
    {
        await SqliteSchema.CreateWithTriggersTableMissingItsForeignKeyAsync(database.ConnectionString, extraDefinitions: null);
        await SqliteSchema.ExecuteAsync(database.ConnectionString, """
            INSERT INTO QRTZ_TRIGGERS (SCHED_NAME, TRIGGER_NAME, TRIGGER_GROUP, JOB_NAME, JOB_GROUP, TRIGGER_STATE, TRIGGER_TYPE, START_TIME)
              VALUES ('elsewhere', 'orphan', 'group', 'no-such-job', 'group', 'WAITING', 'SIMPLE', 1);
            """);

        await using WeaselSqliteContainer weasel = await ContainerAsync("weasel-no-race");

        Func<Task> apply = () => weasel.Database.ApplyAllConfiguredChangesToDatabaseAsync();

        SchedulerException failure = (await apply.Should().ThrowAsync<SchedulerException>()).Which;
        failure.Message.Should().Contain("'weasel-no-race'").And.Contain(weasel.Database.Describe().DatabaseUri().ToString())
            .And.Contain("not retried");
        failure.InnerException.Should().BeOfType<InvalidOperationException>("the failure that ended the apply is kept whole")
            .Which.Message.Should().Match("*QRTZ_TRIGGERS*rolled back*");

        EventIds().Should().NotContain([SchemaApplyRetrying, SchemaAppliedByAnotherProcess],
            "a failure no race causes is not reported as one, nor is the schema read again for it");
    }

    /// <summary>
    /// The applier that loses: it planned its <c>ADD COLUMN</c>s against a 4.2 schema, another process
    /// added them first, and its own fail on the first duplicate. Reading the schema again finds nothing
    /// left to do.
    /// </summary>
    [Test]
    public async Task ALostRaceAnotherProcessFinishedSettles()
    {
        await SqliteSchema.CreateWithBaselineAsync(database.ConnectionString, "4.2");

        await using WeaselSqliteContainer loser = await ContainerAsync("weasel-loser");
        await using WeaselSqliteContainer winner = await WeaselSqliteContainer.CreateAsync(database.ConnectionString, "weasel-winner");
        IQuartzWeaselDatabase losing = (IQuartzWeaselDatabase) loser.Database;

        int applies = 0;
        SchemaPatchDifference result = await LockFreeApply.ApplyAsync(
            losing,
            async cancellationToken =>
            {
                applies++;
                SchemaMigration planned = await losing.CreateMigrationAsync(cancellationToken);
                planned.Difference.Should().NotBe(SchemaPatchDifference.None, "the premise: the loser has columns to add");

                (await winner.Database.ApplyAllConfiguredChangesToDatabaseAsync(ct: cancellationToken)).Should().NotBe(SchemaPatchDifference.None);

                await using SqliteConnection connection = new(database.ConnectionString);
                await connection.OpenAsync(cancellationToken);
                await losing.Migrator.ApplyAllAsync(connection, planned, AutoCreate.CreateOrUpdate, losing.Context.MigrationLogger, cancellationToken);
                return planned.Difference;
            },
            SqliteQuartzDatabase.IsLostRace,
            attempts: 3,
            TimeSpan.Zero,
            TimeProvider.System);

        result.Should().Be(SchemaPatchDifference.None, "another process made every change");
        applies.Should().Be(1, "nothing was left to apply again");
        EventIds().Should().Equal([SchemaApplyRetrying, SchemaAppliedByAnotherProcess]);
        logs.Collector.GetSnapshot().Single(x => x.Id.Id == SchemaApplyRetrying).Exception!.ToString().Should().Contain("duplicate column name",
            "the premise: SQLite's own error for an ADD COLUMN that lost its race");
    }

    /// <summary>
    /// A lost race whose schema still differs is applied again, and the last failure stands once the
    /// attempts run out.
    /// </summary>
    [Test]
    public async Task ALostRaceWhoseSchemaStillDiffersIsRetriedUntilTheAttemptsRunOut()
    {
        await using WeaselSqliteContainer weasel = await ContainerAsync("weasel-still-racing");
        IQuartzWeaselDatabase racing = (IQuartzWeaselDatabase) weasel.Database;

        List<SqliteException> thrown = [];
        Func<Task> apply = () => LockFreeApply.ApplyAsync(
            racing,
            _ =>
            {
                SqliteException race = new($"SQLite Error 1: 'duplicate column name: RESULT{thrown.Count}'.", 1);
                thrown.Add(race);
                throw race;
            },
            SqliteQuartzDatabase.IsLostRace,
            attempts: 3,
            TimeSpan.Zero,
            TimeProvider.System);

        SchedulerException failure = (await apply.Should().ThrowAsync<SchedulerException>()).Which;
        failure.Message.Should().Contain("'weasel-still-racing'").And.Contain("3 attempts");
        failure.InnerException.Should().BeSameAs(thrown[^1], "the last failure is the one that stands");

        thrown.Should().HaveCount(3);
        EventIds().Should().Equal([SchemaApplyRetrying, SchemaApplyRetrying, SchemaApplyRetrying],
            "the empty database still differs from the model after every attempt");
    }

    [Test]
    public async Task ReadingTheSchemaAgainThatFailsEndsTheApply()
    {
        string unreachable = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(Path.GetTempPath(), $"quartz-no-such-directory-{Guid.NewGuid():N}", "quartz.db"),
            Pooling = false,
        }.ToString();

        await using WeaselSqliteContainer weasel = await WeaselSqliteContainer.CreateAsync(
            unreachable,
            "weasel-unreadable",
            configureServices: services => services.AddLogging(logging => logging.AddProvider(logs)));

        Func<Task> apply = () => LockFreeApply.ApplyAsync(
            (IQuartzWeaselDatabase) weasel.Database,
            _ => throw new SqliteException("SQLite Error 1: 'table QRTZ_JOB_STATUS already exists'.", 1),
            SqliteQuartzDatabase.IsLostRace,
            attempts: 3,
            TimeSpan.Zero,
            TimeProvider.System);

        SchedulerException failure = (await apply.Should().ThrowAsync<SchedulerException>()).Which;
        failure.Message.Should().Contain("'weasel-unreadable'").And.Contain("reading the schema again");
        failure.InnerException.Should().BeOfType<SqliteException>("the directory the file would be in does not exist")
            .Which.SqliteErrorCode.Should().Be(14, "SQLITE_CANTOPEN");
    }

    [Test]
    public async Task ACancelledApplyIsNeitherRetriedNorWrapped()
    {
        await using WeaselSqliteContainer weasel = await ContainerAsync("weasel-cancelled");
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        int applies = 0;
        Func<Task> apply = () => LockFreeApply.ApplyAsync(
            (IQuartzWeaselDatabase) weasel.Database,
            cancellationToken =>
            {
                applies++;
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(SchemaPatchDifference.None);
            },
            SqliteQuartzDatabase.IsLostRace,
            attempts: 3,
            TimeSpan.Zero,
            TimeProvider.System,
            cancelled.Token);

        await apply.Should().ThrowAsync<OperationCanceledException>();
        applies.Should().Be(1);
        EventIds().Should().BeEmpty();
    }

    /// <summary>
    /// SQLite's classifier against SQLite's own errors: the two statements a lost race fails, and others
    /// that share their <c>SQLITE_ERROR</c> code without being one.
    /// </summary>
    [TestCase("ALTER TABLE T ADD COLUMN NOTE TEXT", true, TestName = "An ADD COLUMN another applier made first")]
    [TestCase("CREATE TABLE T (ID INTEGER)", true, TestName = "A CREATE TABLE another applier made first")]
    [TestCase("CREATE INDEX IX_T ON T (ID)", true, TestName = "A CREATE INDEX another applier made first")]
    [TestCase("SELECT * FROM NO_SUCH_TABLE", false, TestName = "A missing table")]
    [TestCase("ALTER TABLE T ADD COLUMN", false, TestName = "A syntax error")]
    [TestCase("INSERT INTO T (ID) VALUES (NULL)", false, TestName = "A refused row")]
    public async Task OnlyTheErrorsALostRaceRaisesAreRetried(string statement, bool lostRace)
    {
        await SqliteSchema.ExecuteAsync(database.ConnectionString, """
            CREATE TABLE T (ID INTEGER NOT NULL);
            ALTER TABLE T ADD COLUMN NOTE TEXT;
            CREATE INDEX IX_T ON T (ID);
            """);

        Func<Task> execute = () => SqliteSchema.ExecuteAsync(database.ConnectionString, statement);
        SqliteException error = (await execute.Should().ThrowAsync<SqliteException>()).Which;

        SqliteQuartzDatabase.IsLostRace(error).Should().Be(lostRace, error.Message);
        LockFreeApply.ProviderError(new SchedulerException("wrapped", new InvalidOperationException("wrapped again", error)))
            .Should().BeSameAs(error, "the provider's error is found under whatever Weasel and Quartz wrapped it in");
    }

    [Test]
    public void AFailureWithNoProviderErrorIsNoRace()
    {
        LockFreeApply.ProviderError(new InvalidOperationException("Weasel refused it")).Should().BeNull();
        SqliteQuartzDatabase.IsLostRace(A.Fake<DbException>()).Should().BeFalse("only SQLite's own errors are SQLite's to classify");
    }

    private Task<WeaselSqliteContainer> ContainerAsync(string schedulerName) => WeaselSqliteContainer.CreateAsync(
        database.ConnectionString,
        schedulerName,
        configureServices: services => services.AddLogging(logging => logging.AddProvider(logs)));

    private List<int> EventIds() => logs.Collector.GetSnapshot()
        .Where(x => x.Category == "Quartz.Weasel" && x.Id.Id is SchemaApplyRetrying or SchemaAppliedByAnotherProcess)
        .Select(x => x.Id.Id)
        .ToList();
}
