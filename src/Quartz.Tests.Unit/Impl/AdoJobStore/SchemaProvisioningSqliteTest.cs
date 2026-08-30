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

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// A store creating its own schema, against a real database.
/// </summary>
/// <remarks>
/// <para>
/// SQLite is a file, so a whole empty database costs a temporary path and the scheduler that
/// provisions it runs in this process. That makes it the one dialect whose provisioning can be
/// exercised end to end without a container, which is why the case lives here rather than only in
/// <c>SchemaProvisioningTest</c>, where the other five dialects are.
/// </para>
/// <para>
/// What is under test is the store's decision and the round trip, not the DDL: the SQL that runs is
/// the same generated script <c>SchemaScriptTest</c> compares with the fresh-install one, and the
/// integration legs are where the resulting catalog is compared against a real one per dialect.
/// </para>
/// </remarks>
public sealed class SchemaProvisioningSqliteTest
{
    private string databaseFile = null!;
    private string connectionString = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        // Not created here: an empty path is a valid SQLite database the moment something opens it,
        // which is exactly the "nothing is there yet" case provisioning is for.
        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-provisioning-{Guid.NewGuid():N}.db");
        connectionString = $"Data Source={databaseFile}";
    }

    [TearDown]
    public void DeleteDatabase()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(databaseFile))
        {
            File.Delete(databaseFile);
        }
    }

    [Test]
    public async Task AStoreThatProvisionsStartsAgainstAnEmptyDatabase()
    {
        await ScheduleFireAndReadBack(nameof(AStoreThatProvisionsStartsAgainstAnEmptyDatabase));
    }

    [Test]
    public async Task AStoreThatDoesNotProvisionRefusesToStartAgainstAnEmptyDatabase()
    {
        Func<Task> act = () => ScheduleFireAndReadBack(
            nameof(AStoreThatDoesNotProvisionRefusesToStartAgainstAnEmptyDatabase), provision: false);

        await act.Should().ThrowAsync<SchedulerException>().WithMessage("*schema validation failed*",
            "Validate is still the default, so an empty database is a startup failure naming the schema "
            + "rather than a scheduler that starts and never fires");
    }

    [Test]
    public async Task ProvisioningASchemaThatIsAlreadyThereChangesNothing()
    {
        await ScheduleFireAndReadBack("first");

        // Everything the first scheduler wrote is still in the file, so a create that was not guarded
        // would fail, and one that recreated a table would take the rows with it.
        await ScheduleFireAndReadBack("second");

        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync();

        (await Scalar(connection, "SELECT COUNT(*) FROM QRTZ_JOB_DETAILS")).Should().Be(2,
            "provisioning creates what is missing and touches nothing else, so the first scheduler's job "
            + "is still there beside the second's");
    }

    [Test]
    public async Task ProvisioningCreatesEveryObjectUnderTheConfiguredPrefix()
    {
        await ScheduleFireAndReadBack(nameof(ProvisioningCreatesEveryObjectUnderTheConfiguredPrefix), tablePrefix: "QRTZP_");

        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync();

        (await Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name LIKE 'QRTZP!_%' ESCAPE '!'"))
            .Should().Be(12, "every table Quartz reads or writes is created, under the prefix that was asked for");

        (await Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND name LIKE 'QRTZP!_DELETE!_%' ESCAPE '!'"))
            .Should().Be(4,
                "SQLite enforces no foreign keys unless the connection asks it to, so the four delete "
                + "triggers are what keeps the type tables from leaking rows — and they carry the prefix, "
                + "which is what lets a second schema live in the same file");

        (await Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE name LIKE 'QRTZ!_%' ESCAPE '!'"))
            .Should().Be(0,
                "nothing may be created under the default prefix when another one was configured — a "
                + "literal QRTZ_ left in the script is exactly the object two schedulers would fight over");
    }

    [Test]
    public async Task ADelegateWithNoSchemaScriptSaysWhatToRunInstead()
    {
        Func<Task> act = () => ScheduleFireAndReadBack(
            nameof(ADelegateWithNoSchemaScriptSaysWhatToRunInstead),
            configure: store => store.UseDriverDelegate<ScriptlessDelegate>());

        SchedulerException failure = (await act.Should().ThrowAsync<SchedulerException>())
            .WithMessage("*database/tables/*")
            .WithMessage("*SchemaProvisioning.Validate*",
                "a reader who cannot be granted DDL needs the file to run and the setting to go back to, "
                + "not only the news that something failed")
            .Which;

        failure.InnerException.Should().BeOfType<JobPersistenceException>()
            .Which.Message.Should().Contain(nameof(ScriptlessDelegate),
                "the delegate that has no script is the thing to change, so it is named");
    }

    private async Task ScheduleFireAndReadBack(
        string schedulerName,
        bool provision = true,
        string? tablePrefix = null,
        Action<IPersistentStoreBuilder>? configure = null)
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = schedulerName;
                options.InstanceId = "one";
            });

            q.UsePersistentStore(store =>
            {
                // Before the database is chosen: choosing one registers a driver delegate, and the
                // registrations are TryAdd, so the first one to name a delegate is the one that runs.
                configure?.Invoke(store);

                store.UseSqlite(SqliteFactory.Instance, connectionString);

                if (tablePrefix is not null)
                {
                    store.ConfigureStore(options => options.TablePrefix = tablePrefix);
                }

                if (provision)
                {
                    store.ProvisionSchema();
                }
            });
        });

        await using ServiceProvider container = services.BuildServiceProvider();
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        TaskCompletionSource fired = new(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.Context[SignallingJob.SignalKey] = fired;

        JobKey jobKey = new("job", schedulerName);
        TriggerKey triggerKey = new("trigger", schedulerName);

        await scheduler.ScheduleJob(
            JobBuilder.Create<SignallingJob>().WithIdentity(jobKey).Build(),
            TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .StartNow()
                // Repeating, so reading it back afterwards reads a trigger rather than finding the row a
                // completed one-shot trigger took with it.
                .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
                .Build());

        await scheduler.Start();
        await fired.Task.WaitAsync(TimeSpan.FromSeconds(30));

        (await scheduler.GetTrigger(triggerKey)).Should().NotBeNull(
            "a schema the store created has to be one the store can read back through");

        await scheduler.Shutdown(waitForJobsToComplete: true);
    }

    private static async Task<long> Scalar(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    /// <summary>
    /// A driver delegate of the kind somebody writes for a database Quartz ships no script for.
    /// </summary>
    private sealed class ScriptlessDelegate : SQLiteDelegate
    {
        protected override string? SchemaResourceName => null;
    }

    /// <summary>
    /// Public with a public constructor, because the store hands the job factory nothing but the type
    /// name it read back out of <c>JOB_CLASS_NAME</c>.
    /// </summary>
    public sealed class SignallingJob : IJob
    {
        internal const string SignalKey = "fired";

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            ((TaskCompletionSource) context.Scheduler.Context[SignalKey]!).TrySetResult();
            return default;
        }
    }
}
