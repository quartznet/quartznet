#region License

/*
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved.
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
/// Update-based lock handler for SQL Server memory-optimized tables, which need the
/// <c>WITH (SNAPSHOT)</c> hint on the locking update.
/// </summary>
/// <author>JBVyncent</author>
/// <author>Marko Lahma</author>
public sealed class SqlServerMemoryOptimizedUpdateRowLockHandler : UpdateRowLockHandler
{
    private const string UpdateForLockMemoryOptimized =
        $"UPDATE {StdAdoConstants.TablePrefixSubst}{AdoConstants.TableLocks} WITH (SNAPSHOT) SET {AdoConstants.ColumnLockName} = {AdoConstants.ColumnLockName} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnLockName} = @lockName";

    private const string InsertLockMemoryOptimized =
        $"INSERT INTO {StdAdoConstants.TablePrefixSubst}{AdoConstants.TableLocks}({AdoConstants.ColumnSchedulerName}, {AdoConstants.ColumnLockName}) VALUES (@schedulerName, @lockName)";

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerMemoryOptimizedUpdateRowLockHandler"/> class.
    /// </summary>
    public SqlServerMemoryOptimizedUpdateRowLockHandler(IDbProvider provider)
        : base(AdoConstants.DefaultTablePrefix, null, UpdateForLockMemoryOptimized, InsertLockMemoryOptimized, provider)
    {
    }

    protected override int RetryCount => 5;
}