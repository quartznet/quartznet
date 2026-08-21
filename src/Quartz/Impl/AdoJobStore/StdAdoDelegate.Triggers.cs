using System.Collections;
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
                    SimpleTriggerImpl rcvryTrig = new SimpleTriggerImpl
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
        AddCommandParameter(cmd, "triggerJobJobDataMap", jobData, DbProvider.Metadata.DbBinaryType);

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
        AddCommandParameter(cmd, "blob", buf, DbProvider.Metadata.DbBinaryType);

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

        // save some clock cycles by unnecessarily writing job data blob ...
        var updateJobData = trigger.JobDataMap.Dirty;
        var jobData = updateJobData ? SerializeJobData(trigger.JobDataMap) : null;

        // Only write the preferred node columns when the pin was actually changed on this instance.
        // A trigger on the fire path carries the value loaded at acquire time; writing it back
        // would clobber a concurrent re-pin (ClusterRecover's failover reset, UpdateTriggerDetails).
        bool writePreferredNode = (trigger as TriggerBase)?.PreferredNodeDirty == true;

        string sqlUpdate = (updateJobData, writePreferredNode) switch
        {
            (true, true) => StdAdoConstants.SqlUpdateTriggerWithPreferredNode,
            (true, false) => StdAdoConstants.SqlUpdateTrigger,
            (false, true) => StdAdoConstants.SqlUpdateTriggerSkipDataWithPreferredNode,
            (false, false) => StdAdoConstants.SqlUpdateTriggerSkipData,
        };
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(sqlUpdate));

        AddCommandParameter(cmd, "schedulerName", schedulerName);
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
        AddCommandParameter(cmd, "triggerPriority", trigger.Priority);

        const string JobDataMapParameter = "triggerJobJobDataMap";
        if (updateJobData)
        {
            AddCommandParameter(cmd, JobDataMapParameter, jobData, DbProvider.Metadata.DbBinaryType);
        }

        string? execGroup = trigger.ExecutionGroup;
        AddCommandParameter(cmd, "triggerExecutionGroup", (object?) execGroup ?? DBNull.Value);

        // Parameters are added in SQL token order for providers with positional binding
        if (writePreferredNode)
        {
            PreferredNode preferredNode = trigger.PreferredNode;
            AddCommandParameter(cmd, "triggerPreferredNode", (object?) preferredNode.StoredNode ?? DBNull.Value);
            AddCommandParameter(cmd, "triggerPreferredNodeAuto", GetDbBooleanValue(preferredNode.StoredAutomatic));
        }

        AddCommandParameter(cmd, "triggerName", trigger.Key.Name);
        AddCommandParameter(cmd, "triggerGroup", trigger.Key.Group);

        var updateResult = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

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
        }
        else
        {
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

        return updateResult;
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
        AddCommandParameter(cmd, "blob", os, DbProvider.Metadata.DbBinaryType);
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
    /// Reads the current row of a trigger select. Shared by the single-trigger and batch read paths so
    /// the two cannot drift apart.
    /// </summary>
    private async ValueTask<TriggerRow> ReadTriggerRow(DbDataReader rs)
    {
        var row = new TriggerRow
        {
            JobName = rs.GetString(AdoConstants.ColumnJobName)!,
            JobGroup = rs.GetString(AdoConstants.ColumnJobGroup)!,
            Description = rs.GetString(AdoConstants.ColumnDescription),
            TriggerType = rs.GetString(AdoConstants.ColumnTriggerType)!,
            CalendarName = rs.GetString(AdoConstants.ColumnCalendarName),
            MisfireInstruction = rs.GetInt32(AdoConstants.ColumnMisfireInstruction),
            Priority = rs.GetInt32(AdoConstants.ColumnPriority)
        };

        row.JobDataMap = await ReadMapFromReader(rs, 11).ConfigureAwait(false);

        row.NextFireTimeUtc = GetDateTimeFromDbValue(rs[AdoConstants.ColumnNextFireTime]);
        row.PreviousFireTimeUtc = GetDateTimeFromDbValue(rs[AdoConstants.ColumnPreviousFireTime]);
        row.StartTimeUtc = GetDateTimeFromDbValue(rs[AdoConstants.ColumnStartTime]) ?? DateTimeOffset.MinValue;
        row.EndTimeUtc = GetDateTimeFromDbValue(rs[AdoConstants.ColumnEndTime]);

        // check if we access fast path
        if (row.TriggerType is AdoConstants.TriggerTypeCron or AdoConstants.TriggerTypeSimple)
        {
            row.Props = FindTriggerPersistenceDelegate(row.TriggerType)!.ReadTriggerPropertyBundle(rs);
        }

        row.MisfireOriginalFireTime = GetDateTimeFromDbValue(rs[AdoConstants.ColumnMisfireOriginalFireTime]);

        int execGroupOrdinal = rs.GetOrdinal(AdoConstants.ColumnExecutionGroup);
        row.ExecutionGroup = rs.IsDBNull(execGroupOrdinal) ? null : rs.GetString(execGroupOrdinal);

        int preferredNodeOrdinal = rs.GetOrdinal(AdoConstants.ColumnPreferredNode);
        row.PreferredNode = rs.IsDBNull(preferredNodeOrdinal) ? null : rs.GetString(preferredNodeOrdinal);
        int preferredNodeAutoOrdinal = rs.GetOrdinal(AdoConstants.ColumnPreferredNodeAuto);
        row.PreferredNodeAuto = !rs.IsDBNull(preferredNodeAutoOrdinal) && GetBooleanFromDbValue(rs.GetValue(preferredNodeAutoOrdinal));

        return row;
    }

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
    private static void ApplyBlobTriggerRowState(IOperableTrigger trigger, TriggerRow row)
    {
        ApplyTriggerFireState(trigger, row);
        ApplyTriggerRoutingState(trigger, row);
    }

    /// <summary>
    /// Builds a non-blob trigger from its TRIGGERS row and its type-specific extended properties.
    /// </summary>
    private static IOperableTrigger BuildTrigger(TriggerKey triggerKey, TriggerRow row, TriggerPropertyBundle triggerProps)
    {
        var tb = TriggerBuilder.Create()
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

            row = await ReadTriggerRow(rs).ConfigureAwait(false);
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

        return new StoredTriggerHeader(
            triggerKey,
            new JobKey(jobName, jobGroup),
            state,
            GetDateTimeFromDbValue(nextFireTime));
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

    protected virtual string GetSelectNextTriggerToAcquireSql(int maxCount)
    {
        // by default we don't support limits, this is db specific
        return StdAdoConstants.SqlSelectNextTriggerToAcquire;
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

        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(GetSelectNextTriggerToAcquireSql(maxCount)));
        List<TriggerAcquireResult> nextTriggers = new();

        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "state", StoredTriggerState.Waiting.ToStoredValue());
        AddCommandParameter(cmd, "noLaterThan", GetDbDateTimeValue(criteria.NoLaterThan));
        AddCommandParameter(cmd, "noEarlierThan", GetDbDateTimeValue(criteria.NoEarlierThan));
        AddPreferredNodeParameters(cmd, criteria.LiveNodeCutoff);

        // Work on a copy: the slots are decremented as rows are taken, and the caller may reuse the
        // criteria across retries.
        ExecutionSlots? executionSlots = criteria.ExecutionLimits?.CreateSlots();

        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        // signal cancel, otherwise ADO.NET might have trouble handling partial reads from open reader
        int execGroupOrdinal = -1;
        var shouldStop = false;
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (shouldStop)
            {
                cmd.Cancel();
                break;
            }

            if (execGroupOrdinal < 0)
            {
                execGroupOrdinal = rs.GetOrdinal(AdoConstants.ColumnExecutionGroup);
            }

            if (nextTriggers.Count < maxCount)
            {
                string? executionGroup = rs.IsDBNull(execGroupOrdinal)
                    ? null
                    : rs.GetString(execGroupOrdinal);

                if (executionSlots is not null && !executionSlots.TryTake(executionGroup))
                {
                    continue; // skip this trigger, its group is at limit
                }

                var result = new TriggerAcquireResult(
                    new TriggerKey((string) rs[AdoConstants.ColumnTriggerName], (string) rs[AdoConstants.ColumnTriggerGroup]),
                    (string) rs[AdoConstants.ColumnJobClass],
                    executionGroup);
                nextTriggers.Add(result);
            }
            else
            {
                shouldStop = true;
            }
        }

        return nextTriggers;
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

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> UpdateFiredTrigger(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        StoredTriggerState state,
        IJobDetail job,
        CancellationToken cancellationToken = default)
    {
        var ps = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlUpdateFiredTrigger));
        AddCommandParameter(ps, "schedulerName", schedulerName);
        AddCommandParameter(ps, "instanceName", instanceId);
        AddCommandParameter(ps, "firedTime", GetDbDateTimeValue(timeProvider.GetUtcNow()));
        AddCommandParameter(ps, "scheduledTime", GetDbDateTimeValue(trigger.NextFireTimeUtc));
        AddCommandParameter(ps, "entryState", state.ToStoredValue());
        AddCommandParameter(ps, "jobName", trigger.JobKey.Name);
        AddCommandParameter(ps, "jobGroup", trigger.JobKey.Group);
        AddCommandParameter(ps, "isNonConcurrent", GetDbBooleanValue(job.ConcurrentExecutionDisallowed));
        AddCommandParameter(ps, "requestsRecover", GetDbBooleanValue(job.RequestsRecovery));
        AddCommandParameter(ps, "entryId", trigger.FireInstanceId);

        return await ps.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            records.Add(ReadFiredTriggerRecord(rs));
        }

        return records;
    }

    private FiredTriggerRecord ReadFiredTriggerRecord(DbDataReader rs)
    {
        StoredTriggerState state = StoredTriggerStates.FromStoredValue(rs.GetString(AdoConstants.ColumnEntryState));

        // An ACQUIRED row is written before the job has been loaded, so its job columns hold nothing yet.
        bool hasJob = state != StoredTriggerState.Acquired;

        return new FiredTriggerRecord
        {
            FireInstanceId = rs.GetString(AdoConstants.ColumnEntryId)!,
            FireInstanceState = state,
            FireTimestamp = GetDateTimeFromDbValue(rs[AdoConstants.ColumnFiredTime]) ?? DateTimeOffset.MinValue,
            ScheduleTimestamp = GetDateTimeFromDbValue(rs[AdoConstants.ColumnScheduledTime]) ?? DateTimeOffset.MinValue,
            Priority = Convert.ToInt32(rs[AdoConstants.ColumnPriority], CultureInfo.InvariantCulture),
            SchedulerInstanceId = rs.GetString(AdoConstants.ColumnInstanceName)!,
            TriggerKey = new TriggerKey(rs.GetString(AdoConstants.ColumnTriggerName)!, rs.GetString(AdoConstants.ColumnTriggerGroup)!),
            JobDisallowsConcurrentExecution = hasJob && GetBooleanFromDbValue(rs[AdoConstants.ColumnIsNonConcurrent]),
            JobRequestsRecovery = hasJob && GetBooleanFromDbValue(rs[AdoConstants.ColumnRequestsRecovery]),
            JobKey = hasJob ? new JobKey(rs.GetString(AdoConstants.ColumnJobName)!, rs.GetString(AdoConstants.ColumnJobGroup)!) : null
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
        logger.LogDebug("Adding TriggerPersistenceDelegate of type: {Type}", persistenceDelegate.GetType());
        persistenceDelegate.Initialize(new TriggerPersistenceDelegateContext
        {
            SchedulerName = schedulerName,
            TablePrefix = tablePrefix,
            DbAccessor = this,
        });
        triggerPersistenceDelegates.Add(persistenceDelegate);
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
        foreach (var persistenceDelegate in triggerPersistenceDelegates)
        {
            if (persistenceDelegate.GetHandledTriggerTypeDiscriminator() == discriminator)
            {
                return persistenceDelegate;
            }
        }

        return null;
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

        await ExecuteStatementsIndividually(conn, statements, 0, statements.Count, cancellationToken).ConfigureAwait(false);

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
        var sql = ReplaceTablePrefix(GetSelectMisfiredTriggersToRecoverSql(count != -1 ? count + 1 : count));

        List<TriggerKey> keys = [];
        List<TriggerRow> rows = [];
        bool hasMore = false;

        using (var cmd = PrepareCommand(conn, sql))
        {
            AddCommandParameter(cmd, "schedulerName", schedulerName);
            AddCommandParameter(cmd, "nextFireTime", GetDbDateTimeValue(misfireTime));
            AddCommandParameter(cmd, "state", state.ToStoredValue());

            using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (count != -1 && keys.Count == count)
                {
                    hasMore = true;
                    break;
                }

                keys.Add(new TriggerKey(rs.GetString(AdoConstants.ColumnTriggerName)!, rs.GetString(AdoConstants.ColumnTriggerGroup)!));
                rows.Add(await ReadTriggerRow(rs).ConfigureAwait(false));
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
                logger.LogWarning("Misfired trigger '{TriggerKey}' has no {TriggerType} row and is skipped", keys[i], rows[i].TriggerType);
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
            while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                keys.Add(new TriggerKey(rs.GetString(AdoConstants.ColumnTriggerName)!, rs.GetString(AdoConstants.ColumnTriggerGroup)!));
                rows.Add(await ReadTriggerRow(rs).ConfigureAwait(false));
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
    /// A statement and its parameters, kept as data so that the same definition can be issued either as a
    /// standalone command or as one command inside a <see cref="DbBatch" />.
    /// </summary>
    private readonly record struct SqlStatement(string Sql, List<SqlStatementParameter> Parameters);

    private readonly record struct SqlStatementParameter(string Name, object? Value, Enum? DataType = null);

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

        // Providers that cannot batch report CanCreateBatch = false (the DbConnection default), and get
        // exactly the behaviour they had before batching existed.
        if (!conn.CanCreateBatch)
        {
            await ExecuteStatementsIndividually(conn, statements, 0, statements.Count, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Recovery runs unbounded (maxMisfiresToHandleAtATime is -1), so cap how much goes into one
            // batch rather than handing the provider an arbitrarily large one.
            for (var offset = 0; offset < statements.Count; offset += MaxStatementsPerBatch)
            {
                var length = Math.Min(MaxStatementsPerBatch, statements.Count - offset);
                await ExecuteStatementBatch(conn, statements, offset, length, cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (var blobTrigger in blobTriggers)
        {
            await UpdateBlobTrigger(conn, blobTrigger, cancellationToken).ConfigureAwait(false);
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
            // throwaway command around to mint parameter instances for those that do not.
            using var parameterFactory = DbProvider.CreateCommand();

            for (var i = offset; i < offset + length; i++)
            {
                var statement = statements[i];
                var batchCommand = batch.CreateBatchCommand();
                batchCommand.CommandText = statement.Sql;
                foreach (var parameter in statement.Parameters)
                {
                    adoUtil.AddCommandParameter(batchCommand, parameterFactory, parameter.Name, parameter.Value, parameter.DataType);
                }

                batch.BatchCommands.Add(batchCommand);
            }

            await batch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // A batch fails as a unit, which would let one bad trigger block the whole recovery pass.
            // Retry statement by statement so the others still get through, and so the exception that
            // surfaces names the statement that actually failed.
            logger.LogWarning(e, "Batched misfire update failed, retrying {StatementCount} statement(s) individually", length);
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
