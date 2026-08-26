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

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The ADO.NET contract against a SQLite file database, built fresh for each test from
/// <c>database/tables/tables_sqlite.sql</c>.
/// </summary>
/// <remarks>
/// SQLite on a file needs no container, so a fixture built on this runs wherever the in-memory one
/// does — the point of running one contract against two stores is that both actually run, and a
/// dialect that only runs behind Docker cannot carry that.
/// </remarks>
public abstract class SqliteFileJobStoreContractTest : AdoJobStoreContractTest
{
    private string dbFileName;

    protected override string DbProviderName => "SQLite-Microsoft";

    protected override IDriverDelegate CreateDriverDelegate() => new SQLiteDelegate();

    protected override async ValueTask<string> PrepareDatabase()
    {
        dbFileName = $"test-store-contract-{Guid.NewGuid():N}.db";
        string connectionString = $"Data Source={dbFileName};";

        await using (SqliteConnection connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            await using SqliteCommand command = new SqliteCommand(LoadSqliteTableScript(), connection);
            await command.ExecuteNonQueryAsync();
        }

        return connectionString;
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
