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
using System.IO;
using System.Threading.Tasks;

using Microsoft.Data.Sqlite;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// What a store does when the database is missing one of the tables it validates.
/// </summary>
/// <remarks>
/// <para>
/// The database is a real one, but SQLite is a file, so a schema installed from
/// <c>database/tables/tables_sqlite.sql</c> — the script a reader is told to run — costs a temporary
/// path and no container. Installing it and then dropping one table is the shape of the real failure
/// this guards against: a database that is almost right.
/// </para>
/// <para>
/// The other half of the subject is <c>Quartz.Tests.Unit.Impl.AdoJobStore.SchemaValidationTest</c>,
/// which holds <c>AdoConstants.AllTableNames</c> against every dialect's script without needing a
/// database at all. Neither test alone would have caught #3564: the list agreed with itself, and the
/// table it left out is one nothing here would have thought to drop.
/// </para>
/// </remarks>
[NonParallelizable]
[Category("db-sqlite")]
public class SchemaValidationTest
{
    /// <summary>The tables the cases below drop one at a time.</summary>
    private static readonly string[] EveryValidatedTable = AdoConstants.AllTableNames;

    private string databaseFile;
    private string connectionString;
    private IScheduler scheduler;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-validation-{Guid.NewGuid():N}.db");
        connectionString = $"Data Source={databaseFile}";
    }

    [TearDown]
    public async Task DeleteDatabase()
    {
        if (scheduler != null)
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
            scheduler = null;
        }

        SqliteConnection.ClearAllPools();

        try
        {
            if (File.Exists(databaseFile))
            {
                File.Delete(databaseFile);
            }
        }
        catch (IOException)
        {
            // The file is in the temporary directory and the run is over; failing the case that just
            // passed because the operating system still holds a handle would say nothing about Quartz.
        }
    }

    /// <summary>
    /// The control: an untouched schema starts.
    /// </summary>
    /// <remarks>
    /// Without it, a harness that installed nothing — a script that failed to run, a path that moved —
    /// would make every case below pass for the wrong reason, since a database with no tables at all
    /// also refuses to start.
    /// </remarks>
    [Test]
    public async Task AStoreStartsAgainstTheSchemaTheFreshInstallScriptCreates()
    {
        InstallSchemaFromFreshInstallScript();

        Func<Task> act = async () => scheduler = await GetScheduler(
            nameof(AStoreStartsAgainstTheSchemaTheFreshInstallScriptCreates));

        await act.Should().NotThrowAsync(
            "the eleven tables database/tables/tables_sqlite.sql creates are the eleven the store "
            + "validates, so a schema installed straight from it needs nothing else");
    }

    /// <summary>
    /// Every table on the list is one a database can be missing and be refused for.
    /// </summary>
    /// <remarks>
    /// Running the case for all of them is what makes the list mean something: a name added to
    /// <c>AllTableNames</c> that no statement can query — a view, a misspelling, a table only some
    /// dialects have — would pass the unit-level cases and fail here.
    /// </remarks>
    [TestCaseSource(nameof(EveryValidatedTable))]
    public async Task AStoreRefusesToStartWhenATableIsMissing(string table)
    {
        InstallSchemaFromFreshInstallScript();
        Execute($"DROP TABLE QRTZ_{table}");

        Func<Task> act = () => GetScheduler($"{nameof(AStoreRefusesToStartWhenATableIsMissing)}_{table}");

        SchedulerException failure = (await act.Should().ThrowAsync<SchedulerException>(
                "a database missing a table Quartz writes to has to be a startup failure, not a "
                + "scheduler that runs until something schedules the trigger type that needs it"))
            .WithMessage("*schema validation failed*")
            .Which;

        MessagesOf(failure).Should().ContainMatch($"*QRTZ_{table}*",
            "the message names the table that is missing, since the reader's next move is to run the "
            + "migration or the fresh-install script that creates it");
    }

    /// <summary>
    /// Runs <c>database/tables/tables_sqlite.sql</c>, the script the documentation tells a reader to
    /// run, against the empty file.
    /// </summary>
    /// <remarks>
    /// The whole text goes to one command: SQLite's own parser splits it, which is the only splitter
    /// that gets every statement in the file right.
    /// </remarks>
    private void InstallSchemaFromFreshInstallScript()
    {
        Execute(File.ReadAllText(ResolveRepositoryFile("database", "tables", "tables_sqlite.sql")));
    }

    private void Execute(string sql)
    {
        using (SqliteConnection connection = new SqliteConnection(connectionString))
        {
            connection.Open();

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }
    }

    /// <summary>
    /// A store over the temporary database, with schema validation left at its default of on.
    /// </summary>
    private Task<IScheduler> GetScheduler(string schedulerName)
    {
        SchedulerBuilder config = SchedulerBuilder.Create($"{schedulerName}_instance", schedulerName);

        config.UsePersistentStore(store =>
        {
            store.UseMicrosoftSQLite(connectionString);
            store.UseSystemTextJsonSerializer();
        });

        return config.BuildScheduler();
    }

    /// <summary>Every message in an exception chain, outermost first.</summary>
    private static List<string> MessagesOf(Exception exception)
    {
        List<string> messages = new List<string>();

        for (Exception current = exception; current != null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return messages;
    }

    /// <summary>
    /// A file in the working tree the test assembly was built in, found by walking up from the output
    /// directory rather than counting directories, which is one more thing to fix when the layout moves.
    /// </summary>
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

        throw new FileNotFoundException(
            $"{relativePath} was not found above {AppContext.BaseDirectory}", relativePath);
    }
}
