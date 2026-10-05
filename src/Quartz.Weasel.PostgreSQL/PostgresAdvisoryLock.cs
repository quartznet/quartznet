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

using Npgsql;

using Weasel.Core.Migrations;
using Weasel.Postgresql;

namespace Quartz.Weasel.PostgreSQL;

/// <summary>
/// The session-level advisory lock every apply of a scheduler's schema takes first, on a connection of
/// its own.
/// </summary>
/// <remarks>
/// <para>
/// Waits rather than giving up at the first refusal, because the node that holds the lock is applying
/// exactly the change the waiting node was about to: once the holder is done, the waiter reads the schema
/// again and finds nothing left to do. <see cref="PostgresWeaselOptions.LockTimeout" /> bounds the wait.
/// </para>
/// <para>
/// A connection of its own rather than the one Weasel passes in, which Weasel disposes when the apply
/// ends. A session lock on a pooled connection is not released by disposing it — the pool keeps the
/// session — so a lock left on it would leave every later applier waiting out its timeout. Weasel releases
/// the lock after a failed apply too (from 9.37.0, JasperFx/weasel#659), but gives up quietly if that
/// release fails, so this connection is also released in <see cref="DisposeAsync" />, whichever way the
/// apply ended.
/// </para>
/// </remarks>
internal sealed class PostgresAdvisoryLock : IGlobalLock<NpgsqlConnection>, IAsyncDisposable
{
    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan LongestRetryDelay = TimeSpan.FromSeconds(1);

    private readonly Func<NpgsqlConnection> connections;
    private readonly int lockId;
    private readonly TimeSpan timeout;
    private readonly TimeProvider timeProvider;
    private NpgsqlConnection? held;

    public PostgresAdvisoryLock(Func<NpgsqlConnection> connections, int lockId, TimeSpan timeout, TimeProvider timeProvider)
    {
        this.connections = connections;
        this.lockId = lockId;
        this.timeout = timeout;
        this.timeProvider = timeProvider;
    }

    public async Task<AttainLockResult> TryAttainLock(NpgsqlConnection conn, CancellationToken ct = default)
    {
        NpgsqlConnection connection = connections();

        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);

            long started = timeProvider.GetTimestamp();
            TimeSpan delay = FirstRetryDelay;

            while (true)
            {
                AttainLockResult result = await connection.TryGetGlobalLock(lockId, ct).ConfigureAwait(false);

                if (result.Succeeded)
                {
                    held = connection;
                    return result;
                }

                if (result.ShouldReconnect || timeProvider.GetElapsedTime(started) >= timeout)
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                    return result;
                }

                await Task.Delay(delay, timeProvider, ct).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, LongestRetryDelay.Ticks));
            }
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public Task ReleaseLock(NpgsqlConnection conn, CancellationToken ct = default) => ReleaseHeldAsync(ct);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await ReleaseHeldAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (NpgsqlException)
        {
            // Best effort once the apply has already failed. An unlock that fails has almost always lost
            // the connection, which Npgsql then discards rather than pools, and the session ending is
            // what releases a session lock.
        }
    }

    private async Task ReleaseHeldAsync(CancellationToken ct)
    {
        NpgsqlConnection? connection = held;
        if (connection is null)
        {
            return;
        }

        held = null;

        try
        {
            await connection.ReleaseGlobalLock(lockId, ct).ConfigureAwait(false);
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
