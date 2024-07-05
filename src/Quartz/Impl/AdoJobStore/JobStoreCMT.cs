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

using Quartz.Spi;

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
    /// Instructs this job store whether connections should be automatically opened.
    /// </summary>
    public virtual bool OpenConnection { protected get; set; }

    /// <summary>
    /// Called by the QuartzScheduler before the <see cref="IJobStore"/> is
    /// used, in order to give the it a chance to Initialize.
    /// </summary>
    public override async ValueTask Initialize(
        ITypeLoadHelper loadHelper,
        ISchedulerSignaler signaler,
        CancellationToken cancellationToken = default)
    {
        if (LockHandler is null)
        {
            // If the user hasn't specified an explicit lock handler,
            // then we ///must/// use DB locks with CMT...
            UseDBLocks = true;
        }

        await base.Initialize(loadHelper, signaler, cancellationToken).ConfigureAwait(false);
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
            ConnectionManager.Shutdown(DataSource);
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
    protected override async ValueTask<ConnectionAndTransactionHolder> GetNonManagedTXConnection()
    {
        DbConnection conn;
        try
        {
            conn = ConnectionManager.GetConnection(DataSource);
            if (OpenConnection)
            {
                await conn.OpenAsync().ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            ThrowHelper.ThrowJobPersistenceException($"Failed to obtain DB connection from data source '{DataSource}': {e}", e);
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
    /// <seealso cref="JobStoreSupport.GetNonManagedTXConnection()" />
    /// <seealso cref="JobStoreSupport.GetConnection()" />
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
                    conn = await GetNonManagedTXConnection().ConfigureAwait(false);
                }

                transOwner = await LockHandler.ObtainLock(requestorId, conn!, lockName, cancellationToken).ConfigureAwait(false);
            }

            if (conn is null)
            {
                conn = await GetNonManagedTXConnection().ConfigureAwait(false);
            }

            return await txCallback(conn).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await ReleaseLock(requestorId, lockName!, transOwner, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await CleanupConnection(conn).ConfigureAwait(false);
            }
        }
    }
}