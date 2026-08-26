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

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.Impl.AdoJobStore.Common;
using Quartz.Util;
using Quartz.Extensibility;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// The persistent job store for a scheduler that runs on its own: it begins the ADO.NET transaction
/// each operation runs in, and commits or rolls it back itself.
/// </summary>
/// <remarks>
/// This is the default persistent store. Use <see cref="ExternalTransactionJobStore" /> instead when
/// the transaction belongs to a container, and see
/// <see cref="AdoJobStoreBase.AcceptEnlistedTransactions" /> for taking part in a transaction the
/// application owns while still managing one when nothing is enlisted.
/// </remarks>
/// <author><a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a></author>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public class LocalTransactionJobStore : AdoJobStoreBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LocalTransactionJobStore"/> class.
    /// </summary>
    public LocalTransactionJobStore(
        ISchedulerSignaler schedulerSignaler,
        ITypeLoader typeLoader,
        TimeProvider timeProvider,
        IOptions<QuartzSchedulerOptions> schedulerOptions,
        IOptions<AdoJobStoreOptions> storeOptions,
        IOptions<ClusteringOptions> clusteringOptions,
        IObjectSerializer objectSerializer,
        IDbProvider dbProvider,
        IDriverDelegate driverDelegate,
        ISemaphore? lockHandler = null,
        IEnumerable<ITriggerPersistenceDelegate>? triggerPersistenceDelegates = null,
        ILoggerFactory? loggerFactory = null)
        : base(schedulerSignaler, typeLoader, timeProvider, schedulerOptions, storeOptions, clusteringOptions, objectSerializer, dbProvider, driverDelegate, lockHandler, triggerPersistenceDelegates, loggerFactory)
    {
    }

    /// <summary>
    /// Called by the QuartzScheduler before the <see cref="IJobStore"/> is
    /// used, in order to give the it a chance to Initialize.
    /// </summary>
    public override async ValueTask Initialize(SchedulerIdentity identity, CancellationToken cancellationToken = default)
    {
        await base.Initialize(identity, cancellationToken).ConfigureAwait(false);
        Logger.LocalTransactionStoreInitialized();
    }

    /// <summary>
    /// This store manages its own transactions and has the one data source, so the connection it runs
    /// a locked operation on is just the normal one.
    /// </summary>
    /// <seealso cref="AdoJobStoreBase.GetConnection(CancellationToken)" />
    protected override ValueTask<ConnectionAndTransactionHolder> GetLocalTransactionConnection(CancellationToken cancellationToken = default)
    {
        return GetConnection(cancellationToken);
    }

    /// <summary>
    /// Execute the given callback having optionally acquired the given lock. Because this store
    /// manages its own transactions and only has the one data source, this is the same behavior as
    /// <see cref="AdoJobStoreBase.ExecuteInLocalTransactionLock{T}" />.
    /// </summary>
    /// <param name="lockKind">
    /// The lock to acquire. If <see langword="null" />, then no lock is acquired, but the
    /// <paramref name="txCallback" /> is still executed in a transaction.
    /// </param>
    /// <param name="txCallback">Callback to execute.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <seealso cref="AdoJobStoreBase.ExecuteInLocalTransactionLock{T}" />
    /// <seealso cref="AdoJobStoreBase.GetLocalTransactionConnection(CancellationToken)" />
    /// <seealso cref="AdoJobStoreBase.GetConnection(CancellationToken)" />
    protected override ValueTask<T> ExecuteInLock<T>(
        SchedulerLock? lockKind,
        Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInLocalTransactionLock(lockKind, txCallback, cancellationToken: cancellationToken);
    }
}
