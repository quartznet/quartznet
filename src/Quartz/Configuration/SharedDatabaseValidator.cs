using System.Data.Common;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Logging;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Configuration;

/// <summary>
/// Reports two schedulers of one container that share a database and disagree about the table prefix.
/// </summary>
/// <remarks>
/// <para>
/// The supported arrangement for tenants sharing a database is one table set and one prefix, with
/// <c>SCHED_NAME</c> telling the tenants apart — every Quartz table has it as the first column of its
/// primary key and every statement filters on it. Separate table sets in one database are legal, and
/// occasionally deliberate for backup or permissions reasons, but a prefix that differs by *accident*
/// produces the worst failure this area has: the misconfigured scheduler connects, passes
/// <c>PerformSchemaValidation</c> against its own empty table set, starts, reports healthy, and fires
/// nothing ever again.
/// </para>
/// <para>
/// Schema validation is the natural neighbour and cannot see this — it asks whether the tables this
/// store reads exist, and they do. Nor can the store: "another scheduler shares this database" is only
/// knowable one level up, where the schedulers of a container are all visible. So the check lives here,
/// and each scheduler records its arrangement as it is created; the first pair that disagrees is
/// reported.
/// </para>
/// <para>
/// It only ever warns. Deliberately separate table sets are a legitimate arrangement, and an error that
/// fires on a legitimate arrangement is worse than the silence it replaces — so the message names both
/// schedulers and both prefixes and lets the reader decide which of the two they meant.
/// </para>
/// </remarks>
internal sealed class SharedDatabaseValidator
{
    private readonly ILogger<SharedDatabaseValidator> logger;
    private readonly Lock syncRoot = new();

    /// <summary>
    /// The schedulers seen so far, grouped by the database they talk to. The key is an opaque identity —
    /// a <see cref="DbDataSource"/> instance, or a hash of the connection string — never anything that
    /// could be logged.
    /// </summary>
    private readonly Dictionary<object, List<Arrangement>> byDatabase = [];

    public SharedDatabaseValidator(ILogger<SharedDatabaseValidator> logger)
    {
        this.logger = logger;
    }

    /// <summary>
    /// Records one scheduler's arrangement and reports it against the ones already recorded.
    /// </summary>
    /// <param name="schedulerName">The scheduler's name, as it will appear in <c>SCHED_NAME</c>.</param>
    /// <param name="jobStore">
    /// The scheduler's job store. Anything that is not a database store is ignored: an in-memory
    /// scheduler shares no database with anybody.
    /// </param>
    public void Validate(string schedulerName, IJobStore jobStore)
    {
        try
        {
            Record(schedulerName, jobStore);
        }
        catch (Exception e)
        {
            // A diagnostic must never be the reason a scheduler fails to start. The one thing below that
            // can throw is a job store of somebody else's answering for its connection details, and a
            // provider that refuses to say could not have been compared with anything anyway.
            logger.SharedDatabaseCheckUnavailable(schedulerName, e);
        }
    }

    private void Record(string schedulerName, IJobStore jobStore)
    {
        if (jobStore is not AdoJobStoreBase store)
        {
            return;
        }

        object? database = DatabaseIdentity(store.DbProvider);
        if (database is null)
        {
            // Nothing identifies the database - a provider that keeps its connection details to itself.
            // Guessing here is how a check like this starts firing on unrelated schedulers.
            return;
        }

        Arrangement arrangement = new(schedulerName, store.DataSource, store.TablePrefix);
        List<Arrangement> disagreeing;

        lock (syncRoot)
        {
            if (!byDatabase.TryGetValue(database, out List<Arrangement>? sharing))
            {
                byDatabase[database] = [arrangement];
                return;
            }

            // A scheduler that records itself twice is the same scheduler, not a second one sharing the
            // database with itself.
            sharing.RemoveAll(other => string.Equals(other.SchedulerName, schedulerName, StringComparison.OrdinalIgnoreCase));

            // Prefixes are compared ignoring case because an unquoted identifier is folded to one case by
            // every database Quartz supports, so 'qrtz_' and 'QRTZ_' are the same table set and reporting
            // them would be a false positive.
            disagreeing = sharing.FindAll(other =>
                !string.Equals(other.TablePrefix, arrangement.TablePrefix, StringComparison.OrdinalIgnoreCase));

            sharing.Add(arrangement);
        }

        foreach (Arrangement other in disagreeing)
        {
            logger.SchedulersShareDatabaseWithDifferentPrefixes(
                arrangement.SchedulerName,
                arrangement.DataSource,
                arrangement.TablePrefix,
                other.SchedulerName,
                other.DataSource,
                other.TablePrefix);
        }
    }

    /// <summary>
    /// What identifies the database a provider talks to, or <see langword="null"/> when nothing does.
    /// </summary>
    private static object? DatabaseIdentity(IDbProvider provider)
    {
        if (provider is DataSourceDbProvider dataSourceProvider)
        {
            // A DbDataSource holds its own connection details and reports no connection string here, so
            // the data source object is the identity. Two schedulers pointed at one registered
            // DbDataSource hold the same instance, which is exactly the arrangement this looks for.
            return dataSourceProvider.DataSource;
        }

        string connectionString = provider.ConnectionString;
        return string.IsNullOrWhiteSpace(connectionString) ? null : Fingerprint(connectionString);
    }

    /// <summary>
    /// Reduces a connection string to a value that is equal for two strings describing the same
    /// database, and that carries no credentials.
    /// </summary>
    /// <remarks>
    /// Hashed rather than kept, because this dictionary lives as long as the container and a connection
    /// string holds a password. The normalization before it only makes the check catch more true
    /// positives — two strings spelling the same settings in a different order or case. Two strings that
    /// reach the same database by different spellings of a *key* (<c>Server</c> against
    /// <c>Data Source</c>) still fingerprint differently and simply go unreported, which is the right
    /// direction to be wrong in.
    /// </remarks>
    private static string Fingerprint(string connectionString)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(connectionString))));
    }

    private static string Normalize(string connectionString)
    {
        try
        {
            DbConnectionStringBuilder builder = new() { ConnectionString = connectionString };
            List<string> pairs = new(builder.Count);
            foreach (object key in builder.Keys)
            {
                string name = (string) key;
                pairs.Add(name + "=" + builder[name]);
            }

            pairs.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join(';', pairs).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            // Not a keyword/value connection string the builder can parse. It still identifies a
            // database, so it is compared as written.
            return connectionString.Trim().ToLowerInvariant();
        }
    }

    private readonly record struct Arrangement(string SchedulerName, string DataSource, string TablePrefix);
}
