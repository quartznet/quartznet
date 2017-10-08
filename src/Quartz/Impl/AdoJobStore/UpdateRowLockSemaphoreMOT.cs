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

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// Provides thread/resource using SQL Server memory-optimized tables.
    /// </summary>
    /// <author>JBVyncent</author>
    /// <author>Marko Lahma</author>
    public class UpdateLockRowSemaphoreMOT : UpdateLockRowSemaphore
    {
        private static readonly string SqlUpdateForLockMOT =
            $"UPDATE {TablePrefixSubst}{TableLocks} WITH (SNAPSHOT) SET {ColumnLockName} = {ColumnLockName} WHERE {ColumnSchedulerName} = {SchedulerNameSubst} AND {ColumnLockName} = @lockName";

        private static readonly string SqlInsertLockMOT =
            $"INSERT INTO {TablePrefixSubst}{TableLocks}({ColumnSchedulerName}, {ColumnLockName}) VALUES ({SchedulerNameSubst}, @lockName)";

        /// <summary>
        /// Initializes a new instance of the <see cref="UpdateLockRowSemaphoreMOT"/> class.
        /// </summary>
        public UpdateLockRowSemaphoreMOT(IDbProvider provider)
            : base(DefaultTablePrefix, null, SqlUpdateForLockMOT, SqlInsertLockMOT, provider)
        {
        }

        protected override int RetryCount => 5;
    }
}