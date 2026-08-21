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

using System.Data.Common;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.Impl.AdoJobStore.Common;
using Quartz.Util;
using Quartz.Extensibility;

namespace Quartz.Impl.AdoJobStore;

///<summary>
/// The persistent job store for a scheduler that runs inside a transaction somebody else owns - an
/// application server or another framework providing container-managed transactions. It neither
/// commits nor rolls back.
/// </summary>
/// <remarks>
/// If you need the store to commit and roll back, use <see cref="LocalTransactionJobStore" />
/// instead.
/// </remarks>
/// <author><a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a></author>
/// <author>James House</author>
/// <author>Srinivas Venkatarangaiah</author>
/// <author>Marko Lahma (.NET)</author>
public class ExternalTransactionJobStore : AdoJobStoreBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalTransactionJobStore"/> class.
    /// </summary>
    public ExternalTransactionJobStore(
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
        IEnumerable<ITriggerPersistenceDelegate>? triggerPersistenceDelegates = null)
        : base(schedulerSignaler, typeLoader, timeProvider, schedulerOptions, storeOptions, clusteringOptions, objectSerializer, dbProvider, driverDelegate, lockHandler, triggerPersistenceDelegates)
    {
        openConnection = storeOptions.Value.OpenConnection;
    }

    /// <summary>
    /// Whether this job store opens the connections it creates, configured through
    /// <see cref="AdoJobStoreOptions.OpenConnection" /> and read once at construction like every
    /// sibling setting.
    /// </summary>
    private readonly bool openConnection;

    /// <summary>
    /// Called by the QuartzScheduler before the <see cref="IJobStore"/> is
    /// used, in order to give the it a chance to Initialize.
    /// </summary>
    public override async ValueTask Initialize(CancellationToken cancellationToken = default)
    {
        if (LockHandler is null)
        {
            // If the user hasn't specified an explicit lock handler,
            // then we ///must/// use DB locks with container-managed transactions...
            UseDbLocks = true;
        }

        await base.Initialize(cancellationToken).ConfigureAwait(false);
        Logger.LogInformation("ExternalTransactionJobStore initialized.");
    }

    /// <summary>
    /// Called by the QuartzScheduler to inform the <see cref="IJobStore" /> that
    /// it should free up all of it's resources because the scheduler is
    /// shutting down.
    /// </summary>
    public override async ValueTask Shutdown(CancellationToken cancellationToken = default)
    {
        await base.Shutdown(cancellationToken).ConfigureAwait(false);

        try
        {
            DbProvider.Shutdown();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Database connection shutdown unsuccessful.");
        }
    }

    /// <summary>
    /// Gets the connection a locked operation runs on. There is no transaction of this store's own:
    /// the one the container manages is already in progress.
    /// </summary>
    protected override async ValueTask<ConnectionAndTransactionHolder> GetLocalTransactionConnection(CancellationToken cancellationToken = default)
    {
        var enlisted = await GetEnlistedConnection(cancellationToken).ConfigureAwait(false);
        if (enlisted is not null)
        {
            return enlisted;
        }

        DbConnection conn;
        try
        {
            // Deliberately not kept out of an ambient transaction the way AdoJobStoreBase does it: this
            // store exists precisely to run inside a transaction its container manages, so the
            // connection auto-enlisting is the contract rather than an accident.
            conn = DbProvider.CreateConnection();
            if (openConnection)
            {
                await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException($"Failed to obtain DB connection from data source '{DataSource}': {e}", e);
            return default;
        }

        return new ConnectionAndTransactionHolder(conn, null);
    }

    /// <summary>
    /// Execute the given callback having optionally acquired the given lock.
    /// Because this store assumes that the connection is already part of a transaction its container
    /// manages, it does not attempt to commit or rollback the enclosing transaction.
    /// </summary>
    /// <seealso cref="AdoJobStoreBase.ExecuteInLocalTransactionLock{T}" />
    /// <seealso cref="AdoJobStoreBase.ExecuteInLock{T}" />
    /// <seealso cref="AdoJobStoreBase.GetLocalTransactionConnection(CancellationToken)" />
    /// <seealso cref="AdoJobStoreBase.GetConnection(CancellationToken)" />
    /// <param name="lockKind">
    /// The lock to acquire. If <see langword="null" />, then no lock is acquired, but the
    /// <paramref name="txCallback" /> is still executed in a transaction.
    /// </param>
    /// <param name="txCallback">Callback to execute.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    protected override async ValueTask<T> ExecuteInLock<T>(
        SchedulerLock? lockKind,
        Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback,
        CancellationToken cancellationToken = default)
    {
        bool transOwner = false;
        ConnectionAndTransactionHolder? conn = null;
        Guid requestorId = Guid.NewGuid();
        try
        {
            if (lockKind is not null)
            {
                // If we aren't using db locks, then delay getting DB connection
                // until after acquiring the lock since it isn't needed.
                if (LockHandler.RequiresConnection)
                {
                    conn = await GetLocalTransactionConnection(cancellationToken).ConfigureAwait(false);
                }

                transOwner = await LockHandler.ObtainLock(requestorId, conn!, lockKind.Value, cancellationToken).ConfigureAwait(false);
            }

            if (conn is null)
            {
                conn = await GetLocalTransactionConnection(cancellationToken).ConfigureAwait(false);
            }

            var result = await txCallback(conn).ConfigureAwait(false);

            // Only for a connection the application enlisted, and only for operations that took the
            // lock - those are the ones that can have changed the schedule. There the change becomes
            // visible when its owner commits, and the scheduler is otherwise notified solely before
            // that - by QuartzScheduler, as soon as the store call returns - finds nothing, and waits
            // out a whole idle interval. Signalling for reads as well would announce an unknown earlier
            // time on every query and keep bouncing acquired triggers back to waiting, and deployments
            // that never opted in must keep the behaviour they had, which for this store is no signal
            // from here at all.
            var sigTime = conn.SignalSchedulingChangeOnTxCompletion;
            if (conn.BorrowedFrom is not null
                && (sigTime is not null || lockKind is not null && !LockAllOperations))
            {
                SignalSchedulingChangeOnApplicationCommit(conn, sigTime, cancellationToken);
            }

            return result;
        }
        finally
        {
            try
            {
                await ReleaseLock(requestorId, lockKind, transOwner, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await CleanupConnection(conn, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
