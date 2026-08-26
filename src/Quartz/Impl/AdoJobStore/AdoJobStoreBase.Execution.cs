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

using System.Data;
using System.Data.Common;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Impl.AdoJobStore;

public abstract partial class AdoJobStoreBase
{
    protected abstract ValueTask<ConnectionAndTransactionHolder> GetLocalTransactionConnection(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the connection the application enlisted for this scheduler on the current
    /// asynchronous flow, or <see langword="null" /> when enlisted transactions are not accepted or
    /// nothing is enlisted. The returned holder does not own the connection or the transaction.
    /// </summary>
    /// <remarks>
    /// A store that overrides <see cref="GetLocalTransactionConnection" /> has to start with this, or
    /// it silently opens a connection of its own while the caller believes the scheduling is part of
    /// their transaction. Everything an enlistment needs in order to be safe to use happens here: the
    /// transaction is checked to be alive and still current, the provider is checked to match, the
    /// connection is opened if it is not, and it is booked out for the duration of the operation so
    /// two concurrent scheduler calls cannot share it. Cleaning the returned holder up through
    /// <see cref="CleanupConnection" /> hands the booking back.
    /// </remarks>
    protected async ValueTask<ConnectionAndTransactionHolder?> GetEnlistedConnection(CancellationToken cancellationToken = default)
    {
        var enlisted = AmbientConnection.Get(InstanceName);
        if (enlisted is null)
        {
            return null;
        }

        // Refused rather than ignored. Ignoring it would commit the scheduling in a transaction of the
        // store own while the caller believes it is part of theirs, and that only shows up later as a
        // job firing for an entity the caller rolled back. This is also what covers schedulers the
        // enlistment call site could not inspect, such as a decorator around the real one.
        if (!AcceptEnlistedTransactions)
        {
            Throw.JobPersistenceException(
                $"A connection is enlisted for scheduler '{InstanceName}', but it is not configured to take part in "
                + "transactions the application owns, so this operation would commit on its own. Configure the "
                + "persistent store with Configure(o => o.AcceptEnlistedTransactions = true), or set "
                + "'quartz.jobStore.acceptEnlistedTransactions' to true.");
        }

        // The enlisted transaction may have finished while its scope was still open - the application
        // committed or rolled back, or its TransactionScope ended. Carrying on would run this
        // operation in autocommit, where a half-finished write can no longer be rolled back, so refuse
        // rather than quietly drop the transactional guarantee the caller asked for.
        if (enlisted.Transaction is not null && enlisted.Transaction.Connection is null)
        {
            Throw.JobPersistenceException(
                $"The transaction enlisted for scheduler '{InstanceName}' has already been committed or rolled back, "
                + "so this operation would run with no transaction at all. Dispose the enlistment scope once the "
                + "transaction completes, and enlist a new one for any further scheduling.");
        }

        // Compared with == rather than by reference: a dependent clone is a different object standing
        // for the same transaction, and refusing those would break legitimate fan-out.
        if (enlisted.Ambient is not null && enlisted.Ambient != System.Transactions.Transaction.Current)
        {
            Throw.JobPersistenceException(
                $"The transaction the connection enlisted for scheduler '{InstanceName}' belongs to is no longer the "
                + "current one, so this operation would run with no transaction at all. Keep the enlistment scope inside "
                + "the transaction scope it was created in, and dispose it before that scope ends.");
        }

        var expected = DbProvider.ExpectedConnectionType();
        if (expected is not null && !expected.IsInstanceOfType(enlisted.Connection))
        {
            Throw.JobPersistenceException(
                $"The connection enlisted for scheduler '{InstanceName}' is {enlisted.Connection.GetType().FullName}, but "
                + $"this job store is configured for {expected.FullName}. A connection from a different provider cannot "
                + "carry its commands - configure both against the same one.");
        }

        if (!enlisted.TryClaim())
        {
            Throw.JobPersistenceException(
                $"The connection enlisted for scheduler '{InstanceName}' is already serving another job store operation. "
                + "An enlisted connection carries a single transaction and cannot be used concurrently, so scheduler calls "
                + "made inside an enlistment scope must be awaited one at a time.");
        }

        try
        {
            // Anything that is not open needs opening - Broken and Connecting are their own states, and
            // handing a broken connection to the delegate produces a provider error instead of any of
            // the diagnostics above.
            if (enlisted.Connection.State != ConnectionState.Open)
            {
                if (enlisted.Connection.State != ConnectionState.Closed)
                {
                    await enlisted.Connection.CloseAsync().ConfigureAwait(false);
                }

                await enlisted.Connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not a persistence failure, and callers match on it.
            enlisted.Release();
            throw;
        }
        catch (Exception e)
        {
            enlisted.Release();
            Throw.JobPersistenceException($"Failed to open the connection enlisted for scheduler '{InstanceName}': {e}", e);
        }

        return new ConnectionAndTransactionHolder(enlisted.Connection, enlisted.Transaction, ownsResources: false, borrowedFrom: enlisted, logger: ConnectionLogger);
    }

    /// <summary>
    /// Whether the current operation runs inside a transaction the application owns. That is the case
    /// only when the application enlisted a connection: a connection the job store opens for itself
    /// deliberately stays outside whatever the caller has in flight.
    /// </summary>
    private bool InApplicationOwnedTransaction =>
        AcceptEnlistedTransactions && AmbientConnection.Get(InstanceName) is not null;

    /// <summary>
    /// Opens a connection that belongs to the job store.
    /// </summary>
    /// <remarks>
    /// While <see cref="AcceptEnlistedTransactions" /> is on, such a connection is kept out of any
    /// ambient <see cref="System.Transactions.Transaction" />. The application takes part by enlisting
    /// a connection, not by the job store quietly joining a scope whose outcome it does not control;
    /// letting it enlist would also put a second connection in that transaction, which needs a
    /// distributed transaction and is not available on every provider.
    /// </remarks>
    private async ValueTask<DbConnection> OpenOwnConnection(CancellationToken cancellationToken)
    {
        using var ambientSuppression = AcceptEnlistedTransactions && System.Transactions.Transaction.Current is not null
            ? new System.Transactions.TransactionScope(
                System.Transactions.TransactionScopeOption.Suppress,
                System.Transactions.TransactionScopeAsyncFlowOption.Enabled)
            : null;

        var conn = DbProvider.CreateConnection();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        return conn;
    }

    /// <summary>
    /// Gets the connection and starts a new transaction.
    /// </summary>
    /// <returns></returns>
    protected virtual async ValueTask<ConnectionAndTransactionHolder> GetConnection(CancellationToken cancellationToken = default)
    {
        var enlisted = await GetEnlistedConnection(cancellationToken).ConfigureAwait(false);
        if (enlisted is not null)
        {
            return enlisted;
        }

        DbConnection conn;
        DbTransaction tx;
        try
        {
            conn = await OpenOwnConnection(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException($"Failed to obtain DB connection from data source '{DataSource}': {e}", e);
            return default;
        }

        try
        {
            // Quartz's own default rather than the provider's, which varies -- MySQL's is repeatable
            // read -- and would make the store behave differently depending on which database it is
            // talking to.
            tx = await conn.BeginTransactionAsync(
                TransactionIsolationLevel ?? IsolationLevel.ReadCommitted,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            await conn.CloseAsync().ConfigureAwait(false);
            Throw.JobPersistenceException("Failure setting up connection.", e);
            return default;
        }

        return new ConnectionAndTransactionHolder(conn, tx, ownsResources: true, borrowedFrom: null, logger: ConnectionLogger);
    }

    protected async ValueTask ReleaseLock(
        Guid requestorId,
        SchedulerLock? lockKind,
        bool shouldRelease,
        CancellationToken cancellationToken = default)
    {
        if (shouldRelease && lockKind is not null)
        {
            try
            {
                await LockHandler.ReleaseLock(requestorId, lockKind.Value, cancellationToken).ConfigureAwait(false);
            }
            catch (LockException le)
            {
                Logger.LockReleaseFailed(le.Message, le);
            }
        }
    }

    protected internal ValueTask SignalSchedulingChangeImmediately(
        DateTimeOffset? candidateNewNextFireTime,
        CancellationToken cancellationToken = default)
    {
        return schedSignaler.SignalSchedulingChange(candidateNewNextFireTime, cancellationToken);
    }

    /// <summary>
    /// Holds back a scheduling change signal until the transaction the application owns has completed.
    /// Signalling while our rows are still uncommitted would send the scheduler thread looking for a
    /// trigger it cannot see yet, and it would then wait out the idle interval before looking again.
    /// </summary>
    internal void SignalSchedulingChangeOnApplicationCommit(
        ConnectionAndTransactionHolder conn,
        DateTimeOffset? candidateNewNextFireTime,
        CancellationToken cancellationToken)
    {
        void Signal(DateTimeOffset? signalTime)
        {
            // Fire and forget: the signaler only wakes the scheduler thread, and this runs from a
            // transaction completion callback that has nothing to await it.
            _ = SignalSchedulingChangeImmediately(signalTime, cancellationToken).AsTask();
        }

        var enlisted = conn.BorrowedFrom;
        if (enlisted is null)
        {
            Signal(candidateNewNextFireTime);
            return;
        }

        // Accumulate on the enlistment rather than in the handler, so every operation in the scope
        // contributes and the earliest candidate wins. Capturing one operation time in a closure would
        // let a later, sooner trigger go unannounced until the idle wait expired.
        enlisted.DeferSignal(candidateNewNextFireTime, Signal);

        // The transaction the enlistment was made under, not whatever is ambient now: an unrelated outer
        // scope governs nothing here, and handing it the signal would drop it when that scope aborts.
        var ambient = enlisted.Ambient;
        if (ambient is null)
        {
            // A bare DbTransaction reports no outcome, so the enlistment scope disposal is the only
            // moment we have; the caller is documented to dispose it after committing.
            return;
        }

        // An ambient transaction does report its outcome, and reports it after the enlistment scope is
        // disposed, so let it own the signal: nothing is raised when the application rolls back. Hooked
        // once per enlistment - a scope that schedules hundreds of jobs would otherwise accumulate that
        // many handlers and fire them all back to back, each able to knock the scheduler off its
        // acquired triggers.
        if (enlisted.AmbientSignalHooked)
        {
            return;
        }

        // Subscribe first: the add accessor throws once the transaction has been disposed, and latching
        // the flags before that would leave neither the ambient flush nor the scope fallback able to
        // raise the signal at all.
        ambient.TransactionCompleted += (_, e) =>
        {
            if (e.Transaction?.TransactionInformation.Status == System.Transactions.TransactionStatus.Committed)
            {
                enlisted.FlushSignal();
            }
        };

        enlisted.AmbientSignalHooked = true;
        enlisted.SignalOwnedByAmbient = true;
    }

    //---------------------------------------------------------------------------
    // Cluster management methods
    //---------------------------------------------------------------------------

    /// <summary>
    /// Cleanup the given database connection.  This means restoring
    /// any modified auto commit or transaction isolation connection
    /// attributes, and then closing the underlying connection.
    /// </summary>
    ///
    /// <remarks>
    /// This is separate from closeConnection() because the Spring
    /// integration relies on being able to overload closeConnection() and
    /// expects the same connection back that it originally returned
    /// from the datasource.
    /// </remarks>
    /// <seealso cref="CloseConnection(ConnectionAndTransactionHolder, CancellationToken)" />
    protected static async ValueTask CleanupConnection(
        ConnectionAndTransactionHolder? conn,
        CancellationToken cancellationToken = default)
    {
        if (conn is not null)
        {
            // Hand the enlisted connection back so the next operation on this flow can claim it.
            // Released through the holder rather than by looking the enlistment up again, which with
            // nested scopes can resolve to a different entry than the one that was claimed.
            conn.BorrowedFrom?.Release();

            await CloseConnection(conn, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Closes the supplied connection.
    /// </summary>
    /// <param name="cth">(Optional)</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    protected static async ValueTask CloseConnection(
        ConnectionAndTransactionHolder cth,
        CancellationToken cancellationToken = default)
    {
        await cth.Close(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Rollback the supplied connection.
    /// </summary>
    protected async ValueTask RollbackConnection(
        ConnectionAndTransactionHolder? cth,
        Exception cause,
        CancellationToken cancellationToken = default)
    {
        if (cth is null)
        {
            // db might be down or similar
            Logger.RollbackWithoutConnectionHolder();
            return;
        }

        await cth.Rollback(IsTransient(cause), cancellationToken).ConfigureAwait(false);
    }


    /// <summary>
    /// Whether a failure is worth retrying — a dropped connection, a deadlock victim, a database that
    /// is momentarily too busy — as opposed to one that will fail again just as surely the second time.
    /// </summary>
    /// <remarks>
    /// The seam for a store whose driver reports something Quartz does not know how to read. The
    /// default answer comes from <see cref="TransientErrorDetector" />: the driver's own
    /// <see cref="DbException.IsTransient" />, a SQLSTATE in class <c>40</c> (transaction rollback,
    /// <c>40002</c> excepted), SQL Server's transient error numbers, SQLite's busy and locked codes,
    /// and <see cref="TimeoutException" />, over the whole chain of inner exceptions.
    /// </remarks>
    /// <param name="ex">The exception to classify.</param>
    /// <returns>If the exception is identified as transient.</returns>
    protected virtual bool IsTransient(Exception ex) => TransientErrorDetector.IsTransient(ex);

    /// <summary>
    /// Commit the supplied connection.
    /// </summary>
    /// <param name="cth">The CTH.</param>
    /// <param name="openNewTransaction">if set to <c>true</c> opens a new transaction.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <throws>JobPersistenceException thrown if a SQLException occurs when the </throws>
    protected async ValueTask CommitConnection(
        ConnectionAndTransactionHolder cth,
        bool openNewTransaction,
        CancellationToken cancellationToken = default)
    {
        if (cth is null)
        {
            Logger.CommitWithoutConnectionHolder();
            return;
        }
        await cth.Commit(openNewTransaction, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Execute the given callback in a transaction, taking no lock.
    /// </summary>
    /// <remarks>
    /// Forwards to <see cref="ExecuteInLock{T}" /> with no lock — except under
    /// <see cref="LockAllOperations" />, where every operation including reads has to be serialized.
    /// </remarks>
    protected ValueTask<T> ExecuteWithoutLock<T>(
        Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback,
        CancellationToken cancellationToken = default)
    {
        // For SQLite, all operations must be serialized to avoid "database is locked" errors.
        // Route read operations through the same lock as write operations.
        SchedulerLock? lockKind = LockAllOperations ? SchedulerLock.TriggerAccess : null;
        return ExecuteInLock(lockKind, txCallback, cancellationToken);
    }

    /// <summary>
    /// Execute the given callback having acquired the given lock, when it produces no result.
    /// </summary>
    /// <param name="lockKind">
    /// The lock to acquire. If <see langword="null" />, then no lock is acquired, but the
    /// <paramref name="txCallback" /> is still executed in a transaction.
    /// </param>
    /// <param name="txCallback">
    /// The callback to execute after having acquired the given lock.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    protected async ValueTask ExecuteInLock(
        SchedulerLock? lockKind,
        Func<ConnectionAndTransactionHolder, ValueTask> txCallback,
        CancellationToken cancellationToken = default)
    {
        await ExecuteInLock<object?>(lockKind, async conn =>
        {
            await txCallback(conn).ConfigureAwait(false);
            return null;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Execute the given callback having acquired the given lock.
    /// Depending on the JobStore, the surrounding transaction may be
    /// assumed to be already present (managed).
    /// </summary>
    /// <param name="lockKind">
    /// The lock to acquire. If <see langword="null" />, then no lock is acquired, but the
    /// <paramref name="txCallback" /> is still executed in a transaction.
    /// </param>
    /// <param name="txCallback">
    /// The callback to execute after having acquired the given lock.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    protected abstract ValueTask<T> ExecuteInLock<T>(
        SchedulerLock? lockKind,
        Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Keep retrying <see cref="ExecuteInLocalTransactionLock{T}" /> until it succeeds or the store
    /// shuts down, for work the scheduler cannot simply abandon — cluster recovery and misfire
    /// handling, whose failure is almost always a database that is temporarily gone.
    /// </summary>
    protected async ValueTask RetryExecuteInLocalTransactionLock(
        SchedulerLock? lockKind,
        Func<ConnectionAndTransactionHolder, ValueTask> txCallback,
        CancellationToken cancellationToken = default)
    {
        await RetryExecuteInLocalTransactionLock<object?>(lockKind, async holder =>
        {
            await txCallback(holder).ConfigureAwait(false);
            return null;
        }, requestorId: null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc cref="RetryExecuteInLocalTransactionLock(SchedulerLock?, Func{ConnectionAndTransactionHolder, ValueTask}, CancellationToken)" />
    /// <param name="lockKind">
    /// The lock to acquire. If <see langword="null" />, then no lock is acquired.
    /// </param>
    /// <param name="txCallback">The callback to execute after having acquired the given lock.</param>
    /// <param name="requestorId">
    /// The identity the lock is taken under. Pass the one an outer attempt used, so that a nested
    /// retry is recognised as the same owner rather than deadlocking against itself.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    protected async ValueTask<T> RetryExecuteInLocalTransactionLock<T>(
        SchedulerLock? lockKind,
        Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback,
        Guid? requestorId = null,
        CancellationToken cancellationToken = default)
    {
        for (int retry = 1; !shutdown; retry++)
        {
            try
            {
                return await ExecuteInLocalTransactionLock(lockKind, txCallback, txValidator: null, requestorId, cancellationToken).ConfigureAwait(false);
            }
            catch (JobPersistenceException jpe)
            {
                if (retry % RetryableActionErrorLogThreshold == 0)
                {
                    // No keys: the callback being retried is an arbitrary unit of store work, and the
                    // failure is the connection rather than anything the work was about.
                    SchedulerErrorContext error = new()
                    {
                        Message = "An error occurred during retry",
                        Exception = jpe,
                    };
                    await schedSignaler.NotifySchedulerListenersError(error, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Logger.RetryInLocalTransactionLockFailed(e.Message, e);
            }

            // retry every N seconds (the db connection must be failed)
            await Task.Delay(DbRetryInterval, timeProvider, cancellationToken).ConfigureAwait(false);
        }

        Throw.InvalidOperationException("JobStore is shutdown - aborting retry");
        return default;
    }

    /// <summary>
    /// Execute the given callback having optionally acquired the given lock, on a connection and
    /// transaction this store owns and commits itself, when the callback produces no result.
    /// </summary>
    protected async ValueTask ExecuteInLocalTransactionLock(
        SchedulerLock? lockKind,
        Func<ConnectionAndTransactionHolder, ValueTask> txCallback,
        CancellationToken cancellationToken = default)
    {
        await ExecuteInLocalTransactionLock<object?>(lockKind, async conn =>
        {
            await txCallback(conn).ConfigureAwait(false);
            return null;
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Execute the given callback having optionally acquired the given lock, on a connection and
    /// transaction this store owns and commits itself.
    /// </summary>
    /// <param name="lockKind">
    /// The lock to acquire. If <see langword="null" />, then no lock is acquired, but the
    /// <paramref name="txCallback" /> is still executed in a transaction of this store's own.
    /// </param>
    /// <param name="txCallback">
    /// The callback to execute after having acquired the given lock.
    /// </param>
    /// <param name="txValidator">
    /// Asked, when the commit fails, whether the work landed anyway. Trigger acquisition uses it: a
    /// commit that reported an error but did reach the database must not be retried, or the same
    /// triggers are acquired twice.
    /// </param>
    /// <param name="requestorId">
    /// The identity the lock is taken under. Defaults to the caller id of the current operation, and
    /// otherwise to a fresh one.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    protected async ValueTask<T> ExecuteInLocalTransactionLock<T>(
        SchedulerLock? lockKind,
        Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback,
        Func<ConnectionAndTransactionHolder, T, ValueTask<bool>>? txValidator = null,
        Guid? requestorId = null,
        CancellationToken cancellationToken = default)
    {
        if (requestorId is null)
        {
            requestorId = Core.Context.CallerId.Value;
            if (requestorId is null)
            {
                requestorId = Guid.NewGuid();
            }
        }

        // Retrying inside a transaction the application owns is pointless and harmful: the first failure
        // has already doomed that transaction on most providers, so a second attempt would only pile
        // another error on top of it. Let the caller decide what to do instead.
        bool applicationOwnedTransaction = InApplicationOwnedTransaction;
        int maxRetries = applicationOwnedTransaction ? 0 : MaxTransientRetries;
        int totalAttempts = maxRetries + 1;
        for (int attempt = 1; attempt <= totalAttempts; attempt++)
        {
            bool transOwner = false;
            ConnectionAndTransactionHolder? conn = null;
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

                    transOwner = await LockHandler.ObtainLock(requestorId.Value, conn, lockKind.Value, cancellationToken).ConfigureAwait(false);
                }

                if (conn is null)
                {
                    conn = await GetLocalTransactionConnection(cancellationToken).ConfigureAwait(false);
                }

                T result = await txCallback(conn).ConfigureAwait(false);
                try
                {
                    await CommitConnection(conn, false, cancellationToken).ConfigureAwait(false);
                }
                catch (JobPersistenceException jpe)
                {
                    await RollbackConnection(conn, jpe, cancellationToken).ConfigureAwait(false);
                    if (txValidator is null)
                    {
                        throw;
                    }
                    if (!await RetryExecuteInLocalTransactionLock(
                            lockKind,
                            async connection => await txValidator(connection, result).ConfigureAwait(false),
                            requestorId,
                            cancellationToken).ConfigureAwait(false))
                    {
                        throw;
                    }
                }

                DateTimeOffset? sigTime = conn.SignalSchedulingChangeOnTxCompletion;

                // Arrange a signal for after the commit even when the job store did not ask for one:
                // QuartzScheduler notifies the scheduler thread as soon as the store call returns, which
                // here is still before the application commits, so that notification finds nothing and
                // the thread settles down for a whole idle interval. Taking the lock stands in for "this
                // may have changed the schedule" - doing it for reads as well would signal an unknown
                // earlier time on every query and keep bouncing acquired triggers back to waiting. That
                // proxy does not hold once LockAllOperations routes reads through the lock too, so there
                // we fall back to an explicit request only.
                // Asked of the holder rather than the registry: a subclass overriding GetConnection can
                // return one it opened itself even while an enlistment exists on this flow.
                if (conn.BorrowedFrom is not null
                    && (sigTime is not null || lockKind is not null && !LockAllOperations))
                {
                    SignalSchedulingChangeOnApplicationCommit(conn, sigTime, cancellationToken);
                }
                else if (sigTime is not null)
                {
                    await SignalSchedulingChangeImmediately(sigTime, cancellationToken).ConfigureAwait(false);
                }

                return result;
            }
            catch (JobPersistenceException jpe)
            {
                await RollbackConnection(conn, jpe, cancellationToken).ConfigureAwait(false);
                if (attempt < totalAttempts && IsTransient(jpe))
                {
                    Logger.TransientFailureInLocalTransactionLock(attempt, totalAttempts, TransientRetryInterval, jpe);
                }
                else
                {
                    throw;
                }
            }
            catch (Exception e)
            {
                await RollbackConnection(conn, e, cancellationToken).ConfigureAwait(false);
                Throw.JobPersistenceException("Unexpected runtime exception: " + e.Message, e);
                return default;
            }
            finally
            {
                try
                {
                    await ReleaseLock(requestorId.Value, lockKind, transOwner, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    await CleanupConnection(conn, cancellationToken).ConfigureAwait(false);
                }
            }

            // Delay before the next attempt
            await Task.Delay(TransientRetryInterval, timeProvider, cancellationToken).ConfigureAwait(false);
        }

        Throw.InvalidOperationException("ExecuteInLocalTransactionLock retry loop exited unexpectedly");
        return default;
    }
}
