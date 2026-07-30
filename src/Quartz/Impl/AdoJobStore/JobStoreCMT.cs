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
/// <see cref="JobStoreCMT" /> is meant to be used in an application-server
/// or other software framework environment that provides
/// container-managed-transactions. No commit / rollback will be handled by this class.
/// </summary>
/// <remarks>
/// If you need commit / rollback, use <see cref="JobStoreTX" />
/// instead.
/// </remarks>
/// <author><a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a></author>
/// <author>James House</author>
/// <author>Srinivas Venkatarangaiah</author>
/// <author>Marko Lahma (.NET)</author>
public class JobStoreCMT : JobStoreSupport
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JobStoreCMT"/> class.
    /// </summary>
    public JobStoreCMT(
        ISchedulerSignaler schedulerSignaler,
        ITypeLoadHelper typeLoadHelper,
        TimeProvider timeProvider,
        IOptions<QuartzSchedulerOptions> schedulerOptions,
        IOptions<AdoJobStoreOptions> storeOptions,
        IObjectSerializer objectSerializer,
        IDbConnectionManager connectionManager,
        IDbProvider dbProvider,
        IDriverDelegate driverDelegate,
        ISemaphore? lockHandler = null)
        : base(schedulerSignaler, typeLoadHelper, timeProvider, schedulerOptions, storeOptions, objectSerializer, connectionManager, dbProvider, driverDelegate, lockHandler)
    {
    }

    /// <summary>
    /// Instructs this job store whether connections should be automatically opened.
    /// </summary>
    public virtual bool OpenConnection { protected get; set; }

    /// <summary>
    /// Called by the QuartzScheduler before the <see cref="IJobStore"/> is
    /// used, in order to give the it a chance to Initialize.
    /// </summary>
    public override async ValueTask Initialize(CancellationToken cancellationToken = default)
    {
        if (LockHandler is null)
        {
            // If the user hasn't specified an explicit lock handler,
            // then we ///must/// use DB locks with CMT...
            UseDbLocks = true;
        }

        await base.Initialize(cancellationToken).ConfigureAwait(false);
        Logger.LogInformation("JobStoreCMT initialized.");
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
    /// Gets the non managed TX connection.
    /// </summary>
    /// <returns></returns>
    protected override async ValueTask<ConnectionAndTransactionHolder> GetNonManagedTXConnection(CancellationToken cancellationToken = default)
    {
        var enlisted = await GetEnlistedConnection(cancellationToken).ConfigureAwait(false);
        if (enlisted is not null)
        {
            return enlisted;
        }

        DbConnection conn;
        try
        {
            // Deliberately not kept out of an ambient transaction the way JobStoreSupport does it: this
            // store exists precisely to run inside a transaction its container manages, so the
            // connection auto-enlisting is the contract rather than an accident.
            conn = DbProvider.CreateConnection();
            if (OpenConnection)
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
    /// Because CMT assumes that the connection is already part of a managed
    /// transaction, it does not attempt to commit or rollback the
    /// enclosing transaction.
    /// </summary>
    /// <seealso cref="JobStoreSupport.ExecuteInNonManagedTXLock" />
    /// <seealso cref="JobStoreSupport.ExecuteInLock" />
    /// <seealso cref="JobStoreSupport.GetNonManagedTXConnection(CancellationToken)" />
    /// <seealso cref="JobStoreSupport.GetConnection(CancellationToken)" />
    /// <param name="lockName">
    /// The name of the lock to acquire, for example
    /// "TRIGGER_ACCESS".  If null, then no lock is acquired, but the
    /// txCallback is still executed in a transaction.
    /// </param>
    /// <param name="txCallback">Callback to execute.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    protected override async ValueTask<T> ExecuteInLock<T>(
        string? lockName,
        Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback,
        CancellationToken cancellationToken = default)
    {
        bool transOwner = false;
        ConnectionAndTransactionHolder? conn = null;
        Guid requestorId = Guid.NewGuid();
        try
        {
            if (lockName is not null)
            {
                // If we aren't using db locks, then delay getting DB connection
                // until after acquiring the lock since it isn't needed.
                if (LockHandler.RequiresConnection)
                {
                    conn = await GetNonManagedTXConnection(cancellationToken).ConfigureAwait(false);
                }

                transOwner = await LockHandler.ObtainLock(requestorId, conn!, lockName, cancellationToken).ConfigureAwait(false);
            }

            if (conn is null)
            {
                conn = await GetNonManagedTXConnection(cancellationToken).ConfigureAwait(false);
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
                && (sigTime is not null || lockName is not null && !LockAllOperations))
            {
                SignalSchedulingChangeOnApplicationCommit(conn, sigTime, cancellationToken);
            }

            return result;
        }
        finally
        {
            try
            {
                await ReleaseLock(requestorId, lockName!, transOwner, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await CleanupConnection(conn, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}