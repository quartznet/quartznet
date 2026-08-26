using System.Collections;
using System.Collections.Frozen;
using System.Data.Common;
using System.Globalization;

using Microsoft.Extensions.Logging;

using Quartz.Impl.Triggers;
using Quartz.Extensibility;
using Quartz.Util;

namespace Quartz.Impl.AdoJobStore;

public partial class StdAdoDelegate
{
    /// <summary>
    /// Reduces an old-state set to the distinct states it names, which is what the generated predicate
    /// is built and bound for. A disjunction cannot tell a repeated term apart from a single one, so
    /// folding duplicates away only keeps the number of distinct statement texts down.
    /// </summary>
    /// <exception cref="ArgumentException">The set is empty, which would match no trigger at all.</exception>
    private static List<StoredTriggerState> DistinctStates(IReadOnlyCollection<StoredTriggerState> oldStates, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(oldStates, parameterName);

        List<StoredTriggerState> distinct = new(oldStates.Count);
        foreach (StoredTriggerState state in oldStates)
        {
            if (!distinct.Contains(state))
            {
                distinct.Add(state);
            }
        }

        if (distinct.Count == 0)
        {
            Throw.ArgumentException("At least one old state is required.", parameterName);
        }

        return distinct;
    }

    /// <summary>
    /// Binds the parameters of the predicate <see cref="AdoUtil.BuildTriggerStatePredicate" /> built for
    /// the same states, in the same order.
    /// </summary>
    private void AddOldStateParameters(DbCommand cmd, List<StoredTriggerState> oldStates)
    {
        for (int i = 0; i < oldStates.Count; i++)
        {
            AddCommandParameter(cmd, AdoUtil.TriggerStateParameter(i), oldStates[i].ToStoredValue());
        }
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> UpdateTriggerStatesFromOtherStates(
        ConnectionAndTransactionHolder conn,
        StoredTriggerState newState,
        IReadOnlyCollection<StoredTriggerState> oldStates,
        CancellationToken cancellationToken = default)
    {
        List<StoredTriggerState> states = DistinctStates(oldStates, nameof(oldStates));

        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlUpdateTriggerStatesFromOtherStatesPrefix + AdoUtil.BuildTriggerStatePredicate(states.Count)));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "newState", newState.ToStoredValue());
        AddOldStateParameters(cmd, states);
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<List<TriggerKey>> SelectTriggersInState(ConnectionAndTransactionHolder conn, StoredTriggerState state, CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectTriggersInState));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "state", state.ToStoredValue());
        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        List<TriggerKey> list = [];
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new TriggerKey(rs.GetString(0), rs.GetString(1)));
        }

        return list;
    }

    protected virtual string GetSelectMisfiredTriggersToRecoverSql(int count)
    {
        // by default we don't support limits, this is db specific
        return StdAdoConstants.SqlSelectMisfiredTriggersToRecover;
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> CountMisfiredTriggersInState(
        ConnectionAndTransactionHolder conn,
        StoredTriggerState state,
        DateTimeOffset misfireTime,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(GetCountMisfiredTriggersInStateSql()));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "nextFireTime", GetDbDateTimeValue(misfireTime));
        AddCommandParameter(cmd, "state", state.ToStoredValue());

        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public virtual async ValueTask<List<IOperableTrigger>> SelectTriggersForRecoveringJobs(ConnectionAndTransactionHolder conn, CancellationToken cancellationToken = default)
    {
        List<IOperableTrigger> triggers = [];

        // Only the two timestamps of the fired-trigger row are carried over to the recovery trigger's
        // job data, so there is no reason to build a whole record for them.
        List<(DateTimeOffset ScheduleTimestamp, DateTimeOffset FireTimestamp)> triggerData = [];
        List<TriggerKey> keys = [];

        using (var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectInstancesRecoverableFiredTriggers)))
        {
            AddCommandParameter(cmd, "schedulerName", schedulerName);
            AddCommandParameter(cmd, "instanceName", instanceId);
            AddCommandParameter(cmd, "requestsRecovery", GetDbBooleanValue(true));

            using (var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                long dumId = timeProvider.GetTimestamp();

                while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    string jobName = rs.GetString(AdoConstants.ColumnJobName)!;
                    string jobGroup = rs.GetString(AdoConstants.ColumnJobGroup)!;
                    string trigName = rs.GetString(AdoConstants.ColumnTriggerName)!;
                    string trigGroup = rs.GetString(AdoConstants.ColumnTriggerGroup)!;
                    int priority = Convert.ToInt32(rs[AdoConstants.ColumnPriority], CultureInfo.InvariantCulture);
                    DateTimeOffset firedTime = GetDateTimeFromDbValue(rs[AdoConstants.ColumnFiredTime]) ?? DateTimeOffset.MinValue;
                    DateTimeOffset scheduledTime = GetDateTimeFromDbValue(rs[AdoConstants.ColumnScheduledTime]) ?? DateTimeOffset.MinValue;
                    SimpleTriggerImpl rcvryTrig = new SimpleTriggerImpl(timeProvider)
                    {
                        Key = new TriggerKey("recover_" + instanceId + "_" + Convert.ToString(dumId++, CultureInfo.InvariantCulture), SchedulerConstants.DefaultRecoveryGroup),
                        StartTimeUtc = scheduledTime,
                        JobKey = new JobKey(jobName, jobGroup),
                        Priority = priority,
                        MisfireInstructionCode = MisfireInstruction.IgnoreMisfirePolicy
                    };

                    triggerData.Add((scheduledTime, firedTime));
                    triggers.Add(rcvryTrig);
                    keys.Add(new TriggerKey(trigName, trigGroup));
                }
            }
        }

        // read JobDataMaps with different reader..
        for (int i = 0; i < triggers.Count; i++)
        {
            IOperableTrigger trigger = triggers[i];
            TriggerKey key = keys[i];
            (DateTimeOffset ScheduleTimestamp, DateTimeOffset FireTimestamp) dataHolder = triggerData[i];

            // load job data map and transfer information
            JobDataMap jd = await SelectTriggerJobDataMap(conn, key, cancellationToken).ConfigureAwait(false);
            jd[SchedulerConstants.FailedJobOriginalTriggerName] = key.Name;
            jd[SchedulerConstants.FailedJobOriginalTriggerGroup] = key.Group;
            jd[SchedulerConstants.FailedJobOriginalTriggerFireTime] = Convert.ToString(dataHolder.FireTimestamp, CultureInfo.InvariantCulture)!;
            jd[SchedulerConstants.FailedJobOriginalTriggerScheduledFireTime] = Convert.ToString(dataHolder.ScheduleTimestamp, CultureInfo.InvariantCulture)!;
            trigger.JobDataMap = jd;
        }

        return triggers;
    }

    /// <summary>
    /// Appends the predicates a <see cref="FiredTriggerQuery" /> asks for to a FIRED_TRIGGERS statement.
    /// </summary>
    /// <remarks>
    /// The order the predicates are appended in is the order
    /// <see cref="BindFiredTriggerQuery" /> binds them in, which matters to providers that adapt named
    /// parameters positionally.
    /// </remarks>
    private static string BuildFiredTriggerQuerySql(string sql, FiredTriggerQuery query)
    {
        if (query.Trigger is not null)
        {
            sql += StdAdoConstants.SqlFiredTriggerTriggerPredicate;
        }

        if (query.Job is not null)
        {
            sql += StdAdoConstants.SqlFiredTriggerJobPredicate;
        }

        if (query.InstanceId is not null)
        {
            sql += StdAdoConstants.SqlFiredTriggerInstancePredicate;
        }

        return sql;
    }

    /// <summary>
    /// Binds the scheduler name and whatever predicates
    /// <see cref="BuildFiredTriggerQuerySql" /> appended, in the same order.
    /// </summary>
    private void BindFiredTriggerQuery(DbCommand cmd, FiredTriggerQuery query)
    {
        AddCommandParameter(cmd, "schedulerName", schedulerName);

        if (query.Trigger is not null)
        {
            AddCommandParameter(cmd, "triggerName", query.Trigger.Name);
            AddCommandParameter(cmd, "triggerGroup", query.Trigger.Group);
        }

        if (query.Job is not null)
        {
            AddCommandParameter(cmd, "jobName", query.Job.Name);
            AddCommandParameter(cmd, "jobGroup", query.Job.Group);
        }

        if (query.InstanceId is not null)
        {
            AddCommandParameter(cmd, "instanceName", query.InstanceId);
        }
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> DeleteFiredTriggers(
        ConnectionAndTransactionHolder conn,
        FiredTriggerQuery query,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(BuildFiredTriggerQuerySql(StdAdoConstants.SqlDeleteFiredTriggers, query)));
        BindFiredTriggerQuery(cmd, query);
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<bool> IsJobCurrentlyExecuting(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        using DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectCountExecutingFiredTriggersOfJob));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "jobName", jobKey.Name);
        AddCommandParameter(cmd, "jobGroup", jobKey.Group);
        AddCommandParameter(cmd, "executingState", StoredTriggerState.Executing.ToStoredValue());

        object? result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result) > 0;
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> InsertTrigger(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        StoredTriggerState state,
        IJobDetail jobDetail,
        CancellationToken cancellationToken = default)
    {
        var jobData = SerializeJobData(trigger.JobDataMap);

        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlInsertTrigger));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "triggerName", trigger.Key.Name);
        AddCommandParameter(cmd, "triggerGroup", trigger.Key.Group);
        AddCommandParameter(cmd, "triggerJobName", trigger.JobKey.Name);
        AddCommandParameter(cmd, "triggerJobGroup", trigger.JobKey.Group);
        AddCommandParameter(cmd, "triggerDescription", trigger.Description);
        AddCommandParameter(cmd, "triggerNextFireTime", GetDbDateTimeValue(trigger.NextFireTimeUtc));
        AddCommandParameter(cmd, "triggerPreviousFireTime", GetDbDateTimeValue(trigger.PreviousFireTimeUtc));
        AddCommandParameter(cmd, "triggerState", state.ToStoredValue());

        var tDel = FindTriggerPersistenceDelegate(trigger);
        string type = AdoConstants.TriggerTypeBlob;
        if (tDel is not null)
        {
            type = tDel.GetHandledTriggerTypeDiscriminator();
        }

        AddCommandParameter(cmd, "triggerType", type);
        AddCommandParameter(cmd, "triggerStartTime", GetDbDateTimeValue(trigger.StartTimeUtc));
        AddCommandParameter(cmd, "triggerEndTime", GetDbDateTimeValue(trigger.EndTimeUtc));
        AddCommandParameter(cmd, "triggerCalendarName", trigger.CalendarName);
        AddCommandParameter(cmd, "triggerMisfireInstruction", trigger.MisfireInstructionCode);
        AddCommandParameter(cmd, "triggerJobJobDataMap", jobData, DbProvider.Metadata.BinaryParameterType);

        AddCommandParameter(cmd, "triggerPriority", trigger.Priority);

        string? execGroup = trigger.ExecutionGroup;
        AddCommandParameter(cmd, "triggerExecutionGroup", (object?) execGroup ?? DBNull.Value);

        PreferredNode preferredNode = trigger.PreferredNode;
        AddCommandParameter(cmd, "triggerPreferredNode", (object?) preferredNode.StoredNode ?? DBNull.Value);
        AddCommandParameter(cmd, "triggerPreferredNodeAuto", GetDbBooleanValue(preferredNode.StoredAutomatic));

        int insertResult = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        if (tDel is null)
        {
            await InsertBlobTrigger(conn, trigger, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await tDel.InsertExtendedTriggerProperties(conn, trigger, state, jobDetail, cancellationToken).ConfigureAwait(false);
        }

        return insertResult;
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> InsertBlobTrigger(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlInsertBlobTrigger));
        // update the blob
        byte[]? buf = SerializeObject(trigger);
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "triggerName", trigger.Key.Name);
        AddCommandParameter(cmd, "triggerGroup", trigger.Key.Group);
        AddCommandParameter(cmd, "blob", buf, DbProvider.Metadata.BinaryParameterType);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> UpdateTrigger(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        StoredTriggerState state,
        IJobDetail jobDetail,
        CancellationToken cancellationToken = default)
    {
        var existingType = await SelectTriggerType(conn, trigger.Key, cancellationToken).ConfigureAwait(false);

        // No need to continue if the trigger type is not found - there's nothing to update.
        if (existingType is null) return 0;

        var tDel = FindTriggerPersistenceDelegate(trigger);
        string type = tDel?.GetHandledTriggerTypeDiscriminator() ?? AdoConstants.TriggerTypeBlob;

        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(BuildUpdateTriggerSql(trigger, out bool updateJobData, out bool writePreferredNode)));
        BindUpdateTrigger(cmd, trigger, state, type, updateJobData, writePreferredNode);

        var updateResult = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await WriteTriggerTypeTable(conn, trigger, state, jobDetail, existingType, type, tDel, cancellationToken).ConfigureAwait(false);

        return updateResult;
    }

    /// <summary>
    /// Picks the flavour of the trigger UPDATE this write needs, and reports the two decisions that
    /// picked it, because the parameter binding has to make exactly the same ones.
    /// </summary>
    /// <param name="trigger">The trigger being written.</param>
    /// <param name="updateJobData">
    /// Whether the job data map goes into the statement. Skipped when the map is not dirty, which saves
    /// serializing and shipping a blob that has not changed.
    /// </param>
    /// <param name="writePreferredNode">
    /// Whether the preferred node columns go into the statement. Only written when the pin was actually
    /// changed on this instance: a trigger on the fire path carries the value loaded at acquire time, and
    /// writing that back would clobber a concurrent re-pin (ClusterRecover's failover reset, an
    /// <c>UpdateTriggerDetails</c> re-pin).
    /// </param>
    private static string BuildUpdateTriggerSql(IOperableTrigger trigger, out bool updateJobData, out bool writePreferredNode)
    {
        updateJobData = trigger.JobDataMap.Dirty;
        writePreferredNode = (trigger as TriggerBase)?.PreferredNodeDirty == true;

        return (updateJobData, writePreferredNode) switch
        {
            (true, true) => StdAdoConstants.SqlUpdateTriggerWithPreferredNode,
            (true, false) => StdAdoConstants.SqlUpdateTrigger,
            (false, true) => StdAdoConstants.SqlUpdateTriggerSkipDataWithPreferredNode,
            (false, false) => StdAdoConstants.SqlUpdateTriggerSkipData,
        };
    }

    /// <summary>
    /// Binds the trigger UPDATE onto a command. Parameters are added in SQL token order, for providers
    /// with positional binding.
    /// </summary>
    private void BindUpdateTrigger(
        DbCommand cmd,
        IOperableTrigger trigger,
        StoredTriggerState state,
        string type,
        bool updateJobData,
        bool writePreferredNode)
    {
        foreach (SqlStatementParameter parameter in BuildUpdateTriggerParameters(trigger, state, type, updateJobData, writePreferredNode))
        {
            AddCommandParameter(cmd, parameter.Name, parameter.Value, parameter.DataType);
        }
    }

    /// <summary>
    /// The trigger UPDATE as data, for the paths that send it inside a batch rather than on a command of
    /// its own. Same statement and same binding as <see cref="UpdateTrigger" /> — deliberately so, since
    /// two spellings of this statement would be two things to keep in step.
    /// </summary>
    private SqlStatement BuildUpdateTriggerStatement(IOperableTrigger trigger, StoredTriggerState state, string type)
    {
        string sql = BuildUpdateTriggerSql(trigger, out bool updateJobData, out bool writePreferredNode);
        return new SqlStatement(
            ReplaceTablePrefix(sql),
            BuildUpdateTriggerParameters(trigger, state, type, updateJobData, writePreferredNode));
    }

    private List<SqlStatementParameter> BuildUpdateTriggerParameters(
        IOperableTrigger trigger,
        StoredTriggerState state,
        string type,
        bool updateJobData,
        bool writePreferredNode)
    {
        List<SqlStatementParameter> parameters =
        [
            new("schedulerName", schedulerName),
            new("triggerJobName", trigger.JobKey.Name),
            new("triggerJobGroup", trigger.JobKey.Group),
            new("triggerDescription", trigger.Description),
            new("triggerNextFireTime", GetDbDateTimeValue(trigger.NextFireTimeUtc)),
            new("triggerPreviousFireTime", GetDbDateTimeValue(trigger.PreviousFireTimeUtc)),
            new("triggerState", state.ToStoredValue()),
            new("triggerType", type),
            new("triggerStartTime", GetDbDateTimeValue(trigger.StartTimeUtc)),
            new("triggerEndTime", GetDbDateTimeValue(trigger.EndTimeUtc)),
            new("triggerCalendarName", trigger.CalendarName),
            new("triggerMisfireInstruction", trigger.MisfireInstructionCode),
            new("triggerPriority", trigger.Priority)
        ];

        if (updateJobData)
        {
            parameters.Add(new SqlStatementParameter("triggerJobJobDataMap", SerializeJobData(trigger.JobDataMap), DbProvider.Metadata.BinaryParameterType));
        }

        parameters.Add(new SqlStatementParameter("triggerExecutionGroup", (object?) trigger.ExecutionGroup ?? DBNull.Value));

        if (writePreferredNode)
        {
            PreferredNode preferredNode = trigger.PreferredNode;
            parameters.Add(new SqlStatementParameter("triggerPreferredNode", (object?) preferredNode.StoredNode ?? DBNull.Value));
            parameters.Add(new SqlStatementParameter("triggerPreferredNodeAuto", GetDbBooleanValue(preferredNode.StoredAutomatic)));
        }

        parameters.Add(new SqlStatementParameter("triggerName", trigger.Key.Name));
        parameters.Add(new SqlStatementParameter("triggerGroup", trigger.Key.Group));

        return parameters;
    }

    /// <summary>
    /// Writes the trigger's schedule into whichever type table now holds it, moving it between tables
    /// when the trigger's type changed since it was stored.
    /// </summary>
    /// <param name="conn">The DB connection.</param>
    /// <param name="trigger">The trigger being written.</param>
    /// <param name="state">The state written on the trigger's own row.</param>
    /// <param name="jobDetail">The job the trigger fires.</param>
    /// <param name="existingType">The discriminator the trigger's row held before this write.</param>
    /// <param name="type">The discriminator it holds now.</param>
    /// <param name="tDel">The delegate handling <paramref name="type" />, or null for a blob trigger.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    private async ValueTask WriteTriggerTypeTable(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        StoredTriggerState state,
        IJobDetail jobDetail,
        string existingType,
        string type,
        ITriggerPersistenceDelegate? tDel,
        CancellationToken cancellationToken)
    {
        if (type == existingType)
        {
            if (tDel is null)
            {
                await UpdateBlobTrigger(conn, trigger, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await tDel.UpdateExtendedTriggerProperties(conn, trigger, state, jobDetail, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        var existingDel = FindTriggerPersistenceDelegate(existingType);

        if (existingDel is null)
        {
            await DeleteBlobTrigger(conn, trigger.Key, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await existingDel.DeleteExtendedTriggerProperties(conn, trigger.Key, cancellationToken).ConfigureAwait(false);
        }

        if (tDel is null)
        {
            await InsertBlobTrigger(conn, trigger, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await tDel.InsertExtendedTriggerProperties(conn, trigger, state, jobDetail, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> UpdateBlobTrigger(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlUpdateBlobTrigger));
        // update the blob
        byte[]? os = SerializeObject(trigger);

        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "blob", os, DbProvider.Metadata.BinaryParameterType);
        AddCommandParameter(cmd, "triggerName", trigger.Key.Name);
        AddCommandParameter(cmd, "triggerGroup", trigger.Key.Group);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> UpdateTriggerState(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        StoredTriggerState state,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlUpdateTriggerState));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "state", state.ToStoredValue());
        AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> UpdateTriggerStateFromOtherStates(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        StoredTriggerState newState,
        IReadOnlyCollection<StoredTriggerState> oldStates,
        CancellationToken cancellationToken = default)
    {
        List<StoredTriggerState> states = DistinctStates(oldStates, nameof(oldStates));

        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlUpdateTriggerStateFromStatesPrefix + AdoUtil.BuildTriggerStatePredicate(states.Count)));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "newState", newState.ToStoredValue());
        AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);
        AddOldStateParameters(cmd, states);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> UpdateTriggerGroupStateFromOtherStates(
        ConnectionAndTransactionHolder conn,
        GroupMatcher<TriggerKey> matcher,
        StoredTriggerState newState,
        IReadOnlyCollection<StoredTriggerState> oldStates,
        CancellationToken cancellationToken = default)
    {
        List<StoredTriggerState> states = DistinctStates(oldStates, nameof(oldStates));
        (string sql, string parameter) = MatchGroup(matcher, StdAdoConstants.SqlUpdateTriggerGroupStateFromStatesEqualsPrefix, StdAdoConstants.SqlUpdateTriggerGroupStateFromStatesLikePrefix);

        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(sql + AdoUtil.BuildTriggerStatePredicate(states.Count)));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "newState", newState.ToStoredValue());
        AddCommandParameter(cmd, "groupName", parameter);
        AddOldStateParameters(cmd, states);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> UpdateTriggerStateFromOtherState(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        StoredTriggerState newState,
        StoredTriggerState oldState,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlUpdateTriggerStateFromState));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "newState", newState.ToStoredValue());
        AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);
        AddCommandParameter(cmd, "oldState", oldState.ToStoredValue());

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<int> UpdateTriggerStateFromOtherStateWithNextFireTime(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        StoredTriggerState newState,
        StoredTriggerState oldState,
        DateTimeOffset nextFireTime,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlUpdateTriggerStateFromStateWithNextFireTime));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "newState", newState.ToStoredValue());
        AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);
        AddCommandParameter(cmd, "oldState", oldState.ToStoredValue());
        AddCommandParameter(cmd, "nextFireTime", GetDbDateTimeValue(nextFireTime));

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> UpdateTriggerGroupStateFromOtherState(
        ConnectionAndTransactionHolder conn,
        GroupMatcher<TriggerKey> matcher,
        StoredTriggerState newState,
        StoredTriggerState oldState,
        CancellationToken cancellationToken = default)
    {
        (string sql, string parameter) = MatchGroup(matcher, StdAdoConstants.SqlUpdateTriggerGroupStateFromStateEquals, StdAdoConstants.SqlUpdateTriggerGroupStateFromStateLike);

        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(sql));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "newState", newState.ToStoredValue());
        AddCommandParameter(cmd, "triggerGroup", parameter);
        AddCommandParameter(cmd, "oldState", oldState.ToStoredValue());

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> UpdateTriggerStatesForJob(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        StoredTriggerState state,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlUpdateJobTriggerStates));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "state", state.ToStoredValue());
        AddCommandParameter(cmd, "jobName", jobKey.Name);
        AddCommandParameter(cmd, "jobGroup", jobKey.Group);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> UpdateTriggerStatesForJobFromOtherState(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        StoredTriggerState newState,
        StoredTriggerState oldState,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlUpdateJobTriggerStatesFromOtherState));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "state", newState.ToStoredValue());
        AddCommandParameter(cmd, "jobName", jobKey.Name);
        AddCommandParameter(cmd, "jobGroup", jobKey.Group);
        AddCommandParameter(cmd, "oldState", oldState.ToStoredValue());

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual ValueTask UpdateTriggerStatesForJobFromOtherState(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        IReadOnlyList<TriggerStateTransition> transitions,
        CancellationToken cancellationToken = default)
    {
        if (transitions.Count == 0)
        {
            return default;
        }

        List<SqlStatement> statements = new(transitions.Count);
        AddJobTriggerStateTransitions(statements, jobKey, transitions);
        return ExecuteStatements(conn, statements, cancellationToken);
    }

    private void AddJobTriggerStateTransitions(
        List<SqlStatement> into,
        JobKey jobKey,
        IReadOnlyList<TriggerStateTransition> transitions)
    {
        string sql = ReplaceTablePrefix(StdAdoConstants.SqlUpdateJobTriggerStatesFromOtherState);
        for (int i = 0; i < transitions.Count; i++)
        {
            TriggerStateTransition transition = transitions[i];
            into.Add(new SqlStatement(sql,
            [
                new SqlStatementParameter("schedulerName", schedulerName),
                new SqlStatementParameter("state", transition.To.ToStoredValue()),
                new SqlStatementParameter("jobName", jobKey.Name),
                new SqlStatementParameter("jobGroup", jobKey.Group),
                new SqlStatementParameter("oldState", transition.From.ToStoredValue())
            ]));
        }
    }

    /// <summary>
    /// The transitions a fire applies to the other triggers of a job that disallows concurrent
    /// execution: everything that could still be picked up is parked until the job is done with.
    /// </summary>
    private static readonly TriggerStateTransition[] blockJobTriggersTransitions =
    [
        new(StoredTriggerState.Waiting, StoredTriggerState.Blocked),
        new(StoredTriggerState.Acquired, StoredTriggerState.Blocked),
        new(StoredTriggerState.Paused, StoredTriggerState.PausedBlocked)
    ];

    /// <inheritdoc />
    public virtual async ValueTask ApplyTriggerFired(
        ConnectionAndTransactionHolder conn,
        TriggerFiredUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        IOperableTrigger trigger = update.Trigger;
        List<SqlStatement> statements = [];

        statements.Add(new SqlStatement(ReplaceTablePrefix(StdAdoConstants.SqlUpdateFiredTrigger),
        [
            new SqlStatementParameter("schedulerName", schedulerName),
            new SqlStatementParameter("instanceName", instanceId),
            new SqlStatementParameter("firedTime", GetDbDateTimeValue(timeProvider.GetUtcNow())),
            new SqlStatementParameter("scheduledTime", GetDbDateTimeValue(update.ScheduledFireTimeUtc)),
            new SqlStatementParameter("entryState", StoredTriggerState.Executing.ToStoredValue()),
            new SqlStatementParameter("jobName", trigger.JobKey.Name),
            new SqlStatementParameter("jobGroup", trigger.JobKey.Group),
            new SqlStatementParameter("isNonConcurrent", GetDbBooleanValue(update.JobDetail.ConcurrentExecutionDisallowed)),
            new SqlStatementParameter("requestsRecover", GetDbBooleanValue(update.JobDetail.RequestsRecovery)),
            new SqlStatementParameter("executionGroup", (object?) trigger.ExecutionGroup ?? DBNull.Value),
            new SqlStatementParameter("entryId", trigger.FireInstanceId)
        ]));

        if (update.ClearMisfireOriginalFireTime)
        {
            statements.Add(new SqlStatement(ReplaceTablePrefix(StdAdoConstants.SqlUpdateMisfireOrigFireTime),
            [
                new SqlStatementParameter("schedulerName", schedulerName),
                new SqlStatementParameter("triggerName", trigger.Key.Name),
                new SqlStatementParameter("triggerGroup", trigger.Key.Group),
                new SqlStatementParameter("misfireOrigFireTime", null)
            ]));
        }

        if (update.BlockJobTriggers)
        {
            // Before the trigger's own row is written, exactly as when these were separate round trips:
            // the trigger is still ACQUIRED at this point, so it is one of the rows this moves to
            // BLOCKED, and its own UPDATE then writes the state the store decided on over the top.
            AddJobTriggerStateTransitions(statements, update.JobDetail.Key, blockJobTriggersTransitions);
        }

        ITriggerPersistenceDelegate? tDel = FindTriggerPersistenceDelegate(trigger);
        string type = tDel?.GetHandledTriggerTypeDiscriminator() ?? AdoConstants.TriggerTypeBlob;

        statements.Add(BuildUpdateTriggerStatement(trigger, update.NewState, type));

        // A trigger that still has the type it was stored with — which is every trigger the fire path
        // sees, since it was rebuilt from that very row — can have its schedule written in the same
        // round trip, provided its persistence delegate can describe the statement rather than issue it.
        bool typeUnchanged = type == update.StoredTriggerType;
        bool describedTypeTable = typeUnchanged
            && tDel is not null
            && tDel.TryDescribeUpdateExtendedTriggerProperties(trigger, update.NewState, update.JobDetail, statements);

        await ExecuteStatements(conn, statements, cancellationToken).ConfigureAwait(false);

        if (!describedTypeTable)
        {
            await WriteTriggerTypeTable(conn, trigger, update.NewState, update.JobDetail, update.StoredTriggerType, type, tDel, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> DeleteBlobTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlDeleteBlobTrigger));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> DeleteTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        await DeleteTriggerExtension(conn, triggerKey, cancellationToken).ConfigureAwait(false);

        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlDeleteTrigger));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    protected virtual async ValueTask DeleteTriggerExtension(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        foreach (ITriggerPersistenceDelegate tDel in triggerPersistenceDelegates)
        {
            if (await tDel.DeleteExtendedTriggerProperties(conn, triggerKey, cancellationToken).ConfigureAwait(false) > 0)
            {
                return; // as soon as one affects a row, we're done.
            }
        }

        await DeleteBlobTrigger(conn, triggerKey, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Everything read from one row of the trigger select. That select left-joins the SIMPLE and CRON
    /// type tables, so for those two types <see cref="Props" /> comes back populated from the same row
    /// and no follow-up query is needed.
    /// </summary>
    private sealed class TriggerRow
    {
        public string JobName = null!;
        public string JobGroup = null!;
        public string? Description;
        public string TriggerType = null!;
        public string? CalendarName;
        public int MisfireInstruction;
        public int Priority;
        public JobDataMap? JobDataMap;
        public DateTimeOffset? NextFireTimeUtc;
        public DateTimeOffset? PreviousFireTimeUtc;
        public DateTimeOffset StartTimeUtc;
        public DateTimeOffset? EndTimeUtc;
        public DateTimeOffset? MisfireOriginalFireTime;
        public string? ExecutionGroup;
        public string? PreferredNode;
        public bool PreferredNodeAuto;

        /// <summary>Populated from the joined row for SIMPLE and CRON triggers, <c>null</c> otherwise.</summary>
        public TriggerPropertyBundle? Props;
    }

    /// <summary>
    /// Where each column of a trigger select sits, worked out once for a reader instead of once per
    /// column per row.
    /// </summary>
    /// <remarks>
    /// Reading a column by name asks the provider for its position first, and the trigger row has
    /// sixteen of them. Every statement <see cref="ReadTriggerRow" /> serves projects the same columns,
    /// so a reader's layout is fixed for as long as it is open.
    /// </remarks>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly struct TriggerRowOrdinals
    {
        /// <param name="rs">The reader whose layout to record.</param>
        /// <param name="includesKey">
        /// Whether the statement projects the trigger key as well. The batch statements append it — they
        /// have to, since the row is all the caller has to tell one trigger from another — while the
        /// single-trigger select already knows which key it asked for and leaves the columns out.
        /// </param>
        public TriggerRowOrdinals(DbDataReader rs, bool includesKey)
        {
            TriggerName = includesKey ? rs.GetOrdinal(AdoConstants.ColumnTriggerName) : -1;
            TriggerGroup = includesKey ? rs.GetOrdinal(AdoConstants.ColumnTriggerGroup) : -1;
            JobName = rs.GetOrdinal(AdoConstants.ColumnJobName);
            JobGroup = rs.GetOrdinal(AdoConstants.ColumnJobGroup);
            Description = rs.GetOrdinal(AdoConstants.ColumnDescription);
            TriggerType = rs.GetOrdinal(AdoConstants.ColumnTriggerType);
            CalendarName = rs.GetOrdinal(AdoConstants.ColumnCalendarName);
            MisfireInstruction = rs.GetOrdinal(AdoConstants.ColumnMisfireInstruction);
            Priority = rs.GetOrdinal(AdoConstants.ColumnPriority);
            NextFireTime = rs.GetOrdinal(AdoConstants.ColumnNextFireTime);
            PreviousFireTime = rs.GetOrdinal(AdoConstants.ColumnPreviousFireTime);
            StartTime = rs.GetOrdinal(AdoConstants.ColumnStartTime);
            EndTime = rs.GetOrdinal(AdoConstants.ColumnEndTime);
            MisfireOriginalFireTime = rs.GetOrdinal(AdoConstants.ColumnMisfireOriginalFireTime);
            ExecutionGroup = rs.GetOrdinal(AdoConstants.ColumnExecutionGroup);
            PreferredNode = rs.GetOrdinal(AdoConstants.ColumnPreferredNode);
            PreferredNodeAuto = rs.GetOrdinal(AdoConstants.ColumnPreferredNodeAuto);
        }

        public int TriggerName { get; }
        public int TriggerGroup { get; }
        public int JobName { get; }
        public int JobGroup { get; }
        public int Description { get; }
        public int TriggerType { get; }
        public int CalendarName { get; }
        public int MisfireInstruction { get; }
        public int Priority { get; }
        public int NextFireTime { get; }
        public int PreviousFireTime { get; }
        public int StartTime { get; }
        public int EndTime { get; }
        public int MisfireOriginalFireTime { get; }
        public int ExecutionGroup { get; }
        public int PreferredNode { get; }
        public int PreferredNodeAuto { get; }

        public TriggerKey ReadKey(DbDataReader rs) => new(rs.GetString(TriggerName), rs.GetString(TriggerGroup));
    }

    /// <summary>
    /// Reads the current row of a trigger select. Shared by the single-trigger and batch read paths so
    /// the two cannot drift apart.
    /// </summary>
    private async ValueTask<TriggerRow> ReadTriggerRow(DbDataReader rs, TriggerRowOrdinals ordinals)
    {
        var row = new TriggerRow
        {
            JobName = rs.GetString(ordinals.JobName),
            JobGroup = rs.GetString(ordinals.JobGroup),
            Description = ReadNullableString(rs, ordinals.Description),
            TriggerType = rs.GetString(ordinals.TriggerType),
            CalendarName = ReadNullableString(rs, ordinals.CalendarName),
            // Not GetInt32: Oracle hands back a decimal for a NUMBER column.
            MisfireInstruction = Convert.ToInt32(rs.GetValue(ordinals.MisfireInstruction), CultureInfo.InvariantCulture),
            Priority = Convert.ToInt32(rs.GetValue(ordinals.Priority), CultureInfo.InvariantCulture)
        };

        row.JobDataMap = await ReadMapFromReader(rs, 11).ConfigureAwait(false);

        row.NextFireTimeUtc = GetDateTimeFromDbValue(rs.GetValue(ordinals.NextFireTime));
        row.PreviousFireTimeUtc = GetDateTimeFromDbValue(rs.GetValue(ordinals.PreviousFireTime));
        row.StartTimeUtc = GetDateTimeFromDbValue(rs.GetValue(ordinals.StartTime)) ?? DateTimeOffset.MinValue;
        row.EndTimeUtc = GetDateTimeFromDbValue(rs.GetValue(ordinals.EndTime));

        // check if we access fast path
        if (row.TriggerType is AdoConstants.TriggerTypeCron or AdoConstants.TriggerTypeSimple)
        {
            row.Props = FindTriggerPersistenceDelegate(row.TriggerType)!.ReadTriggerPropertyBundle(rs);
        }

        row.MisfireOriginalFireTime = GetDateTimeFromDbValue(rs.GetValue(ordinals.MisfireOriginalFireTime));

        row.ExecutionGroup = ReadNullableString(rs, ordinals.ExecutionGroup);
        row.PreferredNode = ReadNullableString(rs, ordinals.PreferredNode);
        row.PreferredNodeAuto = !rs.IsDBNull(ordinals.PreferredNodeAuto) && GetBooleanFromDbValue(rs.GetValue(ordinals.PreferredNodeAuto));

        return row;
    }

    private static string? ReadNullableString(DbDataReader rs, int ordinal)
        => rs.IsDBNull(ordinal) ? null : rs.GetString(ordinal);

    /// <summary>
    /// Applies the fire-time state carried on the TRIGGERS row. Applies to blob-deserialized triggers
    /// just as much as to built ones.
    /// </summary>
    private static void ApplyTriggerFireState(IOperableTrigger trigger, TriggerRow row)
    {
        trigger.MisfireInstructionCode = row.MisfireInstruction;
        trigger.NextFireTimeUtc = row.NextFireTimeUtc;
        trigger.PreviousFireTimeUtc = row.PreviousFireTimeUtc;

        if (row.MisfireOriginalFireTime.HasValue && trigger is TriggerBase at)
        {
            at.MisfiredFromFireTimeUtc = row.MisfireOriginalFireTime;
        }
    }

    /// <summary>
    /// Applies the routing state carried on the TRIGGERS row. Applied last, so that it cannot be
    /// overwritten by a persistence delegate's state properties.
    /// </summary>
    private static void ApplyTriggerRoutingState(IOperableTrigger trigger, TriggerRow row)
    {
        trigger.ExecutionGroup = row.ExecutionGroup;

        // Populating from the trigger's own row — not a change, so it must not mark the pin
        // dirty (that would make the next store write it back and clobber concurrent re-pins).
        (trigger as TriggerBase)?.SetPreferredNode(PreferredNode.FromStored(row.PreferredNode, row.PreferredNodeAuto), markDirty: false);
    }

    /// <summary>
    /// Applies the TRIGGERS row state onto a trigger deserialized from BLOB_TRIGGERS. The schedule
    /// itself came out of the blob, so there are no extended properties to apply in between.
    /// </summary>
    private void ApplyBlobTriggerRowState(IOperableTrigger trigger, TriggerRow row)
    {
        ApplyStoreClock(trigger);
        ApplyTriggerFireState(trigger, row);
        ApplyTriggerRoutingState(trigger, row);
    }

    /// <summary>
    /// Hands a materialized trigger the clock this store runs on, so that the misfire arithmetic the
    /// store is about to ask it for is done against the same reading of "now" the store decided it
    /// had misfired by.
    /// </summary>
    /// <remarks>
    /// A trigger out of a blob carries no clock at all — the field does not serialize — and one out of
    /// a schedule builder only carries whatever built it, which is this store only because
    /// <see cref="BuildTrigger" /> creates the builder with the store's clock.
    /// </remarks>
    private void ApplyStoreClock(IOperableTrigger trigger)
    {
        if (trigger is TriggerBase triggerBase)
        {
            triggerBase.TimeProvider = timeProvider;
        }
    }

    /// <summary>
    /// Builds a non-blob trigger from its TRIGGERS row and its type-specific extended properties.
    /// </summary>
    private IOperableTrigger BuildTrigger(TriggerKey triggerKey, TriggerRow row, TriggerPropertyBundle triggerProps)
    {
        // The store's clock, not the machine's: TriggerBuilder gives it to the trigger it builds, so
        // everything the trigger reads as "now" from here on is the scheduler's reading.
        var tb = TriggerBuilder.Create(timeProvider)
            .WithDescription(row.Description)
            .WithPriority(row.Priority)
            .StartAt(row.StartTimeUtc)
            .EndAt(row.EndTimeUtc)
            .WithIdentity(triggerKey)
            .WithCalendarName(row.CalendarName)
            .WithSchedule(triggerProps.ScheduleBuilder)
            .ForJob(new JobKey(row.JobName, row.JobGroup));

        if (row.JobDataMap is not null)
        {
            bool clearDirtyFlag = !row.JobDataMap.ContainsKey(SchedulerConstants.ForceJobDataMapDirty);
            tb.UsingJobData(new JobDataMap(row.JobDataMap));
            if (clearDirtyFlag)
            {
                tb.ClearDirty();
            }
        }

        var trigger = (IOperableTrigger) tb.Build();

        ApplyTriggerFireState(trigger, row);
        // The applier is null when the delegate carries no state beyond the schedule (Cron does not).
        triggerProps.ApplyState?.Invoke(trigger);
        ApplyTriggerRoutingState(trigger, row);

        return trigger;
    }

    /// <inheritdoc />
    public virtual async ValueTask<IOperableTrigger?> SelectTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        TriggerRow row;

        using (var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectTrigger)))
        {
            AddCommandParameter(cmd, "schedulerName", schedulerName);
            AddCommandParameter(cmd, "triggerName", triggerKey.Name);
            AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

            using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            row = await ReadTriggerRow(rs, new TriggerRowOrdinals(rs, includesKey: false)).ConfigureAwait(false);
        }

        if (row.TriggerType == AdoConstants.TriggerTypeBlob)
        {
            IOperableTrigger? blobTrigger = null;

            using (var cmd2 = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectBlobTrigger)))
            {
                AddCommandParameter(cmd2, "schedulerName", schedulerName);
                AddCommandParameter(cmd2, "triggerName", triggerKey.Name);
                AddCommandParameter(cmd2, "triggerGroup", triggerKey.Group);
                using var rs2 = await cmd2.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false);
                if (await rs2.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    blobTrigger = await GetObjectFromBlob<IOperableTrigger>(rs2, 0, cancellationToken).ConfigureAwait(false);
                }
            }

            if (blobTrigger is not null)
            {
                ApplyBlobTriggerRowState(blobTrigger, row);
            }

            return blobTrigger;
        }

        TriggerPropertyBundle? triggerProps = row.Props;
        if (triggerProps is null)
        {
            // fast path didn't succeed
            var tDel = FindTriggerPersistenceDelegate(row.TriggerType);

            if (tDel is null)
            {
                Throw.JobPersistenceException("No TriggerPersistenceDelegate for trigger discriminator type: " + row.TriggerType);
            }

            try
            {
                triggerProps = await tDel.LoadExtendedTriggerProperties(conn, triggerKey, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                if (await IsTriggerStillPresent(conn, triggerKey, cancellationToken).ConfigureAwait(false))
                {
                    throw;
                }

                // QTZ-386 Trigger has been deleted
                return null;
            }
        }

        return BuildTrigger(triggerKey, row, triggerProps);
    }

    private async ValueTask<bool> IsTriggerStillPresent(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectTrigger));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await rs.ReadAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<JobDataMap> SelectTriggerJobDataMap(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectTriggerData));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

        using var rs = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false);
        if (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var map = await ReadMapFromReader(rs, 0).ConfigureAwait(false);
            if (map is not null)
            {
                return map;
            }
        }

        return new JobDataMap();
    }

    /// <inheritdoc />
    public virtual async ValueTask<StoredTriggerState> SelectTriggerState(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectTriggerState));

        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

        var state = (string?) await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        // No row means no trigger, which is what FromStoredValue reads a null as.
        return StoredTriggerStates.FromStoredValue(state);
    }

    /// <inheritdoc />
    public virtual async ValueTask<TriggerExecutionState> SelectTriggerStateWithExecuting(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectTriggerStateWithExecuting));

        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return TriggerExecutionState.NotFound;
        }

        // Providers disagree on the CLR type of a CASE expression, so go through Convert.
        return new TriggerExecutionState(
            StoredTriggerStates.FromStoredValue(rs.GetString(0)),
            Convert.ToInt32(rs.GetValue(1), CultureInfo.InvariantCulture) != 0);
    }

    /// <inheritdoc />
    public virtual async ValueTask<StoredTriggerHeader?> SelectTriggerHeader(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectTriggerHeader));

        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);
        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        StoredTriggerState state = StoredTriggerStates.FromStoredValue(rs.GetString(AdoConstants.ColumnTriggerState));
        object nextFireTime = rs[AdoConstants.ColumnNextFireTime];
        string jobName = rs.GetString(AdoConstants.ColumnJobName)!;
        string jobGroup = rs.GetString(AdoConstants.ColumnJobGroup)!;
        string triggerType = rs.GetString(AdoConstants.ColumnTriggerType)!;

        return new StoredTriggerHeader(
            triggerKey,
            new JobKey(jobName, jobGroup),
            state,
            GetDateTimeFromDbValue(nextFireTime),
            triggerType);
    }

    private async ValueTask<string?> SelectTriggerType(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectTriggerType));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return rs.GetString(AdoConstants.ColumnTriggerType)!;
        }
        return null;
    }

    /// <inheritdoc />
    public virtual async ValueTask<List<string>> SelectTriggerGroupNames(ConnectionAndTransactionHolder conn, GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        (string sql, string parameter) = MatchGroup(matcher, StdAdoConstants.SqlSelectTriggerGroupsEquals, StdAdoConstants.SqlSelectTriggerGroupsLike);

        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(sql));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "triggerGroup", parameter);
        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        List<string> list = [];
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(rs.GetString(0));
        }

        return list;
    }

    /// <inheritdoc />
    public virtual async ValueTask<List<TriggerKey>> SelectTriggerKeysInGroup(ConnectionAndTransactionHolder conn, GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        (string sql, string parameter) = MatchGroup(matcher, StdAdoConstants.SqlSelectTriggersInGroup, StdAdoConstants.SqlSelectTriggersInGroupLike);

        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(sql));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "triggerGroup", parameter);
        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        List<TriggerKey> keys = [];
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            keys.Add(new TriggerKey(rs.GetString(0), rs.GetString(1)));
        }

        return keys;
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> InsertPausedTriggerGroup(
        ConnectionAndTransactionHolder conn,
        string groupName,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlInsertPausedTriggerGroup));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "triggerGroup", groupName);
        int rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return rows;
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> DeletePausedTriggerGroup(
        ConnectionAndTransactionHolder conn,
        GroupMatcher<TriggerKey> matcher,
        CancellationToken cancellationToken = default)
    {
        (string sql, string parameter) = MatchGroup(matcher, StdAdoConstants.SqlDeletePausedTriggerGroupEquals, StdAdoConstants.SqlDeletePausedTriggerGroupLike);

        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(sql));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "triggerGroup", parameter);
        int rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return rows;
    }

    /// <inheritdoc />
    public virtual async ValueTask<bool> IsTriggerGroupPaused(
        ConnectionAndTransactionHolder conn,
        string groupName,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectPausedTriggerGroup));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "triggerGroup", groupName);

        return await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    protected virtual string GetSelectNextTriggerToAcquireSql(TriggerAcquisitionSqlShape shape)
    {
        // by default we don't support limits, this is db specific
        return StdAdoConstants.BuildSqlSelectNextTriggerToAcquire(shape.ExcludedJobTypeBucket);
    }

    /// <summary>
    /// Binds the preferred node (node affinity) parameters of the acquisition SQL. Parameters are
    /// added in SQL token order for providers with positional binding.
    /// </summary>
    /// <param name="cmd">The acquisition command.</param>
    /// <param name="liveNodeCutoff">
    /// Instant before which a node's last check-in is considered stale, releasing its pinned
    /// triggers to other nodes. Bound through <see cref="StdAdoDelegate.GetDbDateTimeValue" />, so
    /// the raw ticks the liveness SQL compares stay inside this binder.
    /// </param>
    protected void AddPreferredNodeParameters(DbCommand cmd, DateTimeOffset liveNodeCutoff)
    {
        AddCommandParameter(cmd, "instanceId", instanceId);
        AddCommandParameter(cmd, "autoPinSentinel", StdAdoConstants.AutoPinSentinel);
        AddCommandParameter(cmd, "liveNodeCutoff", GetDbDateTimeValue(liveNodeCutoff));
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> UpdateTriggerPreferredNodeConditional(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        PreferredNodeTransition transition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transition);

        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlUpdateTriggerPreferredNodeConditional));
        // Parameters are added in SQL token order for providers with positional binding.
        // The expected pin is compared with '=', so a transition expecting PreferredNode.None matches
        // no row: SQL equality against NULL is never true. The claim paths never expect one.
        AddCommandParameter(cmd, "triggerPreferredNode", transition.New.StoredNode);
        AddCommandParameter(cmd, "triggerPreferredNodeAuto", GetDbBooleanValue(transition.New.StoredAutomatic));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);
        AddCommandParameter(cmd, "expectedPreferredNode", transition.Expected.StoredNode);
        AddCommandParameter(cmd, "expectedPreferredNodeAuto", GetDbBooleanValue(transition.Expected.StoredAutomatic));
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> RepinTriggersFromDeadNode(
        ConnectionAndTransactionHolder conn,
        string oldPreferredNode,
        string newPreferredNode,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlRepinTriggersFromDeadNode));
        // Parameters are added in SQL token order for providers with positional binding.
        // Only auto-claimed pins are released; the reset value ("*") is not itself auto-claimed.
        AddCommandParameter(cmd, "newPreferredNode", newPreferredNode);
        AddCommandParameter(cmd, "newPreferredNodeAuto", GetDbBooleanValue(false));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "oldPreferredNode", oldPreferredNode);
        AddCommandParameter(cmd, "oldPreferredNodeAuto", GetDbBooleanValue(true));
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    protected virtual string GetCountMisfiredTriggersInStateSql()
    {
        return StdAdoConstants.SqlCountMisfiredTriggersInStates;
    }

    /// <inheritdoc />
    public virtual async ValueTask<List<TriggerAcquireResult>> SelectTriggersToAcquire(
        ConnectionAndTransactionHolder conn,
        TriggerAcquisitionCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        // we want at least one trigger back
        int maxCount = criteria.MaxCount < 1 ? 1 : criteria.MaxCount;
        List<string>? excludedJobTypeNames = criteria.ExcludedJobTypeNames is { Count: > 0 } names
            ? [.. names]
            : null;
        int excludedJobTypeBucket = StdAdoConstants.RoundUpExcludedJobTypeCount(excludedJobTypeNames?.Count ?? 0);

        string sql = acquisitionSqlByShape.GetOrAdd(
            new TriggerAcquisitionSqlShape(maxCount, excludedJobTypeBucket),
            static (shape, self) => self.ReplaceTablePrefix(self.GetSelectNextTriggerToAcquireSql(shape)),
            this);

        using var cmd = PrepareCommand(conn, sql);
        List<TriggerAcquireResult> nextTriggers = new();

        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "state", StoredTriggerState.Waiting.ToStoredValue());
        AddCommandParameter(cmd, "noLaterThan", GetDbDateTimeValue(criteria.NoLaterThan));
        AddCommandParameter(cmd, "noEarlierThan", GetDbDateTimeValue(criteria.NoEarlierThan));
        AddPreferredNodeParameters(cmd, criteria.LiveNodeCutoff);

        if (excludedJobTypeNames is not null)
        {
            for (int i = 0; i < excludedJobTypeBucket; i++)
            {
                // Pad to the bucket size by repeating the last name. A duplicate NOT IN term cannot
                // change which rows match.
                string jobTypeName = excludedJobTypeNames[Math.Min(i, excludedJobTypeNames.Count - 1)];
                AddCommandParameter(cmd, StdAdoConstants.ExcludedJobTypeParameter(i), jobTypeName);
            }
        }

        // Work on a copy: the slots are decremented as rows are taken, and the caller may reuse the
        // criteria across retries. Cluster-scoped limits arrive already lowered by what the cluster
        // holds in flight; node-scoped ones were lowered by the scheduler thread before the request.
        ExecutionSlots? executionSlots = criteria.ExecutionLimits?.CreateSlots(criteria.ClusterInFlight);

        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        int execGroupOrdinal = -1;
        int triggerNameOrdinal = -1;
        int triggerGroupOrdinal = -1;
        int jobClassOrdinal = -1;
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (nextTriggers.Count >= maxCount)
            {
                // Every dialect Quartz ships puts the batch size into the statement itself, so a row
                // beyond the batch only turns up on a delegate whose SQL cannot be row-limited - the
                // base one, or a driver delegate of somebody's own. There the result set is every
                // waiting trigger, and simply disposing the reader would make the provider drain all
                // of it off the wire. Cancelling abandons it instead.
                cmd.Cancel();
                break;
            }

            if (execGroupOrdinal < 0)
            {
                execGroupOrdinal = rs.GetOrdinal(AdoConstants.ColumnExecutionGroup);
                triggerNameOrdinal = rs.GetOrdinal(AdoConstants.ColumnTriggerName);
                triggerGroupOrdinal = rs.GetOrdinal(AdoConstants.ColumnTriggerGroup);
                jobClassOrdinal = rs.GetOrdinal(AdoConstants.ColumnJobClass);
            }

            string? executionGroup = rs.IsDBNull(execGroupOrdinal)
                ? null
                : rs.GetString(execGroupOrdinal);

            // Read before the limit check rather than after it: the limits may be configured to
            // stand in the trigger group for an execution group the trigger does not carry.
            TriggerKey triggerKey = new(
                rs.GetString(triggerNameOrdinal),
                rs.GetString(triggerGroupOrdinal));

            if (executionSlots is not null && !executionSlots.TryTake(executionGroup, triggerKey.Group))
            {
                continue; // skip this trigger, its group is at limit
            }

            nextTriggers.Add(new TriggerAcquireResult(
                triggerKey,
                rs.GetString(jobClassOrdinal),
                executionGroup));
        }

        return nextTriggers;
    }

    /// <inheritdoc />
    public virtual async ValueTask<List<ExecutionGroupInFlight>> SelectExecutionGroupsInFlight(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectExecutionGroupsInFlight));
        AddCommandParameter(cmd, "schedulerName", schedulerName);

        List<ExecutionGroupInFlight> counts = [];

        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // Read positionally: the projection is this statement's own and COUNT(*) has no column name
            // to ask for. Its type is provider-dependent - Int32 on SQL Server, Int64 on PostgreSQL,
            // MySQL and SQLite - so it is converted rather than read as either.
            counts.Add(new ExecutionGroupInFlight(
                rs.IsDBNull(0) ? null : rs.GetString(0),
                rs.GetString(1),
                Convert.ToInt32(rs.GetValue(2), CultureInfo.InvariantCulture)));
        }

        return counts;
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> InsertFiredTrigger(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        StoredTriggerState state,
        IJobDetail? job,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlInsertFiredTrigger));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "triggerEntryId", trigger.FireInstanceId);
        AddCommandParameter(cmd, "triggerName", trigger.Key.Name);
        AddCommandParameter(cmd, "triggerGroup", trigger.Key.Group);
        AddCommandParameter(cmd, "triggerInstanceName", instanceId);
        AddCommandParameter(cmd, "triggerFireTime", GetDbDateTimeValue(timeProvider.GetUtcNow()));
        AddCommandParameter(cmd, "triggerScheduledTime", GetDbDateTimeValue(trigger.NextFireTimeUtc));
        AddCommandParameter(cmd, "triggerState", state.ToStoredValue());
        if (job is not null)
        {
            AddCommandParameter(cmd, "triggerJobName", trigger.JobKey.Name);
            AddCommandParameter(cmd, "triggerJobGroup", trigger.JobKey.Group);
            AddCommandParameter(cmd, "triggerJobStateful", GetDbBooleanValue(job.ConcurrentExecutionDisallowed));
            AddCommandParameter(cmd, "triggerJobRequestsRecovery", GetDbBooleanValue(job.RequestsRecovery));
        }
        else
        {
            AddCommandParameter(cmd, "triggerJobName", null);
            AddCommandParameter(cmd, "triggerJobGroup", null);
            AddCommandParameter(cmd, "triggerJobStateful", GetDbBooleanValue(false));
            AddCommandParameter(cmd, "triggerJobRequestsRecovery", GetDbBooleanValue(false));
        }

        AddCommandParameter(cmd, "triggerPriority", trigger.Priority);
        AddCommandParameter(cmd, "triggerExecutionGroup", (object?) trigger.ExecutionGroup ?? DBNull.Value);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<List<FiredTriggerRecord>> SelectFiredTriggerRecords(
        ConnectionAndTransactionHolder conn,
        FiredTriggerQuery query,
        CancellationToken cancellationToken = default)
    {
        List<FiredTriggerRecord> records = [];

        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(BuildFiredTriggerQuerySql(StdAdoConstants.SqlSelectFiredTriggers, query)));
        BindFiredTriggerQuery(cmd, query);

        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        FiredTriggerRowOrdinals? ordinals = null;
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            ordinals ??= new FiredTriggerRowOrdinals(rs);
            records.Add(ReadFiredTriggerRecord(rs, ordinals.Value));
        }

        return records;
    }

    /// <summary>
    /// Where each column of the fired-trigger select sits. Same reasoning as
    /// <see cref="TriggerRowOrdinals" />: a cluster recovery reads every fired-trigger row there is.
    /// </summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly struct FiredTriggerRowOrdinals
    {
        public FiredTriggerRowOrdinals(DbDataReader rs)
        {
            EntryState = rs.GetOrdinal(AdoConstants.ColumnEntryState);
            EntryId = rs.GetOrdinal(AdoConstants.ColumnEntryId);
            FiredTime = rs.GetOrdinal(AdoConstants.ColumnFiredTime);
            ScheduledTime = rs.GetOrdinal(AdoConstants.ColumnScheduledTime);
            Priority = rs.GetOrdinal(AdoConstants.ColumnPriority);
            InstanceName = rs.GetOrdinal(AdoConstants.ColumnInstanceName);
            TriggerName = rs.GetOrdinal(AdoConstants.ColumnTriggerName);
            TriggerGroup = rs.GetOrdinal(AdoConstants.ColumnTriggerGroup);
            IsNonConcurrent = rs.GetOrdinal(AdoConstants.ColumnIsNonConcurrent);
            RequestsRecovery = rs.GetOrdinal(AdoConstants.ColumnRequestsRecovery);
            JobName = rs.GetOrdinal(AdoConstants.ColumnJobName);
            JobGroup = rs.GetOrdinal(AdoConstants.ColumnJobGroup);
        }

        public int EntryState { get; }
        public int EntryId { get; }
        public int FiredTime { get; }
        public int ScheduledTime { get; }
        public int Priority { get; }
        public int InstanceName { get; }
        public int TriggerName { get; }
        public int TriggerGroup { get; }
        public int IsNonConcurrent { get; }
        public int RequestsRecovery { get; }
        public int JobName { get; }
        public int JobGroup { get; }
    }

    private FiredTriggerRecord ReadFiredTriggerRecord(DbDataReader rs, FiredTriggerRowOrdinals ordinals)
    {
        StoredTriggerState state = StoredTriggerStates.FromStoredValue(ReadNullableString(rs, ordinals.EntryState));

        // An ACQUIRED row is written before the job has been loaded, so its job columns hold nothing yet.
        bool hasJob = state != StoredTriggerState.Acquired;

        return new FiredTriggerRecord
        {
            FireInstanceId = rs.GetString(ordinals.EntryId),
            FireInstanceState = state,
            FireTimestamp = GetDateTimeFromDbValue(rs.GetValue(ordinals.FiredTime)) ?? DateTimeOffset.MinValue,
            ScheduleTimestamp = GetDateTimeFromDbValue(rs.GetValue(ordinals.ScheduledTime)) ?? DateTimeOffset.MinValue,
            Priority = Convert.ToInt32(rs.GetValue(ordinals.Priority), CultureInfo.InvariantCulture),
            SchedulerInstanceId = rs.GetString(ordinals.InstanceName),
            TriggerKey = new TriggerKey(rs.GetString(ordinals.TriggerName), rs.GetString(ordinals.TriggerGroup)),
            JobDisallowsConcurrentExecution = hasJob && GetBooleanFromDbValue(rs.GetValue(ordinals.IsNonConcurrent)),
            JobRequestsRecovery = hasJob && GetBooleanFromDbValue(rs.GetValue(ordinals.RequestsRecovery)),
            JobKey = hasJob ? new JobKey(rs.GetString(ordinals.JobName), rs.GetString(ordinals.JobGroup)) : null
        };
    }

    /// <inheritdoc />
    public virtual async ValueTask<List<string>> SelectFiredTriggerInstanceNames(ConnectionAndTransactionHolder conn, CancellationToken cancellationToken = default)
    {
        List<string> instanceNames = [];
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectFiredTriggerInstanceNames));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            instanceNames.Add(rs.GetString(AdoConstants.ColumnInstanceName)!);
        }

        return instanceNames;
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> DeleteFiredTrigger(
        ConnectionAndTransactionHolder conn,
        string entryId,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlDeleteFiredTrigger));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "triggerEntryId", entryId);
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public virtual void AddTriggerPersistenceDelegate(ITriggerPersistenceDelegate persistenceDelegate)
    {
        logger.TriggerPersistenceDelegateAdded(persistenceDelegate.GetType());
        persistenceDelegate.Initialize(new TriggerPersistenceDelegateContext
        {
            SchedulerName = schedulerName,
            TablePrefix = tablePrefix,
            DbAccessor = this,
        });

        lock (triggerPersistenceDelegateLock)
        {
            ITriggerPersistenceDelegate[] registered = [.. triggerPersistenceDelegates, persistenceDelegate];

            // First registration of a discriminator wins, which is what the scan this replaces did: the
            // built-in delegates are added before the ones the container supplies.
            Dictionary<string, ITriggerPersistenceDelegate> byDiscriminator = new(registered.Length, StringComparer.Ordinal);
            foreach (ITriggerPersistenceDelegate registration in registered)
            {
                byDiscriminator.TryAdd(registration.GetHandledTriggerTypeDiscriminator(), registration);
            }

            triggerPersistenceDelegates = registered;
            triggerPersistenceDelegatesByDiscriminator = byDiscriminator.ToFrozenDictionary(StringComparer.Ordinal);
        }
    }

    protected virtual ITriggerPersistenceDelegate? FindTriggerPersistenceDelegate(IOperableTrigger trigger)
    {
        foreach (var persistenceDelegate in triggerPersistenceDelegates)
        {
            if (persistenceDelegate.CanHandleTriggerType(trigger))
            {
                return persistenceDelegate;
            }
        }

        return null;
    }

    protected virtual ITriggerPersistenceDelegate? FindTriggerPersistenceDelegate(string discriminator)
    {
        triggerPersistenceDelegatesByDiscriminator.TryGetValue(discriminator, out ITriggerPersistenceDelegate? persistenceDelegate);
        return persistenceDelegate;
    }

    /// <inheritdoc />
    public virtual async ValueTask<bool> TriggerExists(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectTriggerExistence));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

        using var dr = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await dr.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> CountTriggersForJob(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectNumTriggersForJob));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "jobName", jobKey.Name);
        AddCommandParameter(cmd, "jobGroup", jobKey.Group);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public virtual async ValueTask<List<IOperableTrigger>> SelectTriggersForJob(ConnectionAndTransactionHolder conn, JobKey jobKey, CancellationToken cancellationToken = default)
    {
        List<IOperableTrigger> trigList = [];
        List<TriggerKey> keys = [];

        using (var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectTriggersForJob)))
        {
            AddCommandParameter(cmd, "schedulerName", schedulerName);
            AddCommandParameter(cmd, "jobName", jobKey.Name);
            AddCommandParameter(cmd, "jobGroup", jobKey.Group);

            using (var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    keys.Add(new TriggerKey(rs.GetString(0), rs.GetString(1)));
                }
            }
        }

        foreach (TriggerKey triggerKey in keys)
        {
            var t = await SelectTrigger(conn, triggerKey, cancellationToken).ConfigureAwait(false);
            if (t is not null)
            {
                trigList.Add(t);
            }
        }

        return trigList;
    }

    /// <inheritdoc />
    public virtual async ValueTask<List<IOperableTrigger>> SelectTriggersForCalendar(ConnectionAndTransactionHolder conn, string calendarName, CancellationToken cancellationToken = default)
    {
        List<TriggerKey> keys = [];
        using (var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectTriggersForCalendar)))
        {
            AddCommandParameter(cmd, "schedulerName", schedulerName);
            AddCommandParameter(cmd, "calendarName", calendarName);
            using (var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    keys.Add(new TriggerKey(rs.GetString(AdoConstants.ColumnTriggerName)!, rs.GetString(AdoConstants.ColumnTriggerGroup)!));
                }
            }
        }

        List<IOperableTrigger> triggers = [];
        foreach (var key in keys)
        {
            var trigger = await SelectTrigger(conn, key, cancellationToken).ConfigureAwait(false);
            if (trigger is not null)
            {
                triggers.Add(trigger);
            }
        }
        return triggers;
    }

    /// <inheritdoc />
    public virtual async ValueTask UpdateMisfireOriginalFireTime(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        DateTimeOffset? fireTime,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlUpdateMisfireOrigFireTime));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);
        AddCommandParameter(cmd, "misfireOrigFireTime", GetDbDateTimeValue(fireTime));
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask ClearMisfireOriginalFireTime(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlUpdateMisfireOrigFireTime));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);
        AddCommandParameter(cmd, "misfireOrigFireTime", null);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask UpdateMisfiredTrigger(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        StoredTriggerState newState,
        DateTimeOffset? misfireOriginalFireTime,
        CancellationToken cancellationToken = default)
    {
        List<SqlStatement> statements = [];
        List<IOperableTrigger> blobTriggers = [];
        BuildMisfireUpdateStatements(new MisfiredTriggerUpdate(trigger, newState, misfireOriginalFireTime), statements, blobTriggers);

        await ExecuteStatements(conn, statements, cancellationToken).ConfigureAwait(false);

        foreach (var blobTrigger in blobTriggers)
        {
            await UpdateBlobTrigger(conn, blobTrigger, cancellationToken).ConfigureAwait(false);
        }
    }

    //---------------------------------------------------------------------------
    // batched misfire recovery
    //---------------------------------------------------------------------------

    /// <inheritdoc />
    public virtual async ValueTask<MisfiredTriggerBatch> SelectMisfiredTriggersToRecover(
        ConnectionAndTransactionHolder conn,
        StoredTriggerState state,
        DateTimeOffset misfireTime,
        int count,
        CancellationToken cancellationToken = default)
    {
        // Always read one past the limit so we can tell the caller whether the limit truncated the result.
        var sql = misfireRecoverySqlByCount.GetOrAdd(
            count,
            static (limit, self) => self.ReplaceTablePrefix(self.GetSelectMisfiredTriggersToRecoverSql(limit != -1 ? limit + 1 : limit)),
            this);

        List<TriggerKey> keys = [];
        List<TriggerRow> rows = [];
        bool hasMore = false;

        using (var cmd = PrepareCommand(conn, sql))
        {
            AddCommandParameter(cmd, "schedulerName", schedulerName);
            AddCommandParameter(cmd, "nextFireTime", GetDbDateTimeValue(misfireTime));
            AddCommandParameter(cmd, "state", state.ToStoredValue());

            using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            TriggerRowOrdinals? ordinals = null;
            while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (count != -1 && keys.Count == count)
                {
                    hasMore = true;
                    break;
                }

                ordinals ??= new TriggerRowOrdinals(rs, includesKey: true);
                keys.Add(ordinals.Value.ReadKey(rs));
                rows.Add(await ReadTriggerRow(rs, ordinals.Value).ConfigureAwait(false));
            }
        }

        var built = await MaterializeTriggers(conn, keys, rows, cancellationToken).ConfigureAwait(false);

        List<IOperableTrigger> triggers = new(rows.Count);
        for (var i = 0; i < built.Length; i++)
        {
            if (built[i] is not null)
            {
                triggers.Add(built[i]!);
            }
            else
            {
                logger.MisfiredTriggerHasNoTypeRow(keys[i], rows[i].TriggerType);
            }
        }

        return new MisfiredTriggerBatch(triggers, hasMore);
    }

    /// <inheritdoc />
    public virtual async ValueTask<List<IOperableTrigger>> SelectTriggers(
        ConnectionAndTransactionHolder conn,
        IReadOnlyCollection<TriggerKey> triggerKeys,
        CancellationToken cancellationToken = default)
    {
        if (triggerKeys.Count == 0)
        {
            return [];
        }

        // A repeated key would come back as a repeated row, and the predicate is a disjunction that
        // cannot tell the difference, so fold duplicates away before building it.
        List<TriggerKey> requested = Deduplicate(triggerKeys);

        List<TriggerKey> keys = new(requested.Count);
        List<TriggerRow> rows = new(requested.Count);

        for (int offset = 0; offset < requested.Count; offset += AdoUtil.MaxTriggerKeysPerPredicate)
        {
            int length = Math.Min(AdoUtil.MaxTriggerKeysPerPredicate, requested.Count - offset);

            using DbCommand cmd = PrepareTriggerKeySetCommand(conn, StdAdoConstants.SqlSelectTriggersByKeysPrefix, requested, offset, length, qualified: true);
            using DbDataReader rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            TriggerRowOrdinals? ordinals = null;
            while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                ordinals ??= new TriggerRowOrdinals(rs, includesKey: true);
                keys.Add(ordinals.Value.ReadKey(rs));
                rows.Add(await ReadTriggerRow(rs, ordinals.Value).ConfigureAwait(false));
            }
        }

        IOperableTrigger?[] built = await MaterializeTriggers(conn, keys, rows, cancellationToken).ConfigureAwait(false);

        List<IOperableTrigger> triggers = new(built.Length);
        foreach (IOperableTrigger? trigger in built)
        {
            // A null slot means the type-specific row was missing, so that trigger no longer exists as
            // far as this read is concerned — the same as a key that matched no TRIGGERS row at all.
            if (trigger is not null)
            {
                triggers.Add(trigger);
            }
        }

        SortByRequestedOrder(triggers, requested, static trigger => trigger.Key);
        return triggers;
    }

    /// <summary>
    /// Turns a batch of trigger rows into triggers, resolving the extended properties of each trigger
    /// type in as few queries as that type's storage allows.
    /// </summary>
    /// <returns>
    /// Triggers index-aligned with <paramref name="rows" />. A null slot means the type-specific row was
    /// missing — the trigger was deleted concurrently (QTZ-386) — and the caller decides what to do
    /// about it.
    /// </returns>
    private async ValueTask<IOperableTrigger?[]> MaterializeTriggers(
        ConnectionAndTransactionHolder conn,
        List<TriggerKey> keys,
        List<TriggerRow> rows,
        CancellationToken cancellationToken)
    {
        // Slots stay index-aligned with keys/rows while the follow-up queries fill them in.
        var built = new IOperableTrigger?[rows.Count];
        List<TriggerKey>? blobKeys = null;
        List<TriggerKey>? simpropKeys = null;
        Dictionary<ITriggerPersistenceDelegate, List<TriggerKey>>? customKeys = null;

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];

            // SIMPLE and CRON triggers came back complete from the joined row.
            if (row.Props is not null)
            {
                built[i] = BuildTrigger(keys[i], row, row.Props);
                continue;
            }

            if (row.TriggerType == AdoConstants.TriggerTypeBlob)
            {
                (blobKeys ??= []).Add(keys[i]);
                continue;
            }

            var tDel = FindTriggerPersistenceDelegate(row.TriggerType);
            if (tDel is null)
            {
                Throw.JobPersistenceException("No TriggerPersistenceDelegate for trigger discriminator type: " + row.TriggerType);
            }

            if (tDel is SimplePropertiesTriggerPersistenceDelegateBase)
            {
                (simpropKeys ??= []).Add(keys[i]);
                continue;
            }

            // A custom persistence delegate storing into its own table. It decides how well it batches;
            // the default is one read per key.
            customKeys ??= new Dictionary<ITriggerPersistenceDelegate, List<TriggerKey>>();
            if (!customKeys.TryGetValue(tDel!, out var keysForDelegate))
            {
                keysForDelegate = [];
                customKeys[tDel!] = keysForDelegate;
            }

            keysForDelegate.Add(keys[i]);
        }

        if (blobKeys is not null)
        {
            await SelectBlobTriggersForBatch(conn, keys, rows, built, blobKeys, cancellationToken).ConfigureAwait(false);
        }

        if (simpropKeys is not null)
        {
            await SelectSimpropTriggersForBatch(conn, keys, rows, built, simpropKeys, cancellationToken).ConfigureAwait(false);
        }

        if (customKeys is not null)
        {
            var slotByKey = BuildSlotLookup(keys);
            foreach (var entry in customKeys)
            {
                var bundles = await entry.Key.LoadExtendedTriggerProperties(conn, entry.Value, cancellationToken).ConfigureAwait(false);
                foreach (var bundle in bundles)
                {
                    if (slotByKey.TryGetValue(bundle.Key, out var slot))
                    {
                        built[slot] = BuildTrigger(bundle.Key, rows[slot], bundle.Value);
                    }
                }
            }
        }

        return built;
    }

    /// <summary>
    /// Prepares a statement matching a chunk of trigger keys, by appending the parameterized key-set
    /// predicate to <paramref name="sqlPrefix" />.
    /// </summary>
    private DbCommand PrepareTriggerKeySetCommand(
        ConnectionAndTransactionHolder conn,
        string sqlPrefix,
        List<TriggerKey> keys,
        int offset,
        int length,
        bool qualified = false)
    {
        var paddedCount = AdoUtil.RoundUpTriggerKeyCount(length);
        var cmd = PrepareCommand(conn, ReplaceTablePrefix(sqlPrefix + AdoUtil.BuildTriggerKeyPredicate(paddedCount, qualified)));
        AddCommandParameter(cmd, "schedulerName", schedulerName);

        for (var i = 0; i < paddedCount; i++)
        {
            // Pad up to the bucket size by repeating the chunk's last key. The predicate is a
            // disjunction, so a repeated term cannot change which rows match.
            var key = keys[offset + Math.Min(i, length - 1)];
            AddCommandParameter(cmd, AdoUtil.TriggerKeyNameParameter(i), key.Name);
            AddCommandParameter(cmd, AdoUtil.TriggerKeyGroupParameter(i), key.Group);
        }

        return cmd;
    }

    private async ValueTask SelectBlobTriggersForBatch(
        ConnectionAndTransactionHolder conn,
        List<TriggerKey> keys,
        List<TriggerRow> rows,
        IOperableTrigger?[] built,
        List<TriggerKey> blobKeys,
        CancellationToken cancellationToken)
    {
        var slotByKey = BuildSlotLookup(keys);

        for (var offset = 0; offset < blobKeys.Count; offset += AdoUtil.MaxTriggerKeysPerPredicate)
        {
            var length = Math.Min(AdoUtil.MaxTriggerKeysPerPredicate, blobKeys.Count - offset);

            using var cmd = PrepareTriggerKeySetCommand(conn, StdAdoConstants.SqlSelectBlobTriggersByKeysPrefix, blobKeys, offset, length);
            using var rs = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false);
            while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                // Sequential access: the blob is selected first, the key columns after it.
                var trigger = await GetObjectFromBlob<IOperableTrigger>(rs, 0, cancellationToken).ConfigureAwait(false);
                var key = new TriggerKey(rs.GetString(1), rs.GetString(2));

                if (trigger is not null && slotByKey.TryGetValue(key, out var slot))
                {
                    ApplyBlobTriggerRowState(trigger, rows[slot]);
                    built[slot] = trigger;
                }
            }
        }
    }

    private async ValueTask SelectSimpropTriggersForBatch(
        ConnectionAndTransactionHolder conn,
        List<TriggerKey> keys,
        List<TriggerRow> rows,
        IOperableTrigger?[] built,
        List<TriggerKey> simpropKeys,
        CancellationToken cancellationToken)
    {
        var slotByKey = BuildSlotLookup(keys);

        for (var offset = 0; offset < simpropKeys.Count; offset += AdoUtil.MaxTriggerKeysPerPredicate)
        {
            var length = Math.Min(AdoUtil.MaxTriggerKeysPerPredicate, simpropKeys.Count - offset);

            using var cmd = PrepareTriggerKeySetCommand(conn, StdAdoConstants.SqlSelectSimpropTriggersByKeysPrefix, simpropKeys, offset, length);
            using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var key = new TriggerKey(rs.GetString(AdoConstants.ColumnTriggerName)!, rs.GetString(AdoConstants.ColumnTriggerGroup)!);
                if (!slotByKey.TryGetValue(key, out var slot))
                {
                    continue;
                }

                // All simple-properties types share this table, so the delegate to read the row with is
                // the one matching that trigger's own discriminator.
                var tDel = FindTriggerPersistenceDelegate(rows[slot].TriggerType)!;
                built[slot] = BuildTrigger(key, rows[slot], tDel.ReadTriggerPropertyBundle(rs));
            }
        }
    }

    private static Dictionary<TriggerKey, int> BuildSlotLookup(List<TriggerKey> keys)
    {
        var slotByKey = new Dictionary<TriggerKey, int>(keys.Count);
        for (var i = 0; i < keys.Count; i++)
        {
            slotByKey[keys[i]] = i;
        }

        return slotByKey;
    }

    /// <summary>
    /// Builds the statements one misfire update needs. Single source of truth for
    /// <see cref="UpdateMisfiredTrigger" /> and <see cref="UpdateMisfiredTriggers" />.
    /// </summary>
    /// <param name="update">The pending update.</param>
    /// <param name="into">Statements to execute, appended to.</param>
    /// <param name="blobTriggers">
    /// Blob-stored triggers needing re-serialization, appended to. Those are written through the
    /// <see cref="UpdateBlobTrigger" /> virtual rather than inlined as a statement here, so that
    /// subclasses overriding it keep control of how blobs are persisted.
    /// </param>
    private void BuildMisfireUpdateStatements(
        in MisfiredTriggerUpdate update,
        List<SqlStatement> into,
        List<IOperableTrigger> blobTriggers)
    {
        var trigger = update.Trigger;

        // Narrow UPDATE: only columns that change during misfire recovery.
        // Only include MISFIRE_ORIG_FIRE_TIME when we have a value to write;
        // null means "leave unchanged" (matches DoUpdateOfMisfiredTrigger which
        // only calls UpdateMisfireOriginalFireTime on fire-now detection).
        bool writeMisfireOrigFireTime = update.MisfireOriginalFireTime.HasValue;

        List<SqlStatementParameter> parameters =
        [
            new("schedulerName", schedulerName),
            new("triggerNextFireTime", GetDbDateTimeValue(trigger.NextFireTimeUtc)),
            new("triggerPreviousFireTime", GetDbDateTimeValue(trigger.PreviousFireTimeUtc)),
            new("triggerState", update.NewState.ToStoredValue()),
            new("triggerStartTime", GetDbDateTimeValue(trigger.StartTimeUtc))
        ];

        if (writeMisfireOrigFireTime)
        {
            parameters.Add(new SqlStatementParameter("triggerMisfireOrigFireTime", GetDbDateTimeValue(update.MisfireOriginalFireTime)));
        }

        parameters.Add(new SqlStatementParameter("triggerName", trigger.Key.Name));
        parameters.Add(new SqlStatementParameter("triggerGroup", trigger.Key.Group));

        into.Add(new SqlStatement(
            ReplaceTablePrefix(writeMisfireOrigFireTime ? StdAdoConstants.SqlUpdateTriggerMisfireWithOrigFireTime : StdAdoConstants.SqlUpdateTriggerMisfire),
            parameters));

        // Update type-specific table: SimpleTrigger may have modified RepeatCount/TimesTriggered
        // via RescheduleNowWith* policies; blob triggers need re-serialization to persist all
        // in-memory changes. Other built-in types (Cron, CalendarInterval, DailyTimeInterval)
        // do not change extended properties during misfire.
        var persistenceDelegate = FindTriggerPersistenceDelegate(trigger);
        if (trigger is ISimpleTrigger simpleTrigger && persistenceDelegate is not null)
        {
            into.Add(new SqlStatement(ReplaceTablePrefix(StdAdoConstants.SqlUpdateSimpleTrigger),
            [
                new SqlStatementParameter("schedulerName", schedulerName),
                new SqlStatementParameter("triggerRepeatCount", simpleTrigger.RepeatCount),
                new SqlStatementParameter("triggerRepeatInterval", GetDbTimeSpanValue(simpleTrigger.RepeatInterval)),
                new SqlStatementParameter("triggerTimesTriggered", simpleTrigger.TimesTriggered),
                new SqlStatementParameter("triggerName", trigger.Key.Name),
                new SqlStatementParameter("triggerGroup", trigger.Key.Group)
            ]));
        }
        else if (persistenceDelegate is null)
        {
            // Blob-stored trigger: re-serialize to persist all in-memory misfire changes.
            blobTriggers.Add(trigger);
        }
    }

    /// <inheritdoc />
    public virtual async ValueTask UpdateMisfiredTriggers(
        ConnectionAndTransactionHolder conn,
        IReadOnlyList<MisfiredTriggerUpdate> updates,
        CancellationToken cancellationToken = default)
    {
        if (updates.Count == 0)
        {
            return;
        }

        List<SqlStatement> statements = [];
        List<IOperableTrigger> blobTriggers = [];
        foreach (var update in updates)
        {
            BuildMisfireUpdateStatements(update, statements, blobTriggers);
        }

        await ExecuteStatements(conn, statements, cancellationToken).ConfigureAwait(false);

        foreach (var blobTrigger in blobTriggers)
        {
            await UpdateBlobTrigger(conn, blobTrigger, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Issues a list of statements in as few round trips as the provider allows.
    /// </summary>
    /// <remarks>
    /// Providers that cannot batch report <see cref="ConnectionAndTransactionHolder.CanCreateBatch" />
    /// as <see langword="false" /> (the <see cref="DbConnection" /> default) and get exactly the
    /// behaviour they had before batching existed: one command per statement.
    /// </remarks>
    private async ValueTask ExecuteStatements(
        ConnectionAndTransactionHolder conn,
        List<SqlStatement> statements,
        CancellationToken cancellationToken)
    {
        if (!conn.CanCreateBatch)
        {
            await ExecuteStatementsIndividually(conn, statements, 0, statements.Count, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Callers can hand this an unbounded list — misfire recovery runs with no limit when
        // maxMisfiresToHandleAtATime is -1 — so cap how much goes into one batch rather than handing
        // the provider an arbitrarily large one.
        for (var offset = 0; offset < statements.Count; offset += MaxStatementsPerBatch)
        {
            var length = Math.Min(MaxStatementsPerBatch, statements.Count - offset);
            await ExecuteStatementBatch(conn, statements, offset, length, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Statements put into a single <see cref="DbBatch" />. Keeps one batch's size — and the amount
    /// re-run individually if it fails — bounded.
    /// </summary>
    private const int MaxStatementsPerBatch = 100;

    private async ValueTask ExecuteStatementBatch(
        ConnectionAndTransactionHolder conn,
        List<SqlStatement> statements,
        int offset,
        int length,
        CancellationToken cancellationToken)
    {
        try
        {
            using var batch = conn.CreateBatch();

            // A batch is not prepared through AdoUtil, so the configured command timeout has to be
            // applied here as well; otherwise this one round-trip would be the only statement the store
            // issues that can outlive it.
            if (adoUtil.CommandTimeoutSeconds is { } timeoutSeconds)
            {
                batch.Timeout = timeoutSeconds;
            }

            // Providers are not required to implement DbBatchCommand.CreateParameter, so keep one
            // throwaway command around to mint parameter instances for those that do not. Asked of the
            // connection rather than of the provider, because a driver reached through a factory or a
            // data source describes no command type for the provider to construct one from.
            using var parameterFactory = conn.Connection.CreateCommand();

            for (var i = offset; i < offset + length; i++)
            {
                var statement = statements[i];
                var batchCommand = batch.CreateBatchCommand();
                // A batch command never passes through PrepareCommand, so the driver's parameter
                // spelling has to be applied to its text here.
                batchCommand.CommandText = adoUtil.RewriteParameterNames(statement.Sql);
                foreach (var parameter in statement.Parameters)
                {
                    adoUtil.AddCommandParameter(batchCommand, parameterFactory, parameter.Name, parameter.Value, parameter.DataType);
                }

                batch.BatchCommands.Add(batchCommand);
            }

            await batch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException && !TransientErrorDetector.IsTransient(e))
        {
            // A batch fails as a unit, which would let one bad statement block the whole pass. Retry
            // statement by statement so the others still get through, and so the exception that
            // surfaces names the statement that actually failed.
            //
            // Not for a transient failure, though. The caller's retry is the answer to those, and it
            // only recognises them from the exception it is handed: replaying against a connection that
            // just dropped — or a transaction the server has already doomed, which is what Postgres does
            // to every statement after the first error — surfaces some later, unrecognisable failure
            // instead, and a retryable operation stops being retried.
            logger.BatchedStatementExecutionFailed(length, e);
            await ExecuteStatementsIndividually(conn, statements, offset, length, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask ExecuteStatementsIndividually(
        ConnectionAndTransactionHolder conn,
        List<SqlStatement> statements,
        int offset,
        int length,
        CancellationToken cancellationToken)
    {
        for (var i = offset; i < offset + length; i++)
        {
            var statement = statements[i];
            using var cmd = PrepareCommand(conn, statement.Sql);
            foreach (var parameter in statement.Parameters)
            {
                AddCommandParameter(cmd, parameter.Name, parameter.Value, parameter.DataType);
            }

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
