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

using System.Globalization;

using MySqlConnector;

using Weasel.Core.Migrations;

namespace Quartz.Weasel.MySQL;

/// <summary>
/// The named user lock (<c>GET_LOCK</c>) every apply of a scheduler's schema takes first, on a connection
/// of its own.
/// </summary>
/// <remarks>
/// <para>
/// Weasel has no global lock for MySQL, so this is Quartz's own. <c>GET_LOCK</c> needs no privilege, which
/// is why it is the lock rather than a table: the tables are what the apply is here to create.
/// </para>
/// <para>
/// <c>GET_LOCK</c> waits inside a command, and the connection's command timeout — 30 seconds by default —
/// still bounds that command, so a longer wait would end in a client-side timeout instead of a refusal.
/// This asks in slices of half the command timeout instead, until
/// <see cref="MySqlWeaselOptions.LockTimeout" /> is spent. The server takes whole seconds.
/// </para>
/// <para>
/// A user lock belongs to the session, and a pooled connection keeps its session when it is disposed until
/// the pool resets it on the next use. A failed apply would therefore leave every later applier waiting
/// out its timeout. Weasel releases the lock after a failed apply too (from 9.37.0, JasperFx/weasel#659),
/// but gives up quietly if that release fails, so this connection is also released in
/// <see cref="DisposeAsync" />, whichever way the apply ended.
/// </para>
/// </remarks>
internal sealed class MySqlUserLock : IGlobalLock<MySqlConnection>, IAsyncDisposable
{
    private const string GetLockSql = "SELECT GET_LOCK(@name, @timeout)";
    private const string ReleaseLockSql = "SELECT RELEASE_LOCK(@name)";

    private readonly Func<MySqlConnection> connections;
    private readonly string name;
    private readonly TimeSpan timeout;
    private readonly TimeProvider timeProvider;
    private MySqlConnection? held;

    public MySqlUserLock(Func<MySqlConnection> connections, string name, TimeSpan timeout, TimeProvider timeProvider)
    {
        this.connections = connections;
        this.name = name;
        this.timeout = timeout;
        this.timeProvider = timeProvider;
    }

    public async Task<AttainLockResult> TryAttainLock(MySqlConnection conn, CancellationToken ct = default)
    {
        MySqlConnection connection = connections();

        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);

            long started = timeProvider.GetTimestamp();

            while (true)
            {
                if (await TryGetLockAsync(connection, timeout - timeProvider.GetElapsedTime(started), ct).ConfigureAwait(false))
                {
                    held = connection;
                    return AttainLockResult.Success;
                }

                if (timeProvider.GetElapsedTime(started) >= timeout)
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                    return AttainLockResult.Failure();
                }
            }
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public Task ReleaseLock(MySqlConnection conn, CancellationToken ct = default) => ReleaseHeldAsync(ct);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await ReleaseHeldAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (MySqlException)
        {
            // Best effort once the apply has already failed. An unlock that fails has almost always lost
            // the connection, which MySqlConnector then discards rather than pools, and the session ending
            // is what releases a user lock.
        }
    }

    /// <summary>
    /// How many seconds one <c>GET_LOCK</c> may wait: what is left of the timeout, rounded up to the whole
    /// seconds the server takes, and no more than half the command's timeout, so that the server answers
    /// before the client gives up on the command.
    /// </summary>
    /// <param name="remaining">What is left of the whole wait; zero or less asks once without waiting.</param>
    /// <param name="commandTimeoutSeconds">The command's timeout; zero means none.</param>
    internal static int SliceSeconds(TimeSpan remaining, int commandTimeoutSeconds)
    {
        double slice = Math.Max(0, Math.Ceiling(remaining.TotalSeconds));

        if (commandTimeoutSeconds > 0)
        {
            slice = Math.Min(slice, Math.Max(1, commandTimeoutSeconds / 2));
        }

        return (int) Math.Min(slice, int.MaxValue);
    }

    private async Task ReleaseHeldAsync(CancellationToken ct)
    {
        MySqlConnection? connection = held;
        if (connection is null)
        {
            return;
        }

        held = null;

        try
        {
            MySqlCommand command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = ReleaseLockSql;
                command.Parameters.AddWithValue("@name", name);
                await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>One <c>GET_LOCK</c>, waiting at most what is left of the timeout, in whole seconds.</summary>
    private async Task<bool> TryGetLockAsync(MySqlConnection connection, TimeSpan remaining, CancellationToken ct)
    {
        MySqlCommand command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = GetLockSql;
            command.Parameters.AddWithValue("@name", name);
            command.Parameters.AddWithValue("@timeout", SliceSeconds(remaining, command.CommandTimeout));

            // 1 when the lock is this session's, 0 when the wait ran out, NULL when the server gave up on it.
            object? result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return result is not null and not DBNull && Convert.ToInt64(result, CultureInfo.InvariantCulture) == 1;
        }
    }
}
