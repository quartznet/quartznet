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
using System.Collections.Specialized;
using System.Data;
using System.Data.Common;
using System.Globalization;

using Microsoft.Extensions.Logging;

using Quartz.Impl.AdoJobStore.Common;
using Quartz.Impl.Matchers;
using Quartz.Diagnostics;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// This is meant to be an abstract base class for most, if not all, <see cref="IDriverDelegate" />
/// implementations. Subclasses should override only those methods that need
/// special handling for the DBMS driver in question.
/// </summary>
public partial class StdAdoDelegate : StdAdoConstants, IDriverDelegate, IDbAccessor
{
    private const string FileScanListenerName = "FILE_SCAN_LISTENER_NAME";
    private const string DirectoryScanListenerName = "DIRECTORY_SCAN_LISTENER_NAME";

    private ILogger<StdAdoDelegate> logger = null!;
    private string tablePrefix = DefaultTablePrefix;
    private string instanceId = null!;
    private string schedName = null!;
    private bool useProperties;

    private ITypeLoadHelper typeLoadHelper = null!;
    private AdoUtil adoUtil = null!;

    private readonly List<ITriggerPersistenceDelegate> triggerPersistenceDelegates = new();

    private IObjectSerializer objectSerializer = null!;
    private TimeProvider timeProvider = null!;

    private readonly ConcurrentDictionary<string, string> cachedQueries = new();

    protected IDbProvider DbProvider { get; private set; } = null!;

    /// <summary>
    /// Initializes the driver delegate.
    /// </summary>
    public virtual void Initialize(DelegateInitializationArgs args)
    {
        logger = LogProvider.CreateLogger<StdAdoDelegate>();
        tablePrefix = args.TablePrefix;
        schedName = args.InstanceName;
        instanceId = args.InstanceId;
        DbProvider = args.DbProvider;
        typeLoadHelper = args.TypeLoadHelper;
        useProperties = args.UseProperties;
        adoUtil = new AdoUtil(args.DbProvider);
        objectSerializer = args.ObjectSerializer!;
        timeProvider = args.TimeProvider;

        AddDefaultTriggerPersistenceDelegates();

        if (!string.IsNullOrEmpty(args.InitString) && args.InitString is not null)
        {
            string[] settings = args.InitString.Split('\\', '|');

            foreach (string setting in settings)
            {
                var index = setting.IndexOf('=');
                if (index == -1 || index == setting.Length - 1)
                {
                    continue;
                }

                string name = setting.Substring(0, index).Trim();
                string value = setting.Substring(index + 1).Trim();

                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                // we support old *Classes and new *Types, latter has better support for assembly qualified names
                if (name is "triggerPersistenceDelegateClasses" or "triggerPersistenceDelegateTypes")
                {
                    var separator = ',';
                    if (value.Contains(';') || name == "triggerPersistenceDelegateTypes")
                    {
                        // use separator that allows assembly qualified names
                        separator = ';';
                    }

                    string[] trigDelegates = value.Split(separator);

                    foreach (string triggerTypeName in trigDelegates)
                    {
                        var typeName = triggerTypeName.Trim();

                        if (string.IsNullOrEmpty(typeName))
                        {
                            continue;
                        }

                        try
                        {
                            Type trigDelClass = typeLoadHelper.LoadType(typeName)!;
                            AddTriggerPersistenceDelegate((ITriggerPersistenceDelegate) Activator.CreateInstance(trigDelClass)!);
                        }
                        catch (Exception e)
                        {
                            ThrowHelper.ThrowNoSuchDelegateException("Error instantiating TriggerPersistenceDelegate of type: " + triggerTypeName, e);
                        }
                    }
                }
                else
                {
                    ThrowHelper.ThrowNoSuchDelegateException("Unknown setting: '" + name + "'");
                }
            }
        }
    }

    protected virtual void AddDefaultTriggerPersistenceDelegates()
    {
        AddTriggerPersistenceDelegate(new SimpleTriggerPersistenceDelegate());
        AddTriggerPersistenceDelegate(new CronTriggerPersistenceDelegate());
        AddTriggerPersistenceDelegate(new CalendarIntervalTriggerPersistenceDelegate());
        AddTriggerPersistenceDelegate(new DailyTimeIntervalTriggerPersistenceDelegate());
    }

    protected virtual bool CanUseProperties => useProperties;

    //---------------------------------------------------------------------------
    // startup / recovery
    //---------------------------------------------------------------------------

    /// <summary>
    /// Clear (delete!) all scheduling data - all <see cref="IJob"/>s, <see cref="ITrigger" />s
    /// <see cref="ICalendar" />s.
    /// </summary>
    /// <remarks>
    /// </remarks>
    public virtual async ValueTask ClearData(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        DbCommand ps = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteAllSimpleTriggers));
        AddCommandParameter(ps, "schedulerName", schedName);
        await ps.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        ps = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteAllSimpropTriggers));
        AddCommandParameter(ps, "schedulerName", schedName);
        await ps.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        ps = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteAllCronTriggers));
        AddCommandParameter(ps, "schedulerName", schedName);
        await ps.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        ps = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteAllBlobTriggers));
        AddCommandParameter(ps, "schedulerName", schedName);
        await ps.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        ps = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteAllTriggers));
        AddCommandParameter(ps, "schedulerName", schedName);
        await ps.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        ps = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteAllJobDetails));
        AddCommandParameter(ps, "schedulerName", schedName);
        await ps.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        ps = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteAllCalendars));
        AddCommandParameter(ps, "schedulerName", schedName);
        await ps.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        ps = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteAllPausedTriggerGrps));
        AddCommandParameter(ps, "schedulerName", schedName);
        await ps.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        ps = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteFiredTriggers));
        AddCommandParameter(ps, "schedulerName", schedName);
        await ps.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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

        ThrowHelper.ThrowArgumentException("Value must be non-null.");
        return false;
    }

    /// <summary>
    /// Gets the db presentation for date/time value. Subclasses can overwrite this behaviour.
    /// </summary>
    /// <param name="dateTimeValue">Value to map to database.</param>
    /// <returns></returns>
    public virtual object? GetDbDateTimeValue(DateTimeOffset? dateTimeValue)
    {
        return dateTimeValue?.UtcTicks;
    }

    /// <summary>
    /// Gets the date/time value from db presentation. Subclasses can overwrite this behaviour.
    /// </summary>
    /// <param name="columnValue">Value to map from database.</param>
    /// <returns></returns>
    public virtual DateTimeOffset? GetDateTimeFromDbValue(object columnValue)
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
    /// Gets the db presentation for time span value. Subclasses can overwrite this behaviour.
    /// </summary>
    /// <param name="timeSpanValue">Value to map to database.</param>
    /// <returns></returns>
    public virtual object? GetDbTimeSpanValue(TimeSpan? timeSpanValue)
    {
        return timeSpanValue is not null ? (long?) timeSpanValue.Value.TotalMilliseconds : null;
    }

    /// <summary>
    /// Gets the time span value from db presentation. Subclasses can overwrite this behaviour.
    /// </summary>
    /// <param name="columnValue">Value to map from database.</param>
    /// <returns></returns>
    public virtual TimeSpan? GetTimeSpanFromDbValue(object columnValue)
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

    private ValueTask<IDictionary?> ReadMapFromReader(DbDataReader rs, int colIndex)
    {
        var isDbNullTask = rs.IsDBNullAsync(colIndex);
        if (isDbNullTask.IsCompleted && isDbNullTask.Result)
        {
            return new ValueTask<IDictionary?>((IDictionary?) null);
        }

        return Awaited(isDbNullTask);

        async ValueTask<IDictionary?> Awaited(Task<bool> isDbNull)
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
                        return await GetObjectFromBlob<IDictionary>(rs, colIndex).ConfigureAwait(false);
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
                return await GetObjectFromBlob<IDictionary>(rs, colIndex).ConfigureAwait(false);
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
    private async ValueTask<IDictionary?> GetMapFromProperties(DbDataReader rs, int idx)
    {
        NameValueCollection? properties = await GetJobDataFromBlob<NameValueCollection>(rs, idx).ConfigureAwait(false);
        if (properties is null)
        {
            return null;
        }
        IDictionary map = ConvertFromProperty(properties);
        return map;
    }

    /// <summary>
    /// Select all of the jobs contained in a given group.
    /// </summary>
    /// <param name="conn">The DB Connection.</param>
    /// <param name="matcher"></param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>An array of <see cref="String" /> job names.</returns>
    public virtual async ValueTask<List<JobKey>> SelectJobsInGroup(
        ConnectionAndTransactionHolder conn,
        GroupMatcher<JobKey> matcher,
        CancellationToken cancellationToken = default)
    {
        string sql;
        string parameter;
        if (IsMatcherEquals(matcher))
        {
            sql = ReplaceTablePrefix(SqlSelectJobsInGroup);
            parameter = StdAdoDelegate.ToSqlEqualsClause(matcher);
        }
        else
        {
            sql = ReplaceTablePrefix(SqlSelectJobsInGroupLike);
            parameter = ToSqlLikeClause(matcher);
        }

        using var cmd = PrepareCommand(conn, sql);
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "jobGroup", parameter);

        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<JobKey>();
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new JobKey(rs.GetString(0), rs.GetString(1)));
        }
        return list;
    }

    protected static bool IsMatcherEquals<T>(GroupMatcher<T> matcher) where T : Key<T>
    {
        return matcher.CompareWithOperator.Equals(StringOperator.Equality);
    }

    protected static string ToSqlEqualsClause<T>(GroupMatcher<T> matcher) where T : Key<T>
    {
        return matcher.CompareToValue;
    }

    protected virtual string ToSqlLikeClause<T>(GroupMatcher<T> matcher) where T : Key<T>
    {
        string groupName;
        if (StringOperator.Equality.Equals(matcher.CompareWithOperator))
        {
            groupName = matcher.CompareToValue;
        }
        else if (StringOperator.Contains.Equals(matcher.CompareWithOperator))
        {
            groupName = "%" + matcher.CompareToValue + "%";
        }
        else if (StringOperator.EndsWith.Equals(matcher.CompareWithOperator))
        {
            groupName = "%" + matcher.CompareToValue;
        }
        else if (StringOperator.StartsWith.Equals(matcher.CompareWithOperator))
        {
            groupName = matcher.CompareToValue + "%";
        }
        else if (StringOperator.Anything.Equals(matcher.CompareWithOperator))
        {
            groupName = "%";
        }
        else
        {
            ThrowHelper.ThrowArgumentOutOfRangeException("Don't know how to translate " + matcher.CompareWithOperator + " into SQL");
            return default;
        }
        return groupName;
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
        return cachedQueries.GetOrAdd(query, q => AdoJobStoreUtil.ReplaceTablePrefix(q, tablePrefix));
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
            NameValueCollection properties = ConvertToProperty(data.WrappedMap);
            retValue = SerializeObject(properties);
        }

        return retValue;
    }

    /// <summary>
    /// Convert the JobDataMap into a list of properties.
    /// </summary>
    protected virtual IDictionary ConvertFromProperty(NameValueCollection properties)
    {
        var data = new Dictionary<string, string?>();
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
                ThrowHelper.ThrowArgumentException($"JobDataMap values must be strings when the 'useProperties' property is set.  Key of offending value: {key}");
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
            obj = objectSerializer.DeSerialize<T>(data);
        }
        return obj;
    }

    protected virtual ValueTask<byte[]?> ReadBytesFromBlob(
        IDataReader dr,
        int colIndex,
        CancellationToken cancellationToken)
    {
        if (dr.IsDBNull(colIndex))
        {
            return new ValueTask<byte[]?>();
        }

        // If you pass a buffer that is null, GetBytes returns the length of the entire field in bytes, not the remaining size based on the buffer offset parameter.
        var length = dr.GetBytes(colIndex, 0, null!, 0, int.MaxValue);
        byte[] outbyte = new byte[length];
        dr.GetBytes(colIndex, 0, outbyte, 0, outbyte.Length);
        return new ValueTask<byte[]?>(outbyte);
    }

    public virtual DbCommand PrepareCommand(ConnectionAndTransactionHolder cth, string commandText)
    {
        return adoUtil.PrepareCommand(cth, commandText);
    }

    public virtual void AddCommandParameter(
        DbCommand cmd,
        string paramName,
        object? paramValue,
        Enum? dataType = null,
        int? size = null)
    {
        adoUtil.AddCommandParameter(cmd, paramName, paramValue, dataType, size);
    }

    /// <summary>
    /// Validates the persistence schema and returns the number of validates objects.
    /// </summary>
    public virtual async ValueTask<int> ValidateSchema(ConnectionAndTransactionHolder conn, CancellationToken cancellationToken)
    {
        foreach (var tableName in AllTableNames)
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

        return AllTableNames.Length;
    }
}