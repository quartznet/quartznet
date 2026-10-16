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

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The version stamp round-trips through the shipped statements on a real schema: a check-in's insert
/// and update write it, the state read answers it, and a row written without it reads as none (#4067).
/// </summary>
public sealed class SchedulerStateVersionSqliteTest
{
    private const string SchedulerName = "SchedulerStateVersionSqliteTest";

    private static readonly DateTimeOffset CheckedIn = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private SqliteTestDatabase database = null!;
    private SQLiteDelegate adoDelegate = null!;

    [OneTimeSetUp]
    public async Task CreateDatabase()
    {
        database = new SqliteTestDatabase("scheduler-state-version");

        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = new(LoadSqliteTableScript(), connection);
        await command.ExecuteNonQueryAsync();

        adoDelegate = new SQLiteDelegate();
        adoDelegate.Initialize(new DriverDelegateContext
        {
            TablePrefix = "QRTZ_",
            SchedulerName = SchedulerName,
            InstanceId = "node-a",
            TypeLoader = new SimpleTypeLoader(),
            DbProvider = new DbProvider("SQLite-Microsoft", database.ConnectionString),
        });
    }

    [OneTimeTearDown]
    public void DropDatabase()
    {
        database.Dispose();
    }

    [Test]
    public async Task ACheckInStampsTheRowAndTheReadAnswersIt()
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        ConnectionAndTransactionHolder conn = new(connection, null);

        await adoDelegate.InsertSchedulerState(conn, "node-a", CheckedIn, TimeSpan.FromSeconds(7.5));

        // A row as a node older than 4.6 leaves it: no stamp at all.
        await using (SqliteCommand older = connection.CreateCommand())
        {
            older.CommandText = "INSERT INTO QRTZ_SCHEDULER_STATE (SCHED_NAME, INSTANCE_NAME, LAST_CHECKIN_TIME, CHECKIN_INTERVAL) VALUES (@s, 'node-45', @t, 7500)";
            older.Parameters.AddWithValue("@s", SchedulerName);
            older.Parameters.AddWithValue("@t", CheckedIn.UtcTicks);
            await older.ExecuteNonQueryAsync();
        }

        List<SchedulerStateRecord> records = await adoDelegate.SelectSchedulerStateRecords(conn, instanceId: null);

        records.Should().HaveCount(2);
        records.Single(x => x.SchedulerInstanceId == "node-a").QuartzVersion.Should().Be(SchedulerNodeVersion.Current,
            "the insert a first check-in makes stamps this node's version");
        records.Single(x => x.SchedulerInstanceId == "node-45").QuartzVersion.Should().BeNull(
            "a row a 4.5 node inserted names no version, which reads as none");

        (await adoDelegate.UpdateSchedulerState(conn, "node-45", CheckedIn.AddSeconds(8))).Should().Be(1);

        SchedulerStateRecord restamped = (await adoDelegate.SelectSchedulerStateRecords(conn, "node-45")).Single();
        restamped.QuartzVersion.Should().Be(SchedulerNodeVersion.Current,
            "a 4.6 node taking over an instance id restamps the row on its first update, so the row says what runs under that id now");
        restamped.CheckinTimestamp.Should().Be(CheckedIn.AddSeconds(8));
    }

    private static string LoadSqliteTableScript()
    {
        string path = File.Exists("../../../../database/tables/tables_sqlite.sql")
            ? "../../../../database/tables/tables_sqlite.sql"
            : "../../../../../database/tables/tables_sqlite.sql";

        return File.ReadAllText(path);
    }
}
