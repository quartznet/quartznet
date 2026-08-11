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

using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Quartz.Diagnostics;
using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore.Common;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Command preparation and parameter binding for the ADO job store.
/// </summary>
/// <remarks>
/// Internal: how a command gets its parameters — including the driver-specific parameter prefix
/// rewriting in <see cref="AdoUtil" /> — is not a contract anyone outside this assembly implements.
/// </remarks>
internal interface IAdoUtil
{
    void AddCommandParameter(IDbCommand cmd, string paramName, object? paramValue);

    void AddCommandParameter(
        IDbCommand cmd,
        string paramName,
        object? paramValue,
        Enum? dataType,
        int? size);

    DbCommand PrepareCommand(ConnectionAndTransactionHolder cth, string commandText);
}

/// <summary>
/// Common helper methods for working with ADO.NET.
/// </summary>
/// <author>Marko Lahma</author>
internal sealed class AdoUtil : IAdoUtil
{
    private readonly ILogger logger;
    private readonly IDbProvider dbProvider;

    public AdoUtil(IDbProvider dbProvider)
    {
        this.logger = LogProvider.CreateLogger<AdoUtil>();
        this.dbProvider = dbProvider;
    }

    public void AddCommandParameter(IDbCommand cmd, string paramName, object? paramValue)
    {
        AddCommandParameter(cmd, paramName, paramValue, null, null);
    }

    public void AddCommandParameter(
        IDbCommand cmd,
        string paramName,
        object? paramValue,
        Enum? dataType,
        int? size)
    {
        IDbDataParameter param = cmd.CreateParameter();
        ConfigureParameter(param, paramName, paramValue, dataType, size);
        cmd.Parameters.Add(param);
        cmd.CommandText = RewriteParameterName(cmd.CommandText, paramName);
    }

    /// <summary>
    /// Adds a parameter to a <see cref="DbBatchCommand" />. <see cref="DbBatchCommand" /> is not an
    /// <see cref="IDbCommand" />, so it needs its own entry point, but it shares all of the parameter
    /// naming and rewriting rules with the single-command path above.
    /// </summary>
    /// <param name="cmd">The batch command to add the parameter to.</param>
    /// <param name="parameterFactory">
    /// Command used to mint provider parameter instances when the provider has not implemented
    /// <see cref="DbBatchCommand.CreateParameter" /> (it throws by default, and several providers still
    /// do). Parameter objects are not bound to the command that created them, so one throwaway command
    /// can serve a whole batch.
    /// </param>
    /// <param name="paramName">Name of the parameter, without the provider's prefix.</param>
    /// <param name="paramValue">Value to bind, <see langword="null" /> binding as <see cref="DBNull" />.</param>
    /// <param name="dataType">Optional provider-specific parameter type.</param>
    /// <param name="size">Optional parameter size.</param>
    public void AddCommandParameter(
        DbBatchCommand cmd,
        DbCommand parameterFactory,
        string paramName,
        object? paramValue,
        Enum? dataType = null,
        int? size = null)
    {
        DbParameter param = cmd.CanCreateParameter ? cmd.CreateParameter() : parameterFactory.CreateParameter();
        ConfigureParameter(param, paramName, paramValue, dataType, size);
        cmd.Parameters.Add(param);
        cmd.CommandText = RewriteParameterName(cmd.CommandText, paramName);
    }

    private void ConfigureParameter(
        IDbDataParameter param,
        string paramName,
        object? paramValue,
        Enum? dataType,
        int? size)
    {
        if (dataType is not null)
        {
            SetDataTypeToCommandParameter(param, dataType);
        }

        if (size is not null)
        {
            param.Size = size.Value;
        }

        param.ParameterName = dbProvider.Metadata.GetParameterName(paramName);
        param.Value = paramValue ?? DBNull.Value;
    }

    /// <summary>
    /// Rewrites the <c>@name</c> placeholder in the statement text for providers that do not use the
    /// <c>@</c> prefix, or that bind positionally.
    /// </summary>
    /// <remarks>
    /// This is a plain substring replace, so a parameter name that is a prefix of another one in the same
    /// statement would corrupt it (<c>@p1</c> matching inside <c>@p10</c>). Generated parameter names must
    /// therefore be fixed width — see <see cref="BuildTriggerKeyPredicate" />.
    /// </remarks>
    private string RewriteParameterName(string commandText, string paramName)
    {
        if (!dbProvider.Metadata.BindByName)
        {
            return commandText.Replace("@" + paramName, dbProvider.Metadata.ParameterNamePrefix);
        }

        if (dbProvider.Metadata.ParameterNamePrefix != "@")
        {
            // we need to replace
            return commandText.Replace("@" + paramName, dbProvider.Metadata.ParameterNamePrefix + paramName);
        }

        return commandText;
    }

    private void SetDataTypeToCommandParameter(IDbDataParameter param, object parameterType)
    {
        dbProvider.Metadata.ParameterDbTypeProperty!.SetMethod!.Invoke(param, [parameterType]);
    }

    /// <summary>
    /// Largest number of trigger keys put into a single key-set predicate. Bounds both the provider
    /// parameter ceiling (SQL Server allows 2100; 200 keys is 401 parameters) and the size of the
    /// statement text. Callers chunk larger key sets.
    /// </summary>
    internal const int MaxTriggerKeysPerPredicate = 200;

    /// <summary>
    /// Key counts a predicate is built for. Rounding up to one of these and padding the key list with a
    /// repeat of its last key keeps the number of distinct statement texts down to the length of this
    /// array, so the database plan cache sees a handful of statements instead of one per batch size.
    /// Repeating a key is safe because the predicate is a disjunction — a duplicate term cannot change
    /// which rows match.
    /// </summary>
    private static readonly int[] triggerKeyPredicateBuckets = [1, 2, 4, 8, 16, 32, 64, 128, MaxTriggerKeysPerPredicate];

    // Unsynchronized on purpose: two threads racing here just build the same string twice and one
    // reference assignment wins, which costs nothing and cannot produce a wrong value.
    private static readonly string?[] triggerKeyPredicateCache = new string?[triggerKeyPredicateBuckets.Length];

    // The same predicates with the key columns qualified by the "t." alias, for the statements that
    // join the type tables onto TRIGGERS and would otherwise have ambiguous key columns.
    private static readonly string?[] qualifiedTriggerKeyPredicateCache = new string?[triggerKeyPredicateBuckets.Length];

    /// <summary>
    /// Largest number of job keys put into a single key-set predicate. Same reasoning as
    /// <see cref="MaxTriggerKeysPerPredicate" />; 250 keys is 501 parameters. Callers chunk larger key sets.
    /// </summary>
    internal const int MaxJobKeysPerPredicate = 250;

    private static readonly int[] jobKeyPredicateBuckets = [1, 2, 4, 8, 16, 32, 64, 128, MaxJobKeysPerPredicate];

    private static readonly string?[] jobKeyPredicateCache = new string?[jobKeyPredicateBuckets.Length];

    /// <summary>
    /// Rounds a key count up to the next predicate bucket. Callers must then supply exactly this many
    /// key parameter pairs, repeating the last key as padding.
    /// </summary>
    internal static int RoundUpTriggerKeyCount(int count) => RoundUp(count, triggerKeyPredicateBuckets);

    /// <summary>
    /// Rounds a key count up to the next predicate bucket. Callers must then supply exactly this many
    /// key parameter pairs, repeating the last key as padding.
    /// </summary>
    internal static int RoundUpJobKeyCount(int count) => RoundUp(count, jobKeyPredicateBuckets);

    private static int RoundUp(int count, int[] buckets)
    {
        foreach (var bucket in buckets)
        {
            if (count <= bucket)
            {
                return bucket;
            }
        }

        return buckets[buckets.Length - 1];
    }

    /// <summary>
    /// Builds a parameterized <c>(TRIGGER_NAME = @tkn000 AND TRIGGER_GROUP = @tkg000) OR (...)</c>
    /// predicate for <paramref name="keyCount" /> trigger keys.
    /// </summary>
    /// <param name="keyCount">Key count, already rounded via <see cref="RoundUpTriggerKeyCount" />.</param>
    /// <param name="qualified">
    /// Whether to qualify the key columns with the <c>t.</c> alias, which the statements joining the type
    /// tables onto TRIGGERS need to disambiguate them.
    /// </param>
    /// <remarks>
    /// Deliberately not a row-value <c>IN ((a, b), ...)</c>, which SQL Server does not support, and
    /// deliberately not interpolated literals. Parameter names are fixed width so that no name is a
    /// prefix of another — see the remarks on the parameter name rewriting above.
    /// </remarks>
    internal static string BuildTriggerKeyPredicate(int keyCount, bool qualified = false)
    {
        return BuildKeyPredicate(
            keyCount,
            triggerKeyPredicateBuckets,
            qualified ? qualifiedTriggerKeyPredicateCache : triggerKeyPredicateCache,
            qualified ? "t." : "",
            AdoConstants.ColumnTriggerName,
            AdoConstants.ColumnTriggerGroup,
            TriggerKeyNameParameter,
            TriggerKeyGroupParameter);
    }

    /// <summary>
    /// Builds a parameterized <c>(JOB_NAME = @jkn000 AND JOB_GROUP = @jkg000) OR (...)</c> predicate for
    /// <paramref name="keyCount" /> job keys.
    /// </summary>
    /// <param name="keyCount">Key count, already rounded via <see cref="RoundUpJobKeyCount" />.</param>
    internal static string BuildJobKeyPredicate(int keyCount)
    {
        return BuildKeyPredicate(
            keyCount,
            jobKeyPredicateBuckets,
            jobKeyPredicateCache,
            "",
            AdoConstants.ColumnJobName,
            AdoConstants.ColumnJobGroup,
            JobKeyNameParameter,
            JobKeyGroupParameter);
    }

    private static string BuildKeyPredicate(
        int keyCount,
        int[] buckets,
        string?[] cache,
        string columnPrefix,
        string nameColumn,
        string groupColumn,
        Func<int, string> nameParameter,
        Func<int, string> groupParameter)
    {
        var bucketIndex = Array.IndexOf(buckets, keyCount);
        if (bucketIndex < 0)
        {
            Throw.ArgumentOutOfRangeException(nameof(keyCount), "Key count must be rounded to a predicate bucket first");
        }

        var cached = cache[bucketIndex];
        if (cached is not null)
        {
            return cached;
        }

        var sb = new StringBuilder("(");
        for (var i = 0; i < keyCount; i++)
        {
            if (i > 0)
            {
                sb.Append(" OR ");
            }

            sb.Append('(').Append(columnPrefix).Append(nameColumn).Append(" = @").Append(nameParameter(i))
                .Append(" AND ").Append(columnPrefix).Append(groupColumn).Append(" = @").Append(groupParameter(i))
                .Append(')');
        }

        sb.Append(')');

        var predicate = sb.ToString();
        cache[bucketIndex] = predicate;
        return predicate;
    }

    // One entry per possible state count. A state set is deduplicated before it gets here, so it can
    // never be longer than the number of states the enum defines, and the plan cache sees at most that
    // many distinct texts per statement.
    private static readonly string?[] triggerStatePredicateCache = new string?[Enum.GetValues<StoredTriggerState>().Length + 1];

    /// <summary>
    /// Builds a parameterized <c>(TRIGGER_STATE = @oldState00 OR TRIGGER_STATE = @oldState01)</c>
    /// predicate for <paramref name="stateCount" /> old states.
    /// </summary>
    /// <param name="stateCount">
    /// Number of states, which must be at least one and at most the number of states the enum defines —
    /// the caller deduplicates first.
    /// </param>
    /// <remarks>
    /// Parameter names are fixed width so that no name is a prefix of another — see the remarks on the
    /// parameter name rewriting above.
    /// </remarks>
    internal static string BuildTriggerStatePredicate(int stateCount)
    {
        if (stateCount < 1 || stateCount >= triggerStatePredicateCache.Length)
        {
            Throw.ArgumentOutOfRangeException(nameof(stateCount), "A state predicate needs between one and " + (triggerStatePredicateCache.Length - 1) + " distinct states");
        }

        string? cached = triggerStatePredicateCache[stateCount];
        if (cached is not null)
        {
            return cached;
        }

        StringBuilder sb = new("(");
        for (int i = 0; i < stateCount; i++)
        {
            if (i > 0)
            {
                sb.Append(" OR ");
            }

            sb.Append(AdoConstants.ColumnTriggerState).Append(" = @").Append(TriggerStateParameter(i));
        }

        sb.Append(')');

        string predicate = sb.ToString();
        triggerStatePredicateCache[stateCount] = predicate;
        return predicate;
    }

    internal static string TriggerStateParameter(int index) => "oldState" + index.ToString("00", CultureInfo.InvariantCulture);

    internal static string TriggerKeyNameParameter(int index) => "tkn" + index.ToString("000", CultureInfo.InvariantCulture);

    internal static string TriggerKeyGroupParameter(int index) => "tkg" + index.ToString("000", CultureInfo.InvariantCulture);

    internal static string JobKeyNameParameter(int index) => "jkn" + index.ToString("000", CultureInfo.InvariantCulture);

    internal static string JobKeyGroupParameter(int index) => "jkg" + index.ToString("000", CultureInfo.InvariantCulture);

    /// <summary>
    /// Loads the extended properties of a set of triggers that all live in one type table, chunking the
    /// keys into key-set predicates. Shared by the trigger persistence delegates whose table holds
    /// exactly one trigger type, so each of them is a one-line override.
    /// </summary>
    /// <param name="dbAccessor">Accessor used to prepare the command and bind its parameters.</param>
    /// <param name="conn">The DB connection.</param>
    /// <param name="sqlPrefix">Statement up to and including its trailing <c>AND </c>.</param>
    /// <param name="tablePrefix">The table prefix to substitute into the statement.</param>
    /// <param name="schedulerName">The scheduler the rows belong to.</param>
    /// <param name="triggerKeys">The keys to load. Keys with no row are simply absent from the result.</param>
    /// <param name="readBundle">Reads one row of the type table into a property bundle.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    internal static async ValueTask<Dictionary<TriggerKey, TriggerPropertyBundle>> LoadTriggerPropertyBundles(
        IDbAccessor dbAccessor,
        ConnectionAndTransactionHolder conn,
        string sqlPrefix,
        string tablePrefix,
        string schedulerName,
        IReadOnlyCollection<TriggerKey> triggerKeys,
        Func<DbDataReader, TriggerPropertyBundle> readBundle,
        CancellationToken cancellationToken)
    {
        var bundles = new Dictionary<TriggerKey, TriggerPropertyBundle>(triggerKeys.Count);
        if (triggerKeys.Count == 0)
        {
            return bundles;
        }

        List<TriggerKey> keys = triggerKeys as List<TriggerKey> ?? [.. triggerKeys];

        for (var offset = 0; offset < keys.Count; offset += MaxTriggerKeysPerPredicate)
        {
            var length = Math.Min(MaxTriggerKeysPerPredicate, keys.Count - offset);
            var paddedCount = RoundUpTriggerKeyCount(length);

            using var cmd = dbAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(sqlPrefix + BuildTriggerKeyPredicate(paddedCount), tablePrefix));
            dbAccessor.AddCommandParameter(cmd, "schedulerName", schedulerName);

            for (var i = 0; i < paddedCount; i++)
            {
                // Pad up to the bucket size by repeating the chunk's last key. The predicate is a
                // disjunction, so a repeated term cannot change which rows match.
                var key = keys[offset + Math.Min(i, length - 1)];
                dbAccessor.AddCommandParameter(cmd, TriggerKeyNameParameter(i), key.Name);
                dbAccessor.AddCommandParameter(cmd, TriggerKeyGroupParameter(i), key.Group);
            }

            using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var key = new TriggerKey(
                    (string) rs[AdoConstants.ColumnTriggerName],
                    (string) rs[AdoConstants.ColumnTriggerGroup]);
                bundles[key] = readBundle(rs);
            }
        }

        return bundles;
    }

    public DbCommand PrepareCommand(ConnectionAndTransactionHolder cth, string commandText)
    {
        DbCommand cmd = dbProvider.CreateCommand();
        cmd.CommandText = commandText;
        cth.Attach(cmd);

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Prepared SQL: {Sql}", cmd.CommandText);
        }

        return cmd;
    }
}