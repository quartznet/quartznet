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

using System.Collections;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Reflection;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Decides whether a failure is worth retrying: a connection that dropped, a deadlock victim, a
/// database that is momentarily too busy — as opposed to a constraint violation or a typo in a
/// statement, which will fail again just as surely the second time.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AdoJobStoreBase.IsTransient" /> is the seam a store overrides to add its own driver's
/// verdict; this is what it answers with by default. Every signal is inclusive: the first one that
/// says "transient" wins, and the chain of inner exceptions is walked because the store wraps
/// almost everything it catches in a <see cref="JobPersistenceException" />.
/// </para>
/// <para>
/// This used to sniff for a property literally named <c>IsTransient</c> on the exception's type.
/// <see cref="DbException.IsTransient" /> has been on the base class since .NET 6, so the sniffing
/// bought nothing and cost a good deal: reading the property short-circuited the whole rest of the
/// method, which meant a driver reporting <see langword="false" /> — every SQLite driver does, for a
/// database that is merely busy — silently skipped the checks written for exactly that case.
/// </para>
/// </remarks>
internal static class TransientErrorDetector
{
    /// <summary>
    /// Whether <paramref name="exception" />, or anything it wraps, describes a failure that is worth
    /// retrying.
    /// </summary>
    public static bool IsTransient(Exception exception)
    {
        for (Exception? candidate = exception; candidate is not null; candidate = candidate.InnerException)
        {
            if (candidate is TimeoutException
                || candidate is DbException { IsTransient: true }
                || IsTransactionRollback(candidate)
                || IsTransientSqlServerError(candidate)
                || IsSqliteBusyOrLocked(candidate))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the exception reports a SQLSTATE in class <c>40</c> — the standard's own "transaction
    /// rollback" class, which is its way of saying the database abandoned the transaction for a reason
    /// that has nothing to do with the statements in it, so running it again is the prescribed answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SQLSTATE is provider-neutral, which is why it is worth reading. <c>40001</c>, the serialization
    /// failure, is what Firebird reports for a write conflict between two transactions, what MySQL
    /// reports for its <c>1213</c> deadlock, and what PostgreSQL reports beside its own extension
    /// <c>40P01</c>, deadlock detected. Npgsql and MySqlConnector already say as much through
    /// <see cref="DbException.IsTransient" />, so for those two this is belt and braces. Firebird and
    /// MySql.Data do not: <c>FbException</c> reports <c>IsTransient: false</c> for a serialization
    /// failure, so the store treated the one condition retrying exists for as fatal, wrapped it in a
    /// <see cref="JobPersistenceException" /> and gave up.
    /// </para>
    /// <para>
    /// <c>40002</c> is the single member of the class that is excluded. It is an integrity-constraint
    /// violation the database deferred to commit time — a real error, which will fail in exactly the
    /// same way on the next attempt. The rest of the class is transient: <c>40000</c> (rollback with no
    /// subclass), <c>40001</c>, <c>40003</c> (statement completion unknown) and <c>40P01</c>.
    /// </para>
    /// <para>
    /// This leaves the SQL Server path alone. <see cref="DbException.SqlState" /> is null on both
    /// SqlClients, as the error-number check below says, so 1205 and its neighbours still arrive the
    /// way they always did.
    /// </para>
    /// </remarks>
    private static bool IsTransactionRollback(Exception exception)
    {
        string? sqlState = GetSqlState(exception);

        // The class is the first two characters and the subclass is the rest, so the whole class is a
        // prefix match with the one exception carved out by name.
        return sqlState is not null
               && sqlState.StartsWith("40", StringComparison.Ordinal)
               && sqlState is not "40002";
    }

    /// <summary>
    /// The exception's SQLSTATE, read from whichever property the driver chose to spell it with, or
    /// <see langword="null" /> if it reports none.
    /// </summary>
    /// <remarks>
    /// <see cref="DbException.SqlState" /> is the property to ask, and Npgsql, MySqlConnector and
    /// MySql.Data all override it. Firebird does not: <c>FbException</c> declares a <c>SQLSTATE</c> of
    /// its own and leaves the inherited <see cref="DbException.SqlState" /> at its <see langword="null" />
    /// default, so the state that prompted all this is reachable only by name. Read by reflection for
    /// the reason the error numbers are — Quartz references no Firebird driver — and, like them, matched
    /// on shape rather than on a base class, so the <c>IscException</c> Firebird nests inside, which
    /// carries the same property, answers too.
    /// </remarks>
    private static string? GetSqlState(Exception exception)
    {
        if (exception is DbException { SqlState: { Length: > 0 } sqlState })
        {
            return sqlState;
        }

        PropertyInfo? sqlStateProperty = sqlStateProperties.GetOrAdd(
            exception.GetType(),
            static type => type.GetProperty("SQLSTATE", BindingFlags.Instance | BindingFlags.Public));

        return sqlStateProperty?.GetValue(exception) as string;
    }

    private static readonly ConcurrentDictionary<Type, PropertyInfo?> sqlStateProperties = new();

    /// <summary>
    /// Reads the SQL Server error numbers off an exception and matches them against the list below.
    /// </summary>
    /// <remarks>
    /// By reflection, because Quartz references no SQL Server driver and the numbers live on
    /// <c>SqlException.Errors[n].Number</c>, which <see cref="DbException" /> does not surface —
    /// <see cref="DbException.SqlState" /> is null on both SqlClients. The two property lookups are
    /// remembered per exception type, so this costs one dictionary read per error after the first.
    /// Not narrowed to <see cref="DbException" />: the shape is what is recognised, and a driver or a
    /// wrapper Quartz has never heard of that reports errors this way is recognised as it always was.
    /// </remarks>
    private static bool IsTransientSqlServerError(Exception exception)
    {
        PropertyInfo? errorsProperty = errorsProperties.GetOrAdd(
            exception.GetType(),
            static type => type.GetProperty("Errors", BindingFlags.Instance | BindingFlags.Public));

        if (errorsProperty?.GetValue(exception) is not IEnumerable errors)
        {
            return false;
        }

        // https://docs.microsoft.com/en-us/dotnet/api/system.data.sqlclient.sqlexception
        // "SqlException always contains at least one instance of SqlError"
        foreach (object? error in errors)
        {
            if (error is null)
            {
                continue;
            }

            PropertyInfo? numberProperty = numberProperties.GetOrAdd(
                error.GetType(),
                static type => type.GetProperty("Number", BindingFlags.Instance | BindingFlags.Public));

            if (numberProperty?.GetValue(error) is int errorNumber && IsTransientSqlServerErrorNumber(errorNumber))
            {
                return true;
            }
        }

        return false;
    }

    private static readonly ConcurrentDictionary<Type, PropertyInfo?> errorsProperties = new();

    private static readonly ConcurrentDictionary<Type, PropertyInfo?> numberProperties = new();

    /// <summary>
    /// Taken from https://github.com/aspnet/EntityFrameworkCore/blob/d59be61006d78d507dea07a9779c3c4103821ca3/src/EFCore.SqlServer/Storage/Internal/SqlServerTransientExceptionDetector.cs
    /// and merged with https://docs.microsoft.com/en-us/azure/sql-database/sql-database-develop-error-messages
    ///
    /// Copied from EFCore because it states "not intended to be used directly from your code" and we don't
    /// want EF leaking into Quartz.
    /// </summary>
    private static bool IsTransientSqlServerErrorNumber(int errorNumber)
    {
        switch (errorNumber)
        {
            // SQL Error Code: 49920
            // Cannot process request. Too many operations in progress for subscription "%ld".
            // The service is busy processing multiple requests for this subscription.
            // Requests are currently blocked for resource optimization. Query sys.dm_operation_status for operation status.
            // Wait until pending requests are complete or delete one of your pending requests and retry your request later.
            case 49920:
            // SQL Error Code: 49919
            // Cannot process create or update request. Too many create or update operations in progress for subscription "%ld".
            // The service is busy processing multiple create or update requests for your subscription or server.
            // Requests are currently blocked for resource optimization. Query sys.dm_operation_status for pending operations.
            // Wait till pending create or update requests are complete or delete one of your pending requests and
            // retry your request later.
            case 49919:
            // SQL Error Code: 49918
            // Cannot process request. Not enough resources to process request.
            // The service is currently busy.Please retry the request later.
            case 49918:
            // SQL Error Code: 41839
            // Transaction exceeded the maximum number of commit dependencies.
            case 41839:
            // SQL Error Code: 41325
            // The current transaction failed to commit due to a serializable validation failure.
            case 41325:
            // SQL Error Code: 41305
            // The current transaction failed to commit due to a repeatable read validation failure.
            case 41305:
            // SQL Error Code: 41302
            // The current transaction attempted to update a record that has been updated since the transaction started.
            case 41302:
            // SQL Error Code: 41301
            // Dependency failure: a dependency was taken on another transaction that later failed to commit.
            case 41301:
            // SQL Error Code: 40613
            // Database XXXX on server YYYY is not currently available. Please retry the connection later.
            // If the problem persists, contact customer support, and provide them the session tracing ID of ZZZZZ.
            case 40613:
            // SQL Error Code: 40501
            // The service is currently busy. Retry the request after 10 seconds. Code: (reason code to be decoded).
            case 40501:
            // SQL Error Code: 40197
            // The service has encountered an error processing your request. Please try again.
            case 40197:
            // SQL Error Code: 10929
            // Resource ID: %d. The %s minimum guarantee is %d, maximum limit is %d and the current usage for the database is %d.
            // However, the server is currently too busy to support requests greater than %d for this database.
            // For more information, see http://go.microsoft.com/fwlink/?LinkId=267637. Otherwise, please try again.
            case 10929:
            // SQL Error Code: 10928
            // Resource ID: %d. The %s limit for the database is %d and has been reached. For more information,
            // see http://go.microsoft.com/fwlink/?LinkId=267637.
            case 10928:
            // SQL Error Code: 10060
            // A network-related or instance-specific error occurred while establishing a connection to SQL Server.
            // The server was not found or was not accessible. Verify that the instance name is correct and that SQL Server
            // is configured to allow remote connections. (provider: TCP Provider, error: 0 - A connection attempt failed
            // because the connected party did not properly respond after a period of time, or established connection failed
            // because connected host has failed to respond.)"}
            case 10060:
            // SQL Error Code: 10054
            // A transport-level error has occurred when sending the request to the server.
            // (provider: TCP Provider, error: 0 - An existing connection was forcibly closed by the remote host.)
            case 10054:
            // SQL Error Code: 10053
            // A transport-level error has occurred when receiving results from the server.
            // An established connection was aborted by the software in your host machine.
            case 10053:
            // SQL Error Code: 1205
            // Deadlock
            case 1205:
            // SQL Error Code: 233
            // The client was unable to establish a connection because of an error during connection initialization process before login.
            // Possible causes include the following: the client tried to connect to an unsupported version of SQL Server;
            // the server was too busy to accept new connections; or there was a resource limitation (insufficient memory or maximum
            // allowed connections) on the server. (provider: TCP Provider, error: 0 - An existing connection was forcibly closed by
            // the remote host.)
            case 233:
            // SQL Error Code: 121
            // The semaphore timeout period has expired
            case 121:
            // SQL Error Code: 64
            // A connection was successfully established with the server, but then an error occurred during the login process.
            // (provider: TCP Provider, error: 0 - The specified network name is no longer available.)
            case 64:
            // DBNETLIB Error Code: 20
            // The instance of SQL Server you attempted to connect to does not support encryption.
            case 20:
            // Login to read - secondary failed due to long wait on 'HADR_DATABASE_WAIT_FOR_TRANSITION_TO_VERSIONING'.
            // The replica is not available for login because row versions are missing for transactions that were in-flight
            // when the replica was recycled.The issue can be resolved by rolling back or committing the active transactions on
            // the primary replica.Occurrences of this condition can be minimized by avoiding long write transactions on the primary.
            case 4221:
            // Cannot open database "%.*ls" requested by the login. The login failed
            case 4060:
            // SQL Error Code: 11001
            // A network-related or instance-specific error occurred while establishing a connection to SQL Server.
            // The server was not found or was not accessible. Verify that the instance name is correct and that SQL
            // Server is configured to allow remote connections. (provider: TCP Provider, error: 0 - No such host is known.)
            case 11001:
                return true;
                // This exception can be thrown even if the operation completed succesfully, so it's safer to let the application fail.
                // DBNETLIB Error Code: -2
                // Timeout expired. The timeout period elapsed prior to completion of the operation or the server is not responding. The statement has been terminated.
                //case -2:
            default:
                return false;
        }
    }

    /// <summary>
    /// Whether the exception is a SQLite BUSY (error code 5) or LOCKED (error code 6) error, meaning
    /// another connection holds the write lock and the operation is worth trying again.
    /// </summary>
    /// <remarks>
    /// By reflection, for the same reason as the SQL Server numbers: Quartz references no SQLite
    /// driver. The two shipped ones spell the code differently — Microsoft.Data.Sqlite as an
    /// <c>int SqliteErrorCode</c>, System.Data.SQLite as a <c>ResultCode</c> enum — and neither
    /// reports the condition through <see cref="DbException.IsTransient" />.
    /// </remarks>
    private static bool IsSqliteBusyOrLocked(Exception exception)
    {
        Type type = exception.GetType();
        if (type.Name is not "SqliteException" and not "SQLiteException")
        {
            return false;
        }

        PropertyInfo? sqliteErrorCode = sqliteErrorCodeProperties.GetOrAdd(
            type,
            static t => t.GetProperty("SqliteErrorCode", BindingFlags.Instance | BindingFlags.Public));

        if (sqliteErrorCode is not null)
        {
            return sqliteErrorCode.GetValue(exception) is 5 /* SQLITE_BUSY */ or 6 /* SQLITE_LOCKED */;
        }

        PropertyInfo? resultCode = sqliteResultCodeProperties.GetOrAdd(
            type,
            static t => t.GetProperty("ResultCode", BindingFlags.Instance | BindingFlags.Public));

        return resultCode?.GetValue(exception)?.ToString() is "Busy" or "Locked";
    }

    private static readonly ConcurrentDictionary<Type, PropertyInfo?> sqliteErrorCodeProperties = new();

    private static readonly ConcurrentDictionary<Type, PropertyInfo?> sqliteResultCodeProperties = new();
}
