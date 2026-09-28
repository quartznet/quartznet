using System.Data.Common;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// A fire that fails on the database after its own writes have gone out, for the shipped delegate of
/// each engine: the fired row is updated, the job's other triggers are moved to <c>BLOCKED</c>, the
/// trigger row is written, and then a statement the database refuses is issued in the same transaction.
/// That is the shape of a constraint violation or a schema drift on the last statement of a fire, and
/// it is what #3931 is about.
/// </summary>
/// <remarks>
/// Static, because the property bridge builds the delegate from its type name and the fixture never
/// holds the instance; the fixtures that use it are <c>[NonParallelizable]</c>.
/// </remarks>
internal static class FireFault
{
    private static readonly List<string> fireAttempts = [];

    /// <summary>The name of the trigger whose fire fails, or <see langword="null" /> for none.</summary>
    public static string FailFireOf { get; set; }

    /// <summary>Whether the fault clears itself after the first failure, so that the trigger fires on its next attempt.</summary>
    public static bool FailOnce { get; set; }

    /// <summary>Every fire the delegate was asked to write, by trigger name, in order — a rolled-back attempt included.</summary>
    public static List<string> FireAttempts
    {
        get
        {
            lock (fireAttempts)
            {
                return [.. fireAttempts];
            }
        }
    }

    public static void Reset()
    {
        lock (fireAttempts)
        {
            fireAttempts.Clear();
        }

        FailFireOf = null;
        FailOnce = false;
    }

    /// <summary>
    /// Writes the fire as the dialect's delegate does, then fails it if it is the one to fail.
    /// </summary>
    public static async ValueTask ApplyTriggerFired(
        Func<ValueTask> applyTriggerFired,
        ConnectionAndTransactionHolder conn,
        TriggerFiredUpdate update,
        CancellationToken cancellationToken)
    {
        string triggerName = update.Trigger.Key.Name;
        lock (fireAttempts)
        {
            fireAttempts.Add(triggerName);
        }

        await applyTriggerFired().ConfigureAwait(false);

        if (!string.Equals(triggerName, FailFireOf, StringComparison.Ordinal))
        {
            return;
        }

        if (FailOnce)
        {
            FailFireOf = null;
        }

        // A real exception from the driver, and one nothing classifies as transient — on PostgreSQL it
        // also leaves the transaction aborted, as any failed statement there does.
        using DbCommand command = conn.Connection.CreateCommand();
        conn.Attach(command);
        command.CommandText = "SELECT 1 FROM QRTZ_NO_SUCH_TABLE";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class FaultingPostgreSQLDelegate : PostgreSQLDelegate
{
    public override ValueTask ApplyTriggerFired(ConnectionAndTransactionHolder conn, TriggerFiredUpdate update, CancellationToken cancellationToken = default)
    {
        return FireFault.ApplyTriggerFired(() => base.ApplyTriggerFired(conn, update, cancellationToken), conn, update, cancellationToken);
    }
}

public sealed class FaultingSqlServerDelegate : SqlServerDelegate
{
    public override ValueTask ApplyTriggerFired(ConnectionAndTransactionHolder conn, TriggerFiredUpdate update, CancellationToken cancellationToken = default)
    {
        return FireFault.ApplyTriggerFired(() => base.ApplyTriggerFired(conn, update, cancellationToken), conn, update, cancellationToken);
    }
}

public sealed class FaultingMySQLDelegate : MySQLDelegate
{
    public override ValueTask ApplyTriggerFired(ConnectionAndTransactionHolder conn, TriggerFiredUpdate update, CancellationToken cancellationToken = default)
    {
        return FireFault.ApplyTriggerFired(() => base.ApplyTriggerFired(conn, update, cancellationToken), conn, update, cancellationToken);
    }
}

public sealed class FaultingOracleDelegate : OracleDelegate
{
    public override ValueTask ApplyTriggerFired(ConnectionAndTransactionHolder conn, TriggerFiredUpdate update, CancellationToken cancellationToken = default)
    {
        return FireFault.ApplyTriggerFired(() => base.ApplyTriggerFired(conn, update, cancellationToken), conn, update, cancellationToken);
    }
}

public sealed class FaultingFirebirdDelegate : FirebirdDelegate
{
    public override ValueTask ApplyTriggerFired(ConnectionAndTransactionHolder conn, TriggerFiredUpdate update, CancellationToken cancellationToken = default)
    {
        return FireFault.ApplyTriggerFired(() => base.ApplyTriggerFired(conn, update, cancellationToken), conn, update, cancellationToken);
    }
}
