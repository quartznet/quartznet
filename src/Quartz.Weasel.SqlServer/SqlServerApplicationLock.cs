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

using Microsoft.Data.SqlClient;

using Weasel.Core.Migrations;
using Weasel.SqlServer;

namespace Quartz.Weasel.SqlServer;

/// <summary>
/// The session-owned application lock every apply of a scheduler's schema takes first, on a connection
/// of its own.
/// </summary>
/// <remarks>
/// <para>
/// Taken with Weasel's own <c>sp_getapplock</c> calls, the ones <see cref="SqlServerGlobalLock" />
/// makes, but not through that type, for two reasons. It passes one <c>@LockTimeout</c> to a command
/// the connection's command timeout — 30 seconds by default — still bounds, so a longer wait ends in a
/// client-side timeout exception rather than a refusal; this asks in slices of half the command
/// timeout instead, until <see cref="SqlServerWeaselOptions.LockTimeout" /> is spent. And it takes the
/// lock on the connection Weasel passes in, which Weasel disposes when the apply ends.
/// </para>
/// <para>
/// A session lock on a pooled connection is not released by disposing the connection: the pool keeps the
/// session, and only resets it when the connection is next handed out. A lock left on it would therefore
/// leave every later applier waiting out its timeout. Weasel releases the lock after a failed apply too
/// (from 9.37.0, JasperFx/weasel#659), but gives up quietly if that release fails, so this connection is
/// also released in <see cref="DisposeAsync" />, whichever way the apply ended.
/// </para>
/// </remarks>
internal sealed class SqlServerApplicationLock : IGlobalLock<SqlConnection>, IAsyncDisposable
{
    private readonly Func<SqlConnection> connections;
    private readonly string resource;
    private readonly TimeSpan timeout;
    private readonly TimeProvider timeProvider;
    private SqlConnection? held;

    public SqlServerApplicationLock(Func<SqlConnection> connections, string resource, TimeSpan timeout, TimeProvider timeProvider)
    {
        this.connections = connections;
        this.resource = resource;
        this.timeout = timeout;
        this.timeProvider = timeProvider;
    }

    public async Task<AttainLockResult> TryAttainLock(SqlConnection conn, CancellationToken ct = default)
    {
        SqlConnection connection = connections();

        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);

            long started = timeProvider.GetTimestamp();

            while (true)
            {
                TimeSpan remaining = timeout - timeProvider.GetElapsedTime(started);
                int slice = SliceMilliseconds(remaining, connection.CommandTimeout);

                AttainLockResult result = await connection.TryAttainGlobalLock(resource, ct, slice).ConfigureAwait(false);

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
            }
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public Task ReleaseLock(SqlConnection conn, CancellationToken ct = default) => ReleaseHeldAsync(ct);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await ReleaseHeldAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (SqlException)
        {
            // Best effort once the apply has already failed. An unlock that fails has almost always lost
            // the connection, which SqlClient then discards rather than pools, and the session ending is
            // what releases a session lock.
        }
    }

    /// <summary>
    /// How long one <c>sp_getapplock</c> may wait: what is left of the timeout, and no more than half the
    /// connection's command timeout, so that the server answers before the client gives up on the command.
    /// </summary>
    /// <param name="remaining">What is left of the whole wait; zero or less asks once without waiting.</param>
    /// <param name="commandTimeoutSeconds">The connection's command timeout; zero means none.</param>
    internal static int SliceMilliseconds(TimeSpan remaining, int commandTimeoutSeconds)
    {
        double slice = Math.Max(0, remaining.TotalMilliseconds);

        if (commandTimeoutSeconds > 0)
        {
            slice = Math.Min(slice, commandTimeoutSeconds * 500.0);
        }

        return (int) Math.Min(Math.Ceiling(slice), int.MaxValue);
    }

    private async Task ReleaseHeldAsync(CancellationToken ct)
    {
        SqlConnection? connection = held;
        if (connection is null)
        {
            return;
        }

        held = null;

        try
        {
            await connection.ReleaseGlobalLock(resource, ct).ConfigureAwait(false);
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
