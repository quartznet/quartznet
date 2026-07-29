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

using System;
using System.Data.Common;
using System.Threading;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Keeps track of the connection and transaction that the current asynchronous flow has enlisted
/// for a given scheduler, so that <see cref="JobStoreSupport" /> can join the unit of work the
/// application already owns instead of opening a connection and starting a transaction of its own.
/// </summary>
/// <remarks>
/// The state flows with <see cref="AsyncLocal{T}" />, the same mechanism that carries
/// <see cref="System.Transactions.Transaction.Current" />. Entries form an immutable chain so that
/// nested enlistments - possibly for different schedulers - restore the previous value when disposed.
/// </remarks>
/// <seealso cref="SchedulerEnlistmentExtensions" />
internal static class AmbientConnection
{
    private static readonly AsyncLocal<EnlistedConnection?> current = new AsyncLocal<EnlistedConnection?>();

    /// <summary>
    /// Returns the connection enlisted for the given scheduler in the current asynchronous flow,
    /// or <see langword="null" /> when there is none.
    /// </summary>
    internal static EnlistedConnection? Get(string schedulerName)
    {
        EnlistedConnection? entry = current.Value;
        while (entry != null)
        {
            if (!entry.Disposed && string.Equals(entry.SchedulerName, schedulerName, StringComparison.Ordinal))
            {
                return entry;
            }

            entry = entry.Parent;
        }

        return null;
    }

    /// <summary>
    /// Enlists the given connection and transaction for the given scheduler until the returned
    /// scope is disposed.
    /// </summary>
    internal static IDisposable Enlist(
        string schedulerName,
        DbConnection connection,
        DbTransaction? transaction,
        System.Transactions.Transaction? ambient = null)
    {
        EnlistedConnection entry = new EnlistedConnection(schedulerName, connection, transaction, current.Value, ambient);
        current.Value = entry;
        return new EnlistmentScope(entry);
    }

    /// <summary>
    /// Hides every enlistment from the current asynchronous flow until the returned scope is
    /// disposed. Used for work that belongs to the scheduler rather than to the caller - optional
    /// column probing, cluster check-in, misfire recovery - which must run on a connection of its
    /// own whatever the caller enlisted.
    /// </summary>
    /// <remarks>
    /// Only the enlistment is hidden; keeping the connection the job store then opens out of an
    /// ambient transaction is handled where that connection is opened, so it applies to every job
    /// store connection and not just to suppressed work.
    /// </remarks>
    internal static IDisposable Suppress()
    {
        EnlistedConnection? previous = current.Value;
        if (previous == null)
        {
            return NullScope.Instance;
        }

        current.Value = null;
        return new SuppressionScope(previous);
    }

    private sealed class SuppressionScope : IDisposable
    {
        private readonly EnlistedConnection previous;
        private bool disposed;

        internal SuppressionScope(EnlistedConnection previous)
        {
            this.previous = previous;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            current.Value = previous;
        }
    }

    private sealed class NullScope : IDisposable
    {
        internal static readonly NullScope Instance = new NullScope();

        public void Dispose()
        {
        }
    }

    private sealed class EnlistmentScope : IDisposable
    {
        private readonly EnlistedConnection entry;

        internal EnlistmentScope(EnlistedConnection entry)
        {
            this.entry = entry;
        }

        public void Dispose()
        {
            if (entry.Disposed)
            {
                return;
            }

            entry.Disposed = true;

            // Only unwind when we are the innermost entry; out-of-order disposal leaves the chain
            // alone and relies on the disposed flag to hide the entry from lookups.
            if (ReferenceEquals(current.Value, entry))
            {
                current.Value = entry.Parent;
            }

            if (!entry.SignalOwnedByAmbient)
            {
                entry.FlushSignal();
            }
        }
    }
}

/// <summary>
/// A connection and transaction that application code has enlisted for a scheduler, together with
/// the scheduling change signal that is deferred until the enlistment ends.
/// </summary>
internal sealed class EnlistedConnection
{
    private readonly object signalLock = new object();
    private DateTimeOffset? pendingSignalTime;
    private bool signalPending;
    private Action<DateTimeOffset?>? signaler;
    private int inUse;

    internal EnlistedConnection(
        string schedulerName,
        DbConnection connection,
        DbTransaction? transaction,
        EnlistedConnection? parent,
        System.Transactions.Transaction? ambient)
    {
        SchedulerName = schedulerName;
        Connection = connection;
        Transaction = transaction;
        Parent = parent;
        Ambient = ambient;
    }

    internal string SchedulerName { get; }

    internal DbConnection Connection { get; }

    internal DbTransaction? Transaction { get; }

    /// <summary>
    /// The ambient transaction the connection was enlisted under, when the caller supplied no
    /// <see cref="DbTransaction" /> of its own. Kept so that a scope which has since ended can be
    /// recognised instead of letting the operation quietly autocommit.
    /// </summary>
    internal System.Transactions.Transaction? Ambient { get; }

    internal EnlistedConnection? Parent { get; }

    internal bool Disposed { get; set; }

    /// <summary>
    /// Whether a post-commit signal has already been hooked onto the ambient transaction, so that a
    /// scope containing many scheduling calls ends up with one handler rather than one per call.
    /// </summary>
    internal bool AmbientSignalHooked { get; set; }

    /// <summary>
    /// Whether an ambient transaction has taken over raising the deferred signal. It reports whether
    /// the work actually committed, which disposing this scope cannot, so the scope stays quiet and
    /// a rollback raises nothing.
    /// </summary>
    internal bool SignalOwnedByAmbient { get; set; }

    /// <summary>
    /// Claims the enlisted connection for one job store operation. A single connection cannot serve
    /// two operations at once, so a second concurrent claim is refused here with an explanation
    /// rather than left to surface as a provider-specific "a command is already in progress".
    /// </summary>
    internal bool TryClaim() => Interlocked.CompareExchange(ref inUse, 1, 0) == 0;

    internal void Release() => Interlocked.Exchange(ref inUse, 0);

    /// <summary>
    /// Remembers that the scheduler should be told about a scheduling change, but only once the
    /// application has committed. Signalling while the rows are still uncommitted would send the
    /// scheduler thread looking for a trigger it cannot see yet.
    /// </summary>
    internal void DeferSignal(DateTimeOffset? signalTime, Action<DateTimeOffset?> signalAction)
    {
        lock (signalLock)
        {
            signaler = signalAction;

            if (!signalPending)
            {
                signalPending = true;
                pendingSignalTime = signalTime;
                return;
            }

            // Keep the earliest candidate. A null candidate means "recompute from scratch" and wins
            // over any concrete time, since it makes the scheduler re-evaluate unconditionally.
            if (signalTime == null || pendingSignalTime == null)
            {
                pendingSignalTime = null;
            }
            else if (signalTime < pendingSignalTime)
            {
                pendingSignalTime = signalTime;
            }
        }
    }

    /// <summary>
    /// Fires the deferred scheduling change signal, if any. Called when the enlistment scope is
    /// disposed, which the caller is expected to do once it has committed.
    /// </summary>
    internal void FlushSignal()
    {
        Action<DateTimeOffset?>? signalAction;
        DateTimeOffset? signalTime;
        bool pending;
        lock (signalLock)
        {
            signalAction = signaler;
            signalTime = pendingSignalTime;
            pending = signalPending;
            signaler = null;
            pendingSignalTime = null;
            signalPending = false;
        }

        if (pending && signalAction != null)
        {
            signalAction(signalTime);
        }
    }
}
