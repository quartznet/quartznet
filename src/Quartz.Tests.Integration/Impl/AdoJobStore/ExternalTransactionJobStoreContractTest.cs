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

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The job store contract as <see cref="ExternalTransactionJobStore" /> implements it.
/// </summary>
/// <remarks>
/// <para>
/// The two persistent stores share nearly all of their code and differ in the two members that decide
/// who owns the transaction, which is exactly the kind of difference a contract suite is for: an
/// override that stopped committing, or one that started, would be invisible everywhere else.
/// </para>
/// <para>
/// No ambient transaction is opened around the calls. That is the honest shape of this fixture rather
/// than a shortcut: with nothing enlisted the store opens a connection of its own, which is what
/// <see cref="AdoJobStoreOptions.OpenConnection" /> is for, and each statement stands alone. An
/// enlisted-transaction fixture would be asserting <see cref="System.Transactions" /> semantics
/// instead of the store contract, and <c>EnlistedTransactionTest</c> already does that.
/// </para>
/// <para>
/// SQLite, so this runs in the <c>basic</c> leg beside <see cref="SQLiteJobStoreContractTest" />
/// rather than needing a container of its own.
/// </para>
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class ExternalTransactionJobStoreContractTest : SqliteFileJobStoreContractTest
{
    private const string DataSourceName = "job-store-contract-cmt";

    protected override AdoJobStoreBase CreateJobStore(IDbProvider dbProvider, IDriverDelegate driverDelegate)
    {
        // As with the local-transaction store, no lock handler: the store picks it, and for SQLite that
        // is SqliteLockHandler whatever this store would otherwise have insisted on.
        return new ExternalTransactionJobStore(TestJobStores.Dependencies(
            schedulerOptions: TestJobStores.SchedulerOptions(SchedulerName, StoreInstanceId),
            storeOptions: TestJobStores.StoreOptions(DataSourceName, configure: o => o.OpenConnection = true),
            dbProvider: dbProvider,
            driverDelegate: driverDelegate) with
        {
            LockHandler = null,
        });
    }
}
