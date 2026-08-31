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

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// An interface for providing thread/resource locking in order to protect
/// resources from being altered by multiple threads at the same time.
/// </summary>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public interface ILockHandler
{
    /// <summary>
    /// Called once by the job store before the lock handler is used, telling the handler which
    /// scheduler it locks for. The default implementation does nothing, which suits a handler
    /// that does not key its locks by scheduler identity.
    /// </summary>
    void Initialize(LockHandlerContext context)
    {
    }

    /// <summary>
    /// Grants a lock on the identified resource to the calling context, waiting until it is
    /// available.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The answer is a release obligation rather than a report of success. <see langword="true" />
    /// says this call took the lock and its caller is the one that has to give it back;
    /// <see langword="false" /> says <paramref name="requestorId" /> already held it — a re-entrant
    /// acquire — so the outer caller's single release is what frees it, and releasing here would
    /// drop a lock that operation is still relying on. A handler that instead counts its holds may
    /// answer <see langword="true" /> to a re-entrant acquire, because there the inner release is
    /// the one that decrements; either way the caller releases exactly when it was told
    /// <see langword="true" />.
    /// </para>
    /// <para>
    /// <b>Re-entry is the only thing <see langword="false" /> may mean.</b> A handler that could not
    /// take the lock says so by throwing: <see cref="LockException" /> when the lock was refused,
    /// and <see cref="OperationCanceledException" /> when
    /// <paramref name="cancellationToken" /> fired. Answering <see langword="false" /> on
    /// cancellation is not a harmless approximation — the job store reads it as "already held, do
    /// not release" and goes on to run the guarded operation with no lock at all, which is the whole
    /// point of taking one.
    /// </para>
    /// <para>
    /// The store's own ordering must not be relied on to cover for a handler that gets this wrong. A
    /// handler answering <see langword="false" /> to <see cref="RequiresConnection" /> is followed
    /// immediately by a connection open on the same token, so a mistake here is masked today by that
    /// open throwing first. That is an accident of statement order rather than a guarantee, and it
    /// does not hold wherever the store reaches the lock in a different sequence.
    /// </para>
    /// <para>
    /// A handler that gives up leaves nothing behind: an abandoned wait must not consume a handover
    /// meant for the next waiter, and a partially taken lock is released before the exception
    /// escapes, so that the next caller can still be served.
    /// </para>
    /// </remarks>
    /// <param name="requestorId">
    /// Identifies the calling context, so that a re-entrant acquire can be told from a competing one.
    /// </param>
    /// <param name="conn">
    /// The unit of work to take the lock on. It is <see langword="null" /> when the store has not
    /// opened a connection yet, which it delays for a handler whose <see cref="RequiresConnection" />
    /// is <see langword="false" />.
    /// </param>
    /// <param name="lockKind">Which of the two locks to take.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>
    /// <see langword="true" /> if this call took the lock and its caller must release it;
    /// <see langword="false" /> if <paramref name="requestorId" /> already held it and must not.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken" /> fired before the lock was taken.
    /// </exception>
    /// <exception cref="LockException">The lock could not be obtained.</exception>
    ValueTask<bool> AcquireLock(
        Guid requestorId,
        ConnectionAndTransactionHolder? conn,
        SchedulerLock lockKind,
        CancellationToken cancellationToken = default);

    /// <summary> Release the lock on the identified resource if it is held by the calling
    /// thread.
    /// </summary>
    ValueTask ReleaseLock(
        Guid requestorId,
        SchedulerLock lockKind,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether this lock handler requires a database connection for its lock
    /// management operations.
    /// </summary>
    /// <seealso cref="AcquireLock" />
    /// <seealso cref="ReleaseLock" />
    bool RequiresConnection { get; }
}