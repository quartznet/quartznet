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

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Data.Sqlite;

using Quartz.Impl.AdoJobStore;
using Quartz.Spi;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The manual-firing execution group on SQLite, and what a manual firing costs: one statement for the
/// group, however many triggers the job has (#4167).
/// </summary>
/// <remarks>
/// <para>
/// 3.22.7 found the group by loading every stored trigger of the job, three statements a trigger, so a
/// job whose manual firings piled up behind a scheduler in standby made each next <c>TriggerJob</c>
/// slower than the last.
/// </para>
/// <para>
/// Each test has a SQLite file of its own, and the scheduler runs the shipped SQLite delegate counting
/// what it is asked while <see cref="CountingSqliteDelegate.Start" /> is in effect.
/// </para>
/// </remarks>
[NonParallelizable]
[Category("db-sqlite")]
public sealed class ManualFiringExecutionGroupSqliteTest : ManualFiringExecutionGroupTestBase
{
    private string databaseFile;
    private string connectionString;

    protected override string Provider => "SQLite-Microsoft";

    protected override string ConnectionString => connectionString;

    protected override Type DriverDelegate => typeof(CountingSqliteDelegate);

    protected override DbConnection CreateConnection() => new SqliteConnection(connectionString);

    protected override Task PrepareDatabase()
    {
        CountingSqliteDelegate.Reset();
        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-manual-firing-{Guid.NewGuid():N}.db");
        connectionString = $"Data Source={databaseFile}";
        InstallSchema(withExecutionGroup: true);
        return Task.CompletedTask;
    }

    protected override Task CleanUpDatabase()
    {
        CountingSqliteDelegate.Reset();
        SqliteConnection.ClearAllPools();
        if (File.Exists(databaseFile))
        {
            File.Delete(databaseFile);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The regression #4167 reported: a manual firing of a job with 200 triggers costs what one of a job
    /// with 10 does, and loads none of them.
    /// </summary>
    [Test]
    public async Task AManualFiringCostsTheSameHoweverManyTriggersTheJobHas()
    {
        FiringCost ten = await MeasureManualFiring(storedTriggers: 10);
        FiringCost twoHundred = await MeasureManualFiring(storedTriggers: 200);

        TestContext.Out.WriteLine($"10 stored triggers: {ten}");
        TestContext.Out.WriteLine($"200 stored triggers: {twoHundred}");

        twoHundred.Statements.Should().Be(ten.Statements,
            "the group the job's triggers share is one statement, so the number of triggers is not a cost");
        twoHundred.TriggerLoads.Should().Be(0, "the group is read from the column, not from each trigger loaded whole");
        twoHundred.JobTriggerReads.Should().Be(0, "the job's triggers are not listed in order to read their groups");
    }

    /// <summary>
    /// A schema that never ran the 3.18 migration: there is no column to read a group from, so a manual
    /// firing has none and reads nothing to find that out.
    /// </summary>
    [Test]
    public async Task WithoutTheColumnAManualFiringHasNoGroupAndReadsNoTriggers()
    {
        await CleanUpDatabase();
        InstallSchema(withExecutionGroup: false);

        IScheduler scheduler = await Build();
        JobKey job = await AddDurable(scheduler, "render");
        await AddTrigger(scheduler, job, "nightly-1", Reports);
        await AddTrigger(scheduler, job, "nightly-2", Reports);

        CountingSqliteDelegate.Start();
        try
        {
            await scheduler.TriggerJob(job);
        }
        finally
        {
            CountingSqliteDelegate.Stop();
        }

        FiringCost cost = CountingSqliteDelegate.Snapshot();
        TestContext.Out.WriteLine($"without the column: {cost}");
        cost.TriggerLoads.Should().Be(0, "no trigger can carry a stored group without the column, so none is loaded to ask");
        cost.JobTriggerReads.Should().Be(0);

        (await GroupOfTheManualFiring(scheduler, job)).Should().BeNull("there is no column to read a group from, nor to store one in");
    }

    /// <summary>
    /// One manual firing of a job with <paramref name="storedTriggers" /> triggers, all in one group, and
    /// what it asked of the delegate.
    /// </summary>
    private async Task<FiringCost> MeasureManualFiring(int storedTriggers)
    {
        IScheduler scheduler = await Build();
        JobKey job = await AddDurable(scheduler, $"render-{storedTriggers}");
        for (int i = 1; i <= storedTriggers; i++)
        {
            await AddTrigger(scheduler, job, $"render-{storedTriggers}-{i}", Reports);
        }

        CountingSqliteDelegate.Start();
        try
        {
            await scheduler.TriggerJob(job);
        }
        finally
        {
            CountingSqliteDelegate.Stop();
        }

        FiringCost cost = CountingSqliteDelegate.Snapshot();
        (await GroupOfTheManualFiring(scheduler, job)).Should().Be(Reports, "the premise: every trigger of the job carries reports");
        return cost;
    }

    /// <param name="withExecutionGroup">
    /// Whether the schema has the EXECUTION_GROUP columns the 3.18 migration adds; the fresh-install
    /// script has them, and leaving its two lines out gives a schema that never ran the migration.
    /// </param>
    private void InstallSchema(bool withExecutionGroup)
    {
        IEnumerable<string> lines = File.ReadAllLines(ResolveRepositoryFile("database", "tables", "tables_sqlite.sql"));
        if (!withExecutionGroup)
        {
            lines = lines.Where(line => line.IndexOf("EXECUTION_GROUP", StringComparison.Ordinal) < 0);
        }

        using SqliteConnection connection = new SqliteConnection(connectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = string.Join("\n", lines);
        command.ExecuteNonQuery();
    }

    private static string ResolveRepositoryFile(params string[] pathSegments)
    {
        string relativePath = Path.Combine(pathSegments);
        DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not find '{relativePath}' above '{AppContext.BaseDirectory}'.");
    }

    /// <summary>What one manual firing asked of the delegate.</summary>
    internal readonly struct FiringCost
    {
        public FiringCost(int statements, int triggerLoads, int jobTriggerReads)
        {
            Statements = statements;
            TriggerLoads = triggerLoads;
            JobTriggerReads = jobTriggerReads;
        }

        /// <summary>Every statement the delegate prepared.</summary>
        public int Statements { get; }

        /// <summary><c>SelectTrigger</c> calls.</summary>
        public int TriggerLoads { get; }

        /// <summary><c>SelectTriggersForJob</c> calls.</summary>
        public int JobTriggerReads { get; }

        public override string ToString()
        {
            return $"{Statements} statements, {TriggerLoads} SelectTrigger, {JobTriggerReads} SelectTriggersForJob";
        }
    }

    /// <summary>
    /// The shipped SQLite delegate, counting what it is asked while <see cref="Start" /> is in effect.
    /// </summary>
    /// <remarks>
    /// Static, because the store builds the delegate from its type name. Statements are counted where
    /// the delegate prepares them, which the trigger persistence delegates share.
    /// </remarks>
    public sealed class CountingSqliteDelegate : SQLiteDelegate
    {
        private static volatile bool recording;
        private static int statements;
        private static int triggerLoads;
        private static int jobTriggerReads;

        public static void Reset()
        {
            recording = false;
            statements = 0;
            triggerLoads = 0;
            jobTriggerReads = 0;
        }

        public static void Start()
        {
            Reset();
            recording = true;
        }

        public static void Stop() => recording = false;

        internal static FiringCost Snapshot()
        {
            return new FiringCost(
                Volatile.Read(ref statements),
                Volatile.Read(ref triggerLoads),
                Volatile.Read(ref jobTriggerReads));
        }

        public override DbCommand PrepareCommand(ConnectionAndTransactionHolder cth, string commandText)
        {
            Count(ref statements);
            return base.PrepareCommand(cth, commandText);
        }

        public override Task<IOperableTrigger> SelectTrigger(
            ConnectionAndTransactionHolder conn,
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            Count(ref triggerLoads);
            return base.SelectTrigger(conn, triggerKey, cancellationToken);
        }

        public override Task<IReadOnlyCollection<IOperableTrigger>> SelectTriggersForJob(
            ConnectionAndTransactionHolder conn,
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            Count(ref jobTriggerReads);
            return base.SelectTriggersForJob(conn, jobKey, cancellationToken);
        }

        private static void Count(ref int counter)
        {
            if (recording)
            {
                Interlocked.Increment(ref counter);
            }
        }
    }
}
