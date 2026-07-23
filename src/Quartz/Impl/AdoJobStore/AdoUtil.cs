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
using Quartz.Impl.AdoJobStore.Common;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Quartz.Impl.AdoJobStore;

public interface IAdoUtil
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

    /// <summary>
    /// Rounds a key count up to the next predicate bucket. Callers must then supply exactly this many
    /// key parameter pairs, repeating the last key as padding.
    /// </summary>
    internal static int RoundUpTriggerKeyCount(int count)
    {
        foreach (var bucket in triggerKeyPredicateBuckets)
        {
            if (count <= bucket)
            {
                return bucket;
            }
        }

        return MaxTriggerKeysPerPredicate;
    }

    /// <summary>
    /// Builds a parameterized <c>(TRIGGER_NAME = @tkn000 AND TRIGGER_GROUP = @tkg000) OR (...)</c>
    /// predicate for <paramref name="keyCount" /> trigger keys.
    /// </summary>
    /// <remarks>
    /// Deliberately not a row-value <c>IN ((a, b), ...)</c>, which SQL Server does not support, and
    /// deliberately not interpolated literals. Parameter names are fixed width so that no name is a
    /// prefix of another — see the remarks on the parameter name rewriting above.
    /// </remarks>
    internal static string BuildTriggerKeyPredicate(int keyCount)
    {
        var bucketIndex = Array.IndexOf(triggerKeyPredicateBuckets, keyCount);
        if (bucketIndex < 0)
        {
            Throw.ArgumentOutOfRangeException(nameof(keyCount), "Key count must be rounded via RoundUpTriggerKeyCount first");
        }

        var cached = triggerKeyPredicateCache[bucketIndex];
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

            sb.Append('(').Append(AdoConstants.ColumnTriggerName).Append(" = @").Append(TriggerKeyNameParameter(i))
                .Append(" AND ").Append(AdoConstants.ColumnTriggerGroup).Append(" = @").Append(TriggerKeyGroupParameter(i))
                .Append(')');
        }

        sb.Append(')');

        var predicate = sb.ToString();
        triggerKeyPredicateCache[bucketIndex] = predicate;
        return predicate;
    }

    internal static string TriggerKeyNameParameter(int index) => "tkn" + index.ToString("000", CultureInfo.InvariantCulture);

    internal static string TriggerKeyGroupParameter(int index) => "tkg" + index.ToString("000", CultureInfo.InvariantCulture);

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