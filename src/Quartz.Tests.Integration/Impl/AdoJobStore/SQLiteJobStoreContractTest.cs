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

using Microsoft.Data.Sqlite;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The job store contract as the ADO.NET store implements it, against a real database. SQLite on a
/// file needs no container, so this runs wherever the in-memory fixture does — the point of the pair
/// is that both stores answer the same assertions, and that only holds if both actually run.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class SQLiteJobStoreContractTest : JobStoreContractTest
{
    private const string DataSourceName = "job-store-contract-sqlite";
    private const string SchedulerName = "JobStoreContractTest";
    private const string InstanceId = "contract-instance";

    private string dbFileName;

    /// <summary>
    /// The ADO schema has no row for a paused job group, so every job group reads as unpaused.
    /// </summary>
    protected override bool ReportsJobGroupPauseState => false;

    protected override async ValueTask<IJobStore> CreateStore()
    {
        dbFileName = $"test-store-contract-{Guid.NewGuid():N}.db";

        await using (SqliteConnection connection = new SqliteConnection($"Data Source={dbFileName};"))
        {
            await connection.OpenAsync();
            await using SqliteCommand command = new SqliteCommand(LoadSqliteTableScript(), connection);
            await command.ExecuteNonQueryAsync();
        }

        // The store reads through the provider it is constructed with, so the test only has to build
        // one.
        IDbProvider dbProvider = new DbProvider("SQLite-Microsoft", $"Data Source={dbFileName};");

        LocalTransactionJobStore store = new LocalTransactionJobStore(
            TestJobStores.Signaler(),
            TestJobStores.TypeLoader(),
            TimeProvider.System,
            TestJobStores.SchedulerOptions(SchedulerName, InstanceId),
            TestJobStores.StoreOptions(DataSourceName),
            TestJobStores.ClusteringOptions(),
            TestJobStores.Serializer(),
            dbProvider,
            new SQLiteDelegate(),
            TestJobStores.LockHandler());

        await store.Initialize(new SchedulerIdentity { SchedulerName = SchedulerName, InstanceId = InstanceId });

        // SchedulerStarted() is deliberately not called: it spawns the misfire handler, which would
        // both race the tests that drive trigger state by hand and leave a foreground thread behind
        // for every store this fixture builds.
        return store;
    }

    protected override ValueTask DisposeStore()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(dbFileName))
        {
            try
            {
                File.Delete(dbFileName);
            }
            catch (IOException)
            {
                // the file is only test scratch space, leaving it behind is not worth failing over
            }
        }

        return default;
    }

    private static string LoadSqliteTableScript()
    {
        string path = File.Exists("../../../../database/tables/tables_sqlite.sql")
            ? "../../../../database/tables/tables_sqlite.sql"
            : "../../../../../database/tables/tables_sqlite.sql";

        return File.ReadAllText(path);
    }
}
