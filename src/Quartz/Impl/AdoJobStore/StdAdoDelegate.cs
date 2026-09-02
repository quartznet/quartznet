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

using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Specialized;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Quartz.Diagnostics;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Extensibility;
using Quartz.Util;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// This is meant to be an abstract base class for most, if not all, <see cref="IDriverDelegate" />
/// implementations. Subclasses should override only those methods that need
/// special handling for the DBMS driver in question.
/// </summary>
public partial class StdAdoDelegate : IDriverDelegate, IDbAccessor
{
    private const string FileScanListenerName = "FILE_SCAN_LISTENER_NAME";
    private const string DirectoryScanListenerName = "DIRECTORY_SCAN_LISTENER_NAME";

    private ILogger<StdAdoDelegate> logger = null!;
    private string tablePrefix = AdoConstants.DefaultTablePrefix;
    private string instanceId = null!;
    private string schedulerName = null!;
    private bool useProperties;

    private ITypeLoader typeLoader = null!;
    private AdoUtil adoUtil = null!;

    /// <summary>
    /// The registered persistence delegates, and the same set indexed by the discriminator each one
    /// writes into TRIGGER_TYPE.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Looking a delegate up by discriminator is on the path of every trigger read and every trigger
    /// write, and it was a scan of the list asking each delegate in turn what type it handles. The
    /// index answers it without the virtual calls.
    /// </para>
    /// <para>
    /// Replaced wholesale rather than mutated, because <see cref="AddTriggerPersistenceDelegate" /> is
    /// public: registrations all happen during <see cref="Initialize" /> in practice, but nothing stops
    /// an application adding one to a delegate that is already serving a scheduler, and a reader mid-
    /// lookup must not see a half-built index. Adding one is rare enough that copying is free.
    /// </para>
    /// </remarks>
    private volatile ITriggerPersistenceDelegate[] triggerPersistenceDelegates = [];

    private volatile FrozenDictionary<string, ITriggerPersistenceDelegate> triggerPersistenceDelegatesByDiscriminator
        = FrozenDictionary<string, ITriggerPersistenceDelegate>.Empty;

    private readonly Lock triggerPersistenceDelegateLock = new();

    private IObjectSerializer objectSerializer = null!;
    private TimeProvider timeProvider = null!;

    private readonly ConcurrentDictionary<string, string> cachedQueries = new();

    /// <summary>
    /// The acquisition statement for each <see cref="TriggerAcquisitionSqlShape" /> it has been asked
    /// for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The statement carries its limit in the text — a <c>SELECT TOP n</c> spliced into the projection
    /// on SQL Server, a <c>rownum</c> wrapper on Oracle, a trailing <c>LIMIT n</c> elsewhere — so the
    /// dialect rebuilds around a kilobyte of SQL for every acquisition, and the table-prefix cache then
    /// hashes all of it to hand back a string it already had. It is a pure function of its shape, so
    /// the finished statement is remembered against the shape instead — which is why the shape is a
    /// record: adding an acquisition dimension extends the key without touching this cache.
    /// </para>
    /// <para>
    /// The shape takes few distinct values: acquisition asks for the smaller of the free thread count
    /// and the configured batch size, and the exclusion count is bucketed, so the dictionary settles
    /// within a handful of entries.
    /// </para>
    /// </remarks>
    private readonly ConcurrentDictionary<TriggerAcquisitionSqlShape, string> acquisitionSqlByShape = new();

    /// <summary>
    /// The misfire recovery statement for each row limit it has been asked for, remembered for the
    /// same reason and settling on as few entries.
    /// </summary>

    private readonly ConcurrentDictionary<int, string> misfireRecoverySqlByCount = new();

    /// <summary>
    /// The database this delegate reads and writes through.
    /// </summary>
    protected IDbProvider DbProvider { get; private set; } = null!;

    /// <summary>
    /// The name of the scheduler this delegate serves, which every statement here is scoped by.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A dialect delegate that writes a statement of its own rather than adjusting one of the base
    /// class's needs the same value, and the base class already has it: without this it would have to
    /// override <see cref="Initialize" /> to keep a second copy from
    /// <see cref="DriverDelegateContext" />.
    /// </para>
    /// <para>
    /// A property over the field rather than the field turned into an auto-property, so that the
    /// hundred statements below go on reading <c>schedulerName</c> and this stays one added line.
    /// </para>
    /// </remarks>
    protected string SchedulerName => schedulerName;

    /// <summary>
    /// Initializes the driver delegate.
    /// </summary>
    public virtual void Initialize(DriverDelegateContext context)
    {
        // The category stays this type's rather than the dialect subclass's, so a log filter written
        // against it keeps matching whichever database the scheduler turns out to be talking to.
        logger = context.LoggerFactory.CreateLogger<StdAdoDelegate>();
        tablePrefix = context.TablePrefix;
        schedulerName = context.SchedulerName;
        instanceId = context.InstanceId;
        DbProvider = context.DbProvider;
        typeLoader = context.TypeLoader;
        useProperties = context.UseProperties;
        adoUtil = new AdoUtil(context.DbProvider, context.CommandTimeout, context.LoggerFactory.CreateLogger<AdoUtil>());
        objectSerializer = context.ObjectSerializer!;
        timeProvider = context.TimeProvider;

        AddDefaultTriggerPersistenceDelegates();

        foreach (ITriggerPersistenceDelegate persistenceDelegate in context.TriggerPersistenceDelegates)
        {
            AddTriggerPersistenceDelegate(persistenceDelegate);
        }
    }

    /// <summary>
    /// Registers the persistence delegates for the five trigger families Quartz ships.
    /// </summary>
    /// <remarks>
    /// Called before the delegates a container supplied, so those override a built-in one by
    /// declaring the same discriminator.
    /// </remarks>
    protected virtual void AddDefaultTriggerPersistenceDelegates()
    {
        AddTriggerPersistenceDelegate(new SimpleTriggerPersistenceDelegate());
        AddTriggerPersistenceDelegate(new CronTriggerPersistenceDelegate());
        AddTriggerPersistenceDelegate(new CalendarIntervalTriggerPersistenceDelegate());
        AddTriggerPersistenceDelegate(new DailyTimeIntervalTriggerPersistenceDelegate());
        AddTriggerPersistenceDelegate(new RecurrenceTriggerPersistenceDelegate());
    }

    /// <summary>
    /// Whether job data is stored as a name-value table of strings rather than as a serialized map.
    /// </summary>
    /// <remarks>
    /// The store's <c>StoreJobDataAsStrings</c> setting. A store that has written blobs cannot be
    /// switched to strings without rewriting them, which is why this is read rather than decided here.
    /// </remarks>
    protected virtual bool CanUseProperties => useProperties;

    //---------------------------------------------------------------------------
    // startup / recovery
    //---------------------------------------------------------------------------

    /// <summary>
    /// The statements <see cref="ClearData" /> issues, in the order the foreign keys require: the
    /// type tables before TRIGGERS, TRIGGERS before JOB_DETAILS.
    /// </summary>
    private static readonly string[] clearDataStatements =
    [
        StdAdoConstants.SqlDeleteAllSimpleTriggers,
        StdAdoConstants.SqlDeleteAllSimpropTriggers,
        StdAdoConstants.SqlDeleteAllCronTriggers,
        StdAdoConstants.SqlDeleteAllBlobTriggers,
        StdAdoConstants.SqlDeleteAllTriggers,
        StdAdoConstants.SqlDeleteAllJobDetails,
        StdAdoConstants.SqlDeleteAllCalendars,
        StdAdoConstants.SqlDeleteAllPausedTriggerGrps,
        StdAdoConstants.SqlDeleteAllPausedJobGrps,
        StdAdoConstants.SqlDeleteFiredTriggers
    ];

    /// <summary>
    /// Clear (delete!) all scheduling data - all <see cref="IJob"/>s, <see cref="ITrigger" />s
    /// <see cref="ICalendar" />s.
    /// </summary>
    /// <remarks>
    /// One delete per table, which used to be one round trip and one undisposed command each. They go
    /// out as one batch where the provider supports batching, and one disposed command at a time where
    /// it does not. The order matters and the batch preserves it: a batch executes its commands in
    /// sequence.
    /// </remarks>
    public virtual ValueTask ClearData(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        List<SqlStatement> statements = new(clearDataStatements.Length);
        foreach (string sql in clearDataStatements)
        {
            statements.Add(new SqlStatement(ReplaceTablePrefix(sql), [new SqlStatementParameter(SqlParameters.SchedulerName, schedulerName)]));
        }

        return ExecuteStatements(conn, statements, cancellationToken);
    }

    //---------------------------------------------------------------------------
    // jobs
    //---------------------------------------------------------------------------

    /// <summary>
    /// Gets the db presentation for boolean value. Subclasses can overwrite this behaviour.
    /// </summary>
    /// <param name="booleanValue">Value to map to database.</param>
    /// <returns></returns>
    public virtual object GetDbBooleanValue(bool booleanValue)
    {
        // works nicely for databases we have currently supported
        return booleanValue;
    }

    /// <summary>
    /// Gets the boolean value from db presentation. Subclasses can overwrite this behaviour.
    /// </summary>
    /// <param name="columnValue">Value to map from database.</param>
    /// <returns></returns>
    public virtual bool GetBooleanFromDbValue(object columnValue)
    {
        if (columnValue is not null && columnValue != DBNull.Value)
        {
            return Convert.ToBoolean(columnValue);
        }

        Throw.ArgumentException("Value must be non-null.");
        return false;
    }

    /// <summary>
    /// Gets the db presentation for date/time value: UTC ticks. The storage format is part of the
    /// schema contract, not an extension point — the preferred-node liveness SQL compares raw
    /// check-in values assuming it, so a delegate cannot change how instants are stored without
    /// owning its SQL outright.
    /// </summary>
    /// <param name="dateTimeValue">Value to map to database.</param>
    /// <returns></returns>
    public object? GetDbDateTimeValue(DateTimeOffset? dateTimeValue)
    {
        return dateTimeValue?.UtcTicks;
    }

    /// <summary>
    /// Gets the date/time value from db presentation. The storage format is part of the schema
    /// contract; see <see cref="GetDbDateTimeValue" />.
    /// </summary>
    /// <param name="columnValue">Value to map from database.</param>
    /// <returns></returns>
    public DateTimeOffset? GetDateTimeFromDbValue(object columnValue)
    {
        if (columnValue is not null && columnValue != DBNull.Value)
        {
            var ticks = Convert.ToInt64(columnValue, CultureInfo.CurrentCulture);
            if (ticks > 0)
            {
                return new DateTimeOffset(ticks, TimeSpan.Zero);
            }
        }
        return null;
    }

    /// <summary>
    /// Gets the db presentation for time span value: whole milliseconds. The storage format is part
    /// of the schema contract, not an extension point — the preferred-node liveness SQL multiplies
    /// stored check-in intervals assuming it, so a delegate cannot change how durations are stored
    /// without owning its SQL outright.
    /// </summary>
    /// <param name="timeSpanValue">Value to map to database.</param>
    /// <returns></returns>
    public object? GetDbTimeSpanValue(TimeSpan? timeSpanValue)
    {
        return timeSpanValue is not null ? (long?) timeSpanValue.Value.TotalMilliseconds : null;
    }

    /// <summary>
    /// Gets the time span value from db presentation. The storage format is part of the schema
    /// contract; see <see cref="GetDbTimeSpanValue" />.
    /// </summary>
    /// <param name="columnValue">Value to map from database.</param>
    /// <returns></returns>
    public TimeSpan? GetTimeSpanFromDbValue(object columnValue)
    {
        if (columnValue is not null && columnValue != DBNull.Value)
        {
            var millis = Convert.ToInt64(columnValue, CultureInfo.CurrentCulture);
            if (millis > 0)
            {
                return TimeSpan.FromMilliseconds(millis);
            }
        }

        return null;
    }

    private ValueTask<JobDataMap?> ReadMapFromReader(DbDataReader rs, int colIndex)
    {
        var isDbNullTask = rs.IsDBNullAsync(colIndex);
        if (isDbNullTask.IsCompleted && isDbNullTask.Result)
        {
            return new ValueTask<JobDataMap?>((JobDataMap?) null);
        }

        return Awaited(isDbNullTask);

        async ValueTask<JobDataMap?> Awaited(Task<bool> isDbNull)
        {
            if (await isDbNull.ConfigureAwait(false))
            {
                return null;
            }

            if (CanUseProperties)
            {
                try
                {
                    var properties = await GetMapFromProperties(rs, colIndex).ConfigureAwait(false);
                    return properties;
                }
                catch (InvalidCastException)
                {
                    // old data from user error or XML scheduling plugin data
                    try
                    {
                        return await GetObjectFromBlob<JobDataMap>(rs, colIndex).ConfigureAwait(false);
                    }
                    catch
                    {
                    }

                    // throw original exception
                    throw;
                }
            }
            try
            {
                return await GetObjectFromBlob<JobDataMap>(rs, colIndex).ConfigureAwait(false);
            }
            catch (InvalidCastException)
            {
                // old data from user error?
                try
                {
                    // we use this then
                    return await GetMapFromProperties(rs, colIndex).ConfigureAwait(false);
                }
                catch
                {
                }

                // throw original exception
                throw;
            }
        }
    }

    /// <summary>
    /// Build dictionary from serialized NameValueCollection.
    /// </summary>
    private async ValueTask<JobDataMap?> GetMapFromProperties(DbDataReader rs, int idx)
    {
        NameValueCollection? properties = await GetJobDataFromBlob<NameValueCollection>(rs, idx).ConfigureAwait(false);
        if (properties is null)
        {
            return null;
        }

        return new JobDataMap(ConvertFromProperty(properties));
    }

    /// <summary>
    /// Select all of the jobs contained in a given group.
    /// </summary>
    /// <param name="conn">The DB Connection.</param>
    /// <param name="matcher"></param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>An array of <see cref="String" /> job names.</returns>
    public virtual async ValueTask<List<JobKey>> SelectJobKeysInGroup(
        ConnectionAndTransactionHolder conn,
        GroupMatcher<JobKey> matcher,
        CancellationToken cancellationToken = default)
    {
        (string sql, string parameter) = MatchGroup(matcher, StdAdoConstants.SqlSelectJobsInGroup, StdAdoConstants.SqlSelectJobsInGroupLike);

        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(sql));
        AddCommandParameter(cmd, SqlParameters.SchedulerName, schedulerName);
        AddCommandParameter(cmd, SqlParameters.JobGroup, parameter);

        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<JobKey>();
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new JobKey(rs.GetString(0), rs.GetString(1)));
        }
        return list;
    }

    /// <summary>
    /// Picks the statement a group matcher should run and the value to bind for it: an equality
    /// matcher gets the '=' form, everything else the LIKE form over the pattern the matcher
    /// translates to.
    /// </summary>
    /// <remarks>
    /// The equality form is not merely a shortcut — LIKE would treat a group whose name contains
    /// '%' or '_' as a pattern, and it cannot use an index the way '=' can.
    /// </remarks>
    protected (string Sql, string Parameter) MatchGroup<T>(
        GroupMatcher<T> matcher,
        string equalsSql,
        string likeSql) where T : Key<T>
    {
        return IsMatcherEquals(matcher)
            ? (equalsSql, ToSqlEqualsClause(matcher))
            : (likeSql, ToSqlLikeClause(matcher));
    }

    /// <summary>
    /// Whether <paramref name="matcher" /> is an exact-match one, which becomes <c>=</c> rather than
    /// <c>LIKE</c>.
    /// </summary>
    /// <param name="matcher">The matcher being translated.</param>
    protected static bool IsMatcherEquals<T>(StringMatcher<T> matcher) where T : Key<T>
    {
        return matcher.CompareWithOperator.Equals(StringOperator.Equality);
    }

    /// <summary>
    /// The value an exact-match matcher compares with, which needs no escaping because it is bound as
    /// a parameter rather than built into the statement.
    /// </summary>
    /// <param name="matcher">The matcher being translated.</param>
    protected static string ToSqlEqualsClause<T>(StringMatcher<T> matcher) where T : Key<T>
    {
        return matcher.CompareToValue;
    }

    /// <summary>
    /// Translates a group or name matcher into a LIKE pattern.
    /// </summary>
    protected string ToSqlLikeClause<T>(StringMatcher<T> matcher) where T : Key<T>
    {
        return ToSqlLikeClause(matcher.CompareWithOperator, matcher.CompareToValue);
    }

    /// <summary>
    /// Translates a matcher's operator and text into a LIKE pattern.
    /// </summary>
    /// <remarks>
    /// The matcher's own text is a literal, so its wildcard characters are escaped with '!'; the
    /// statements this feeds all name that character in an ESCAPE clause. Only the '%' this method
    /// adds itself stays a wildcard, so a
    /// group literally named "50%" is found by an exact match and by a "starts with 50" one, and not
    /// by a "starts with 5" one.
    /// </remarks>
    /// <remarks>
    /// This takes the operator and the text rather than the matcher, because a calendar is matched
    /// by <see cref="NameMatcher" /> and a job or trigger by <see cref="StringMatcher{TKey}" />,
    /// and the translation is the same for both.
    /// </remarks>
    protected virtual string ToSqlLikeClause(StringOperator compareWith, string compareToValue)
    {
        if (StringOperator.Anything.Equals(compareWith))
        {
            return "%";
        }

        string value = EscapeSqlLikeWildcards(compareToValue);

        if (StringOperator.Equality.Equals(compareWith))
        {
            return value;
        }

        if (StringOperator.Contains.Equals(compareWith))
        {
            return "%" + value + "%";
        }

        if (StringOperator.EndsWith.Equals(compareWith))
        {
            return "%" + value;
        }

        if (StringOperator.StartsWith.Equals(compareWith))
        {
            return value + "%";
        }

        Throw.ArgumentOutOfRangeException("Don't know how to translate " + compareWith + " into SQL");
        return default;
    }

    /// <summary>
    /// Escapes the LIKE wildcards '%' and '_', and the escape character '!' itself, so that the value
    /// matches literally.
    /// </summary>
    protected static string EscapeSqlLikeWildcards(string value)
    {
        if (value.AsSpan().IndexOfAny(StdAdoConstants.SqlLikeEscapeCharacter, '%', '_') < 0)
        {
            return value;
        }

        StringBuilder builder = new(value.Length + 8);
        foreach (char c in value)
        {
            if (c is StdAdoConstants.SqlLikeEscapeCharacter or '%' or '_')
            {
                builder.Append(StdAdoConstants.SqlLikeEscapeCharacter);
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    //---------------------------------------------------------------------------
    // triggers
    //---------------------------------------------------------------------------

    //---------------------------------------------------------------------------
    // calendars
    //---------------------------------------------------------------------------

    //---------------------------------------------------------------------------
    // trigger firing
    //---------------------------------------------------------------------------

    //---------------------------------------------------------------------------
    // protected methods that can be overridden by subclasses
    //---------------------------------------------------------------------------

    /// <summary>
    /// Replace the table prefix in a query by replacing any occurrences of
    /// "{0}" with the table prefix.
    /// </summary>
    /// <param name="query">The unsubstituted query</param>
    /// <returns>The query, with proper table prefix substituted</returns>
    protected string ReplaceTablePrefix(string query)
    {
        return cachedQueries.GetOrAdd(query, static (q, prefix) => AdoJobStoreUtil.ReplaceTablePrefix(q, prefix), tablePrefix);
    }

    /// <summary>
    /// Create a serialized <see langword="byte[]"/> version of an Object.
    /// </summary>
    /// <param name="obj">the object to serialize</param>
    /// <returns>Serialized object as byte array.</returns>
    protected virtual byte[]? SerializeObject(object? obj)
    {
        byte[]? retValue = null;
        if (obj is not null)
        {
            retValue = objectSerializer.Serialize(obj);
        }
        return retValue;
    }

    /// <summary>
    /// The key of the first value in <paramref name="data" /> the serializer cannot write, or
    /// <see langword="null" /> when every value round-trips.
    /// </summary>
    /// <remarks>
    /// Used to name the offending entry in the failure, rather than reporting that a job data map
    /// somewhere could not be stored.
    /// </remarks>
    /// <param name="data">The map being stored.</param>
    protected object? GetKeyOfNonSerializableValue(JobDataMap data)
    {
        foreach (KeyValuePair<string, object?> entry in data)
        {
            try
            {
                SerializeObject(entry.Value);
            }
            catch (Exception)
            {
                return entry.Key;
            }
        }

        // As long as it is true that the Map was not serializable, we should
        // not hit this case.
        return null;
    }

    private byte[]? SerializeProperties(JobDataMap data)
    {
        byte[]? retValue = null;
        if (data.Count > 0)
        {
            NameValueCollection properties = ConvertToProperty(data);
            retValue = SerializeObject(properties);
        }

        return retValue;
    }

    /// <summary>
    /// Convert the stored properties into the entries of a <see cref="JobDataMap" />.
    /// </summary>
    protected virtual Dictionary<string, object?> ConvertFromProperty(NameValueCollection properties)
    {
        var data = new Dictionary<string, object?>(properties.Count);
        foreach (var key in properties.AllKeys)
        {
            data[key!] = properties[key];
        }

        return data;
    }

    /// <summary>
    /// Convert the JobDataMap into a list of properties.
    /// </summary>
    protected virtual NameValueCollection ConvertToProperty(IDictionary<string, object?> data)
    {
        NameValueCollection properties = new NameValueCollection();
        foreach (KeyValuePair<string, object?> entry in data)
        {
            string key = entry.Key;
            object val = entry.Value ?? string.Empty;

            if (val is not string s)
            {
                Throw.ArgumentException($"JobDataMap values must be strings when the 'useProperties' property is set.  Key of offending value: {key}");
                return default;
            }
            properties[key] = s;
        }
        return properties;
    }

    /// <summary>
    /// This method should be overridden by any delegate subclasses that need
    /// special handling for BLOBs. The default implementation uses standard
    /// ADO.NET operations.
    /// </summary>
    /// <param name="rs">The data reader, already queued to the correct row.</param>
    /// <param name="colIndex">The column index for the BLOB.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The deserialized object from the DataReader BLOB.</returns>
    protected virtual async ValueTask<T?> GetObjectFromBlob<T>(
        DbDataReader rs,
        int colIndex,
        CancellationToken cancellationToken = default) where T : class
    {
        T? obj = null;

        byte[]? data = await ReadBytesFromBlob(rs, colIndex, cancellationToken).ConfigureAwait(false);
        if (data is not null && data.Length > 0)
        {
            obj = objectSerializer.Deserialize<T>(data);
        }
        return obj;
    }

    /// <summary>
    /// Reads a BLOB column as bytes. Overridden by a delegate whose driver needs special handling for
    /// large objects.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This was the last member of the ADO.NET surface still speaking the legacy
    /// <see cref="IDataReader" />. Every reader the store hands it is a <see cref="DbDataReader" />, and
    /// the synchronous interface meant this one read blocked its thread and ignored the cancellation
    /// token it was handed.
    /// </para>
    /// <para>
    /// It also read the column twice — once with a null buffer to learn the length, once to fill it —
    /// which is a documented <see cref="IDataRecord.GetBytes" /> idiom but two trips through the
    /// provider's blob handling. <see cref="DbDataReader.GetFieldValueAsync{T}(int, CancellationToken)" />
    /// asks for the whole value once and lets the provider size it.
    /// </para>
    /// </remarks>
    /// <param name="rs">The data reader, already queued to the correct row.</param>
    /// <param name="colIndex">The column index for the BLOB.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    protected virtual async ValueTask<byte[]?> ReadBytesFromBlob(
        DbDataReader rs,
        int colIndex,
        CancellationToken cancellationToken = default)
    {
        if (await rs.IsDBNullAsync(colIndex, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return await rs.GetFieldValueAsync<byte[]>(colIndex, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual DbCommand PrepareCommand(ConnectionAndTransactionHolder cth, string commandText)
    {
        return adoUtil.PrepareCommand(cth, commandText);
    }

    /// <inheritdoc />
    public virtual void AddCommandParameter(
        DbCommand cmd,
        string paramName,
        object? paramValue,
        Enum? dataType = null,
        int? size = null)
    {
        adoUtil.AddCommandParameter(cmd, paramName, paramValue, dataType, size);
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> ValidateSchema(ConnectionAndTransactionHolder conn, CancellationToken cancellationToken = default)
    {
        foreach (var tableName in AdoConstants.AllTableNames)
        {
            var targetTable = $"{tablePrefix}{tableName}";
            var sql = $"SELECT 1 FROM {targetTable}";

            try
            {
                using var cmd = PrepareCommand(conn, sql);
                await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new JobPersistenceException($"Unable to query against table {targetTable}: " + ex.Message, ex);
            }
        }

        return AdoConstants.AllTableNames.Length;
    }

    /// <summary>
    /// The embedded script that creates this dialect's schema, or <see langword="null" /> when this
    /// delegate has none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Null on <see cref="StdAdoDelegate" /> itself, because there is no dialect-neutral DDL: the
    /// six shipped delegates each name their own script, and a delegate written outside Quartz names
    /// one or leaves provisioning unavailable. Overriding it is the whole of what a delegate has to
    /// do to support <see cref="SchemaProvisioning.CreateIfMissing" />.
    /// </para>
    /// <para>
    /// The name is a manifest resource name, looked for in the assembly of the delegate's own type and
    /// then in each assembly up its base chain. So a delegate of somebody else's can embed a script of
    /// its own beside itself, and one that subclasses a shipped dialect delegate to change a statement
    /// or two inherits that dialect's script rather than having to carry a copy.
    /// </para>
    /// </remarks>
    protected virtual string? SchemaResourceName => null;

    /// <summary>
    /// The line the schema scripts separate their statements with.
    /// </summary>
    /// <remarks>
    /// A sentinel rather than a terminator, because there is no terminator that works: a semicolon
    /// ends a statement on one dialect and a line inside a PL/SQL block on another, and finding out
    /// which would mean lexing SQL here. The generator writes the sentinel, so splitting on it is
    /// exact.
    /// </remarks>
    private const string SchemaStatementSeparator = "--;;";

    /// <inheritdoc />
    public virtual async ValueTask CreateSchema(ConnectionAndTransactionHolder conn, CancellationToken cancellationToken = default)
    {
        string script = ReadSchemaScript();

        foreach (string statement in SplitSchemaStatements(script))
        {
            try
            {
                DbCommand cmd = PrepareCommand(conn, statement);
                await using (cmd.ConfigureAwait(false))
                {
                    await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                throw new JobPersistenceException(
                    $"Unable to create the schema with table prefix '{tablePrefix}': {ex.Message}"
                    + Environment.NewLine + statement, ex);
            }
        }
    }

    /// <summary>
    /// Reads this delegate's schema script and substitutes the configured table prefix into it.
    /// </summary>
    private string ReadSchemaScript()
    {
        if (SchemaResourceName is not { } resourceName)
        {
            throw new JobPersistenceException(
                $"{GetType().Name} ships no schema script, so it cannot create a schema. Create it by hand "
                + "from the script for your database under database/tables/, and set "
                + "SchemaProvisioning.Validate (the default). A driver delegate of your own supports "
                + "provisioning by overriding StdAdoDelegate.SchemaResourceName with the name of an embedded "
                + "script.");
        }

        // The resource lives beside the delegate that named it, which is this assembly for a shipped
        // dialect and somebody else's for a delegate written outside Quartz — and for the commonest
        // shape of all, a delegate subclassing a shipped dialect to change a statement or two, it is
        // this assembly rather than that delegate's own. Walking up the hierarchy finds it either way,
        // nearest first, so a subclass that embeds a script of its own still wins over the base's.
        for (Type? declaring = GetType(); declaring is not null; declaring = declaring.BaseType)
        {
            using Stream? stream = declaring.Assembly.GetManifestResourceStream(resourceName);
            if (stream is not null)
            {
                using StreamReader reader = new(stream, Encoding.UTF8);
                return AdoJobStoreUtil.ReplaceTablePrefix(reader.ReadToEnd(), tablePrefix);
            }
        }

        throw new JobPersistenceException(
            $"{GetType().Name} names the schema script '{resourceName}', which is embedded neither in "
            + $"{GetType().Assembly.GetName().Name} nor in the assembly of any delegate it derives from.");
    }

    /// <summary>
    /// Splits a schema script into the statements the provider is given one at a time, dropping the
    /// chunks that are nothing but comments — the file's header, and nothing else.
    /// </summary>
    private static List<string> SplitSchemaStatements(string script)
    {
        return script
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split($"\n{SchemaStatementSeparator}\n")
            .Where(HasSql)
            .Select(chunk => chunk.Trim())
            .ToList();
    }

    /// <summary>Whether a chunk holds anything but blank lines and whole-line comments.</summary>
    private static bool HasSql(string chunk)
    {
        foreach (string line in chunk.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length > 0 && !trimmed.StartsWith("--", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
