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

using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// PostgreSQL-specific row lock semaphore that uses INSERT ... ON CONFLICT DO NOTHING
/// to handle race conditions when multiple threads try to insert the same lock row.
/// </summary>
/// <remarks>
/// This implementation fixes the transaction abort issue that occurs in PostgreSQL
/// when two threads simultaneously attempt to insert a lock row, causing a primary key
/// violation that aborts the transaction.
/// </remarks>
public class PostgreSqlSelectForUpdateSemaphore : SelectForUpdateSemaphore
{
    // PostgreSQL-specific INSERT statement that uses ON CONFLICT DO NOTHING to prevent
    // transaction aborts when multiple threads try to insert the same lock row
    private const string PostgreSqlInsertLock =
        $"INSERT INTO {StdAdoConstants.TablePrefixSubst}{AdoConstants.TableLocks}({AdoConstants.ColumnSchedulerName}, {AdoConstants.ColumnLockName}) VALUES (@schedulerName, @lockName) ON CONFLICT DO NOTHING";

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlSelectForUpdateSemaphore"/> class.
    /// </summary>
    public PostgreSqlSelectForUpdateSemaphore(IDbProvider dbProvider)
        : base(AdoConstants.DefaultTablePrefix, null, selectWithLockSql: null, PostgreSqlInsertLock, dbProvider)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlSelectForUpdateSemaphore"/> class.
    /// </summary>
    /// <param name="tablePrefix">The table prefix.</param>
    /// <param name="schedulerName">the scheduler name</param>
    /// <param name="selectWithLockSql">The select with lock SQL.</param>
    /// <param name="dbProvider">The db provider.</param>
    public PostgreSqlSelectForUpdateSemaphore(string tablePrefix, string schedulerName, string? selectWithLockSql, IDbProvider dbProvider)
        : base(tablePrefix, schedulerName, selectWithLockSql, PostgreSqlInsertLock, dbProvider)
    {
    }
}
