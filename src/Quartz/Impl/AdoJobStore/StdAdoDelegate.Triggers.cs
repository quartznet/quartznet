using System.Collections;
using System.Data.Common;
using System.Globalization;

using Microsoft.Extensions.Logging;

using Quartz.Impl.Matchers;
using Quartz.Impl.Triggers;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Impl.AdoJobStore;

public partial class StdAdoDelegate
{
    /// <inheritdoc />
    public virtual async ValueTask<int> UpdateTriggerStatesFromOtherStates(
        ConnectionAndTransactionHolder conn,
        string newState,
        string oldState1,
        string oldState2,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateTriggerStatesFromOtherStates));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "newState", newState);
        AddCommandParameter(cmd, "oldState1", oldState1);
        AddCommandParameter(cmd, "oldState2", oldState2);
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<List<TriggerKey>> SelectMisfiredTriggers(ConnectionAndTransactionHolder conn, DateTimeOffset ts, CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectMisfiredTriggers));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "timestamp", GetDbDateTimeValue(ts));
        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        List<TriggerKey> list = [];
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string triggerName = rs.GetString(ColumnTriggerName)!;
            string groupName = rs.GetString(ColumnTriggerGroup)!;
            list.Add(new TriggerKey(triggerName, groupName));
        }

        return list;
    }

    /// <inheritdoc />
    public virtual async ValueTask<List<TriggerKey>> SelectTriggersInState(ConnectionAndTransactionHolder conn, string state, CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggersInState));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "state", state);
        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        List<TriggerKey> list = [];
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new TriggerKey(rs.GetString(0), rs.GetString(1)));
        }

        return list;
    }

    /// <inheritdoc />
    public virtual async ValueTask<List<TriggerKey>> HasMisfiredTriggersInState(ConnectionAndTransactionHolder conn,
        string state,
        DateTimeOffset ts,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectMisfiredTriggersInState));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "timestamp", GetDbDateTimeValue(ts));
        AddCommandParameter(cmd, "state", state);

        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        List<TriggerKey> list = [];
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string triggerName = rs.GetString(ColumnTriggerName)!;
            string groupName = rs.GetString(ColumnTriggerGroup)!;
            list.Add(new TriggerKey(triggerName, groupName));
        }

        return list;
    }

    /// <inheritdoc />
    public virtual async ValueTask<bool> HasMisfiredTriggersInState(
        ConnectionAndTransactionHolder conn,
        string state1,
        DateTimeOffset ts,
        int count,
        ICollection<TriggerKey> resultList,
        CancellationToken cancellationToken = default)
    {
        // always take one more than count so that hasReachedLimit will work properly
        var sql = ReplaceTablePrefix(GetSelectNextMisfiredTriggersInStateToAcquireSql(count != -1 ? count + 1 : count));
        using var cmd = PrepareCommand(conn, sql);
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "nextFireTime", GetDbDateTimeValue(ts));
        AddCommandParameter(cmd, "state1", state1);

        DbDataReader rs;
        using (rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            bool hasReachedLimit = false;
            while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false) && !hasReachedLimit)
            {
                if (resultList.Count == count)
                {
                    hasReachedLimit = true;
                }
                else
                {
                    string triggerName = rs.GetString(ColumnTriggerName)!;
                    string groupName = rs.GetString(ColumnTriggerGroup)!;
                    resultList.Add(new TriggerKey(triggerName, groupName));
                }
            }

            return hasReachedLimit;
        }
    }

    protected virtual string GetSelectNextMisfiredTriggersInStateToAcquireSql(int count)
    {
        // by default we don't support limits, this is db specific
        return SqlSelectHasMisfiredTriggersInState;
    }

    protected virtual string GetSelectMisfiredTriggersToRecoverSql(int count)
    {
        // by default we don't support limits, this is db specific
        return SqlSelectMisfiredTriggersToRecover;
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> CountMisfiredTriggersInState(
        ConnectionAndTransactionHolder conn,
        string state1,
        DateTimeOffset ts,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(GetCountMisfiredTriggersInStateSql()));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "nextFireTime", GetDbDateTimeValue(ts));
        AddCommandParameter(cmd, "state1", state1);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public virtual async ValueTask<List<TriggerKey>> SelectMisfiredTriggersInGroupInState(
        ConnectionAndTransactionHolder conn,
        string groupName,
        string state,
        DateTimeOffset ts,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectMisfiredTriggersInGroupInState));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "timestamp", GetDbDateTimeValue(ts));
        AddCommandParameter(cmd, "triggerGroup", groupName);
        AddCommandParameter(cmd, "state", state);

        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        List<TriggerKey> list = [];
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string triggerName = rs.GetString(ColumnTriggerName)!;
            list.Add(new TriggerKey(triggerName, groupName));
        }

        return list;
    }

    /// <inheritdoc />
    public virtual async ValueTask<List<IOperableTrigger>> SelectTriggersForRecoveringJobs(ConnectionAndTransactionHolder conn, CancellationToken cancellationToken = default)
    {
        List<IOperableTrigger> triggers = [];
        List<FiredTriggerRecord> triggerData = [];
        List<TriggerKey> keys = [];

        using (var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectInstancesRecoverableFiredTriggers)))
        {
            AddCommandParameter(cmd, "schedulerName", schedName);
            AddCommandParameter(cmd, "instanceName", instanceId);
            AddCommandParameter(cmd, "requestsRecovery", GetDbBooleanValue(true));

            using (var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                long dumId = timeProvider.GetTimestamp();

                while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    string jobName = rs.GetString(ColumnJobName)!;
                    string jobGroup = rs.GetString(ColumnJobGroup)!;
                    string trigName = rs.GetString(ColumnTriggerName)!;
                    string trigGroup = rs.GetString(ColumnTriggerGroup)!;
                    int priority = Convert.ToInt32(rs[ColumnPriority], CultureInfo.InvariantCulture);
                    DateTimeOffset firedTime = GetDateTimeFromDbValue(rs[ColumnFiredTime]) ?? DateTimeOffset.MinValue;
                    DateTimeOffset scheduledTime = GetDateTimeFromDbValue(rs[ColumnScheduledTime]) ?? DateTimeOffset.MinValue;
                    SimpleTriggerImpl rcvryTrig = new SimpleTriggerImpl("recover_" + instanceId + "_" + Convert.ToString(dumId++, CultureInfo.InvariantCulture),
                        SchedulerConstants.DefaultRecoveryGroup, scheduledTime);
                    rcvryTrig.JobKey = new JobKey(jobName, jobGroup);
                    rcvryTrig.Priority = priority;
                    rcvryTrig.MisfireInstruction = MisfireInstruction.IgnoreMisfirePolicy;

                    var dataHolder = new FiredTriggerRecord
                    {
                        ScheduleTimestamp = scheduledTime,
                        FireTimestamp = firedTime
                    };

                    triggerData.Add(dataHolder);
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
            FiredTriggerRecord dataHolder = triggerData[i];

            // load job data map and transfer information
            JobDataMap jd = await SelectTriggerJobDataMap(conn, key, cancellationToken).ConfigureAwait(false);
            jd[SchedulerConstants.FailedJobOriginalTriggerName] = key.Name;
            jd[SchedulerConstants.FailedJobOriginalTriggerGroup] = key.Group;
            jd[SchedulerConstants.FailedJobOriginalTriggerFiretime] = Convert.ToString(dataHolder.FireTimestamp, CultureInfo.InvariantCulture)!;
            jd[SchedulerConstants.FailedJobOriginalTriggerScheduledFiretime] = Convert.ToString(dataHolder.ScheduleTimestamp, CultureInfo.InvariantCulture)!;
            trigger.JobDataMap = jd;
        }

        return triggers;
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> DeleteFiredTriggers(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteFiredTriggers));
        AddCommandParameter(cmd, "schedulerName", schedName);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> DeleteFiredTriggers(
        ConnectionAndTransactionHolder conn,
        string instanceName,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteInstancesFiredTriggers));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "instanceName", instanceName);
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> DeleteFiredTriggers(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteFiredTriggersForTrigger));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> DeleteFiredTriggers(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteFiredTriggersForJob));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "jobName", jobKey.Name);
        AddCommandParameter(cmd, "jobGroup", jobKey.Group);
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<bool> IsJobCurrentlyExecuting(
        ConnectionAndTransactionHolder conn,
        string jobName,
        string jobGroup,
        CancellationToken cancellationToken = default)
    {
        using DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectCountExecutingFiredTriggersOfJob));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "jobName", jobName);
        AddCommandParameter(cmd, "jobGroup", jobGroup);
        AddCommandParameter(cmd, "executingState", AdoConstants.StateExecuting);

        object? result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result) > 0;
    }

    /// <inheritdoc />
    public virtual async ValueTask<bool> IsTriggerCurrentlyExecuting(
        ConnectionAndTransactionHolder conn,
        string triggerName,
        string triggerGroup,
        CancellationToken cancellationToken = default)
    {
        using DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectCountExecutingFiredTriggersOfTrigger));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "triggerName", triggerName);
        AddCommandParameter(cmd, "triggerGroup", triggerGroup);
        AddCommandParameter(cmd, "executingState", AdoConstants.StateExecuting);

        object? result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result) > 0;
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> InsertTrigger(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        string state,
        IJobDetail jobDetail,
        CancellationToken cancellationToken = default)
    {
        var jobData = SerializeJobData(trigger.JobDataMap);

        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlInsertTrigger));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "triggerName", trigger.Key.Name);
        AddCommandParameter(cmd, "triggerGroup", trigger.Key.Group);
        AddCommandParameter(cmd, "triggerJobName", trigger.JobKey.Name);
        AddCommandParameter(cmd, "triggerJobGroup", trigger.JobKey.Group);
        AddCommandParameter(cmd, "triggerDescription", trigger.Description);
        AddCommandParameter(cmd, "triggerNextFireTime", GetDbDateTimeValue(trigger.GetNextFireTimeUtc()));
        AddCommandParameter(cmd, "triggerPreviousFireTime", GetDbDateTimeValue(trigger.GetPreviousFireTimeUtc()));
        AddCommandParameter(cmd, "triggerState", state);

        var tDel = FindTriggerPersistenceDelegate(trigger);
        string type = TriggerTypeBlob;
        if (tDel is not null)
        {
            type = tDel.GetHandledTriggerTypeDiscriminator();
        }

        AddCommandParameter(cmd, "triggerType", type);
        AddCommandParameter(cmd, "triggerStartTime", GetDbDateTimeValue(trigger.StartTimeUtc));
        AddCommandParameter(cmd, "triggerEndTime", GetDbDateTimeValue(trigger.EndTimeUtc));
        AddCommandParameter(cmd, "triggerCalendarName", trigger.CalendarName);
        AddCommandParameter(cmd, "triggerMisfireInstruction", trigger.MisfireInstruction);
        AddCommandParameter(cmd, "triggerJobJobDataMap", jobData, DbProvider.Metadata.DbBinaryType);

        AddCommandParameter(cmd, "triggerPriority", trigger.Priority);

        string? execGroup = trigger.ExecutionGroup;
        AddCommandParameter(cmd, "triggerExecutionGroup", (object?) execGroup ?? DBNull.Value);

        string? preferredNode = trigger.PreferredNode;
        AddCommandParameter(cmd, "triggerPreferredNode", (object?) preferredNode ?? DBNull.Value);
        AddCommandParameter(cmd, "triggerPreferredNodeAuto", GetDbBooleanValue(preferredNode is not null && trigger.IsPreferredNodeAuto));

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
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlInsertBlobTrigger));
        // update the blob
        byte[]? buf = SerializeObject(trigger);
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "triggerName", trigger.Key.Name);
        AddCommandParameter(cmd, "triggerGroup", trigger.Key.Group);
        AddCommandParameter(cmd, "blob", buf, DbProvider.Metadata.DbBinaryType);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> UpdateTrigger(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        string state,
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
        bool writePreferredNode = (trigger as AbstractTrigger)?.PreferredNodeDirty == true;

        string sqlUpdate = (updateJobData, writePreferredNode) switch
        {
            (true, true) => SqlUpdateTriggerWithPreferredNode,
            (true, false) => SqlUpdateTrigger,
            (false, true) => SqlUpdateTriggerSkipDataWithPreferredNode,
            (false, false) => SqlUpdateTriggerSkipData,
        };
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(sqlUpdate));

        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "triggerJobName", trigger.JobKey.Name);
        AddCommandParameter(cmd, "triggerJobGroup", trigger.JobKey.Group);
        AddCommandParameter(cmd, "triggerDescription", trigger.Description);
        AddCommandParameter(cmd, "triggerNextFireTime", GetDbDateTimeValue(trigger.GetNextFireTimeUtc()));
        AddCommandParameter(cmd, "triggerPreviousFireTime", GetDbDateTimeValue(trigger.GetPreviousFireTimeUtc()));

        AddCommandParameter(cmd, "triggerState", state);

        var tDel = FindTriggerPersistenceDelegate(trigger);

        string type = TriggerTypeBlob;
        if (tDel is not null)
        {
            type = tDel.GetHandledTriggerTypeDiscriminator();
        }

        AddCommandParameter(cmd, "triggerType", type);

        AddCommandParameter(cmd, "triggerStartTime", GetDbDateTimeValue(trigger.StartTimeUtc));
        AddCommandParameter(cmd, "triggerEndTime", GetDbDateTimeValue(trigger.EndTimeUtc));
        AddCommandParameter(cmd, "triggerCalendarName", trigger.CalendarName);
        AddCommandParameter(cmd, "triggerMisfireInstruction", trigger.MisfireInstruction);
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
            string? preferredNode = trigger.PreferredNode;
            AddCommandParameter(cmd, "triggerPreferredNode", (object?) preferredNode ?? DBNull.Value);
            AddCommandParameter(cmd, "triggerPreferredNodeAuto", GetDbBooleanValue(preferredNode is not null && trigger.IsPreferredNodeAuto));
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
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateBlobTrigger));
        // update the blob
        byte[]? os = SerializeObject(trigger);

        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "blob", os, DbProvider.Metadata.DbBinaryType);
        AddCommandParameter(cmd, "triggerName", trigger.Key.Name);
        AddCommandParameter(cmd, "triggerGroup", trigger.Key.Group);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> UpdateTriggerState(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        string state,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateTriggerState));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "state", state);
        AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> UpdateTriggerStateFromOtherStates(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        string newState,
        string oldState1,
        string oldState2,
        string oldState3,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateTriggerStateFromStates));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "newState", newState);
        AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);
        AddCommandParameter(cmd, "oldState1", oldState1);
        AddCommandParameter(cmd, "oldState2", oldState2);
        AddCommandParameter(cmd, "oldState3", oldState3);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> UpdateTriggerGroupStateFromOtherStates(
        ConnectionAndTransactionHolder conn,
        GroupMatcher<TriggerKey> matcher,
        string newState,
        string oldState1,
        string oldState2,
        string oldState3,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateTriggerGroupStateFromStates));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "newState", newState);
        AddCommandParameter(cmd, "groupName", ToSqlLikeClause(matcher));
        AddCommandParameter(cmd, "oldState1", oldState1);
        AddCommandParameter(cmd, "oldState2", oldState2);
        AddCommandParameter(cmd, "oldState3", oldState3);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> UpdateTriggerStateFromOtherState(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        string newState,
        string oldState,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateTriggerStateFromState));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "newState", newState);
        AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);
        AddCommandParameter(cmd, "oldState", oldState);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<int> UpdateTriggerStateFromOtherStateWithNextFireTime(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        string newState,
        string oldState,
        DateTimeOffset nextFireTime,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateTriggerStateFromStateWithNextFireTime));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "newState", newState);
        AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);
        AddCommandParameter(cmd, "oldState", oldState);
        AddCommandParameter(cmd, "nextFireTime", GetDbDateTimeValue(nextFireTime));

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> UpdateTriggerGroupStateFromOtherState(
        ConnectionAndTransactionHolder conn,
        GroupMatcher<TriggerKey> matcher,
        string newState,
        string oldState,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateTriggerGroupStateFromState));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "newState", newState);
        AddCommandParameter(cmd, "triggerGroup", ToSqlLikeClause(matcher));
        AddCommandParameter(cmd, "oldState", oldState);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> UpdateTriggerStatesForJob(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        string state,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateJobTriggerStates));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "state", state);
        AddCommandParameter(cmd, "jobName", jobKey.Name);
        AddCommandParameter(cmd, "jobGroup", jobKey.Group);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> UpdateTriggerStatesForJobFromOtherState(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        string state,
        string oldState,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateJobTriggerStatesFromOtherState));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "state", state);
        AddCommandParameter(cmd, "jobName", jobKey.Name);
        AddCommandParameter(cmd, "jobGroup", jobKey.Group);
        AddCommandParameter(cmd, "oldState", oldState);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> DeleteBlobTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteBlobTrigger));
        AddCommandParameter(cmd, "schedulerName", schedName);
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

        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteTrigger));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    protected virtual async ValueTask DeleteTriggerExtension(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken)
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
        public IDictionary? JobDataMap;
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
            JobName = rs.GetString(ColumnJobName)!,
            JobGroup = rs.GetString(ColumnJobGroup)!,
            Description = rs.GetString(ColumnDescription),
            TriggerType = rs.GetString(ColumnTriggerType)!,
            CalendarName = rs.GetString(ColumnCalendarName),
            MisfireInstruction = rs.GetInt32(ColumnMifireInstruction),
            Priority = rs.GetInt32(ColumnPriority)
        };

        row.JobDataMap = await ReadMapFromReader(rs, 11).ConfigureAwait(false);

        row.NextFireTimeUtc = GetDateTimeFromDbValue(rs[ColumnNextFireTime]);
        row.PreviousFireTimeUtc = GetDateTimeFromDbValue(rs[ColumnPreviousFireTime]);
        row.StartTimeUtc = GetDateTimeFromDbValue(rs[ColumnStartTime]) ?? DateTimeOffset.MinValue;
        row.EndTimeUtc = GetDateTimeFromDbValue(rs[ColumnEndTime]);

        // check if we access fast path
        if (row.TriggerType is TriggerTypeCron or TriggerTypeSimple)
        {
            row.Props = FindTriggerPersistenceDelegate(row.TriggerType)!.ReadTriggerPropertyBundle(rs);
        }

        row.MisfireOriginalFireTime = GetDateTimeFromDbValue(rs[ColumnMisfireOriginalFireTime]);

        int execGroupOrdinal = rs.GetOrdinal(ColumnExecutionGroup);
        row.ExecutionGroup = rs.IsDBNull(execGroupOrdinal) ? null : rs.GetString(execGroupOrdinal);

        int preferredNodeOrdinal = rs.GetOrdinal(ColumnPreferredNode);
        row.PreferredNode = rs.IsDBNull(preferredNodeOrdinal) ? null : rs.GetString(preferredNodeOrdinal);
        int preferredNodeAutoOrdinal = rs.GetOrdinal(ColumnPreferredNodeAuto);
        row.PreferredNodeAuto = !rs.IsDBNull(preferredNodeAutoOrdinal) && GetBooleanFromDbValue(rs.GetValue(preferredNodeAutoOrdinal));

        return row;
    }

    /// <summary>
    /// Applies the fire-time state carried on the TRIGGERS row. Applies to blob-deserialized triggers
    /// just as much as to built ones.
    /// </summary>
    private static void ApplyTriggerFireState(IOperableTrigger trigger, TriggerRow row)
    {
        trigger.MisfireInstruction = row.MisfireInstruction;
        trigger.SetNextFireTimeUtc(row.NextFireTimeUtc);
        trigger.SetPreviousFireTimeUtc(row.PreviousFireTimeUtc);

        if (row.MisfireOriginalFireTime.HasValue && trigger is AbstractTrigger at)
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
        (trigger as AbstractTrigger)?.SetPreferredNodeRaw(row.PreferredNode, row.PreferredNodeAuto, markDirty: false);
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
        TriggerBuilder tb = TriggerBuilder.Create()
            .WithDescription(row.Description)
            .WithPriority(row.Priority)
            .StartAt(row.StartTimeUtc)
            .EndAt(row.EndTimeUtc)
            .WithIdentity(triggerKey)
            .ModifiedByCalendar(row.CalendarName)
            .WithSchedule(triggerProps.ScheduleBuilder)
            .ForJob(new JobKey(row.JobName, row.JobGroup));

        if (row.JobDataMap is not null)
        {
            bool clearDirtyFlag = !row.JobDataMap.Contains(SchedulerConstants.ForceJobDataMapDirty);
            tb.UsingJobData(new JobDataMap(row.JobDataMap));
            if (clearDirtyFlag)
            {
                tb.ClearDirty();
            }
        }

        var trigger = (IOperableTrigger) tb.Build();

        ApplyTriggerFireState(trigger, row);
        SetTriggerStateProperties(trigger, triggerProps);
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

        using (var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTrigger)))
        {
            AddCommandParameter(cmd, "schedulerName", schedName);
            AddCommandParameter(cmd, "triggerName", triggerKey.Name);
            AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

            using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            row = await ReadTriggerRow(rs).ConfigureAwait(false);
        }

        if (row.TriggerType == TriggerTypeBlob)
        {
            IOperableTrigger? blobTrigger = null;

            using (var cmd2 = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectBlobTrigger)))
            {
                AddCommandParameter(cmd2, "schedulerName", schedName);
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
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTrigger));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await rs.ReadAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void SetTriggerStateProperties(IOperableTrigger trigger, TriggerPropertyBundle props)
    {
        if (props.StatePropertyNames is null)
        {
            return;
        }

        ObjectUtils.SetObjectProperties(trigger, props.StatePropertyNames, props.StatePropertyValues);
    }

    /// <inheritdoc />
    public virtual async ValueTask<JobDataMap> SelectTriggerJobDataMap(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggerData));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

        using var rs = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false);
        if (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var map = await ReadMapFromReader(rs, 0).ConfigureAwait(false);
            if (map is not null)
            {
                return map as JobDataMap ?? new JobDataMap(map);
            }
        }

        return new JobDataMap();
    }

    /// <inheritdoc />
    public virtual async ValueTask<string> SelectTriggerState(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggerState));

        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

        var state = (string?) await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        return state ?? StateDeleted;
    }

    /// <inheritdoc />
    public virtual async ValueTask<TriggerStatus?> SelectTriggerStatus(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggerStatus));
        TriggerStatus? status = null;

        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);
        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string state = rs.GetString(ColumnTriggerState)!;
            object nextFireTime = rs[ColumnNextFireTime];
            string jobName = rs.GetString(ColumnJobName)!;
            string jobGroup = rs.GetString(ColumnJobGroup)!;

            var nft = GetDateTimeFromDbValue(nextFireTime);

            status = new TriggerStatus(state, nft, triggerKey, new JobKey(jobName, jobGroup));
        }

        return status;
    }

    private async ValueTask<string?> SelectTriggerType(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggerType));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return rs.GetString(ColumnTriggerType)!;
        }
        return null;
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> SelectNumTriggers(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectNumTriggers));
        AddCommandParameter(cmd, "schedulerName", schedName);

        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        return count;
    }

    /// <inheritdoc />
    public virtual async ValueTask<List<string>> SelectTriggerGroups(ConnectionAndTransactionHolder conn, CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggerGroups));
        AddCommandParameter(cmd, "schedulerName", schedName);

        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        List<string> list = [];
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(rs.GetString(0));
        }

        return list;
    }

    /// <inheritdoc />
    public virtual async ValueTask<List<string>> SelectTriggerGroups(ConnectionAndTransactionHolder conn, GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggerGroupsFiltered));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "triggerGroup", ToSqlLikeClause(matcher));
        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        List<string> list = [];
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(rs.GetString(0));
        }

        return list;
    }

    /// <inheritdoc />
    public virtual async Task<List<TriggerKey>> SelectTriggersInGroup(ConnectionAndTransactionHolder conn, GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        string sql;
        string parameter;
        if (IsMatcherEquals(matcher))
        {
            sql = ReplaceTablePrefix(SqlSelectTriggersInGroup);
            parameter = StdAdoDelegate.ToSqlEqualsClause(matcher);
        }
        else
        {
            sql = ReplaceTablePrefix(SqlSelectTriggersInGroupLike);
            parameter = ToSqlLikeClause(matcher);
        }

        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(sql));
        AddCommandParameter(cmd, "schedulerName", schedName);
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
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlInsertPausedTriggerGroup));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "triggerGroup", groupName);
        int rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return rows;
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> DeletePausedTriggerGroup(
        ConnectionAndTransactionHolder conn,
        string groupName,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlDeletePausedTriggerGroup));
        AddCommandParameter(cmd, "schedulerName", schedName);
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
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlDeletePausedTriggerGroup));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "triggerGroup", ToSqlLikeClause(matcher));
        int rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return rows;
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> DeleteAllPausedTriggerGroups(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlDeletePausedTriggerGroups));
        AddCommandParameter(cmd, "schedulerName", schedName);
        int rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return rows;
    }

    /// <inheritdoc />
    public virtual async ValueTask<bool> IsTriggerGroupPaused(
        ConnectionAndTransactionHolder conn,
        string groupName,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectPausedTriggerGroup));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "triggerGroup", groupName);

        return await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    /// <inheritdoc />
    public virtual async ValueTask<bool> IsExistingTriggerGroup(
        ConnectionAndTransactionHolder conn,
        string groupName,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectNumTriggersInGroup));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "triggerGroup", groupName);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) > 0;
    }

    /// <inheritdoc />
    public virtual async ValueTask<TriggerKey?> SelectTriggerForFireTime(
        ConnectionAndTransactionHolder conn,
        DateTimeOffset fireTime,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggerForFireTime));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "state", StateWaiting);
        AddCommandParameter(cmd, "fireTime", GetDbDateTimeValue(fireTime));

        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return new TriggerKey(rs.GetString(ColumnTriggerName)!, rs.GetString(ColumnTriggerGroup)!);
        }

        return null;
    }

    /// <inheritdoc />
    public virtual async ValueTask<List<TriggerAcquireResult>> SelectTriggerToAcquire(
        ConnectionAndTransactionHolder conn,
        DateTimeOffset noLaterThan,
        DateTimeOffset noEarlierThan,
        int maxCount,
        long liveNodeCutoff,
        CancellationToken cancellationToken = default)
    {
        if (maxCount < 1)
        {
            maxCount = 1; // we want at least one trigger back.
        }

        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(GetSelectNextTriggerToAcquireSql(maxCount)));
        List<TriggerAcquireResult> nextTriggers = new();

        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "state", StateWaiting);
        AddCommandParameter(cmd, "noLaterThan", GetDbDateTimeValue(noLaterThan));
        AddCommandParameter(cmd, "noEarlierThan", GetDbDateTimeValue(noEarlierThan));
        AddPreferredNodeParameters(cmd, liveNodeCutoff);

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
                execGroupOrdinal = rs.GetOrdinal(ColumnExecutionGroup);
            }

            if (nextTriggers.Count < maxCount)
            {
                string? executionGroup = rs.IsDBNull(execGroupOrdinal) ? null : rs.GetString(execGroupOrdinal);
                var result = new TriggerAcquireResult(
                    (string) rs[ColumnTriggerName],
                    (string) rs[ColumnTriggerGroup],
                    (string) rs[ColumnJobClass],
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

    protected virtual string GetSelectNextTriggerToAcquireSql(int maxCount)
    {
        // by default we don't support limits, this is db specific
        return SqlSelectNextTriggerToAcquire;
    }

    /// <summary>
    /// Binds the preferred node (node affinity) parameters of the acquisition SQL. Parameters are
    /// added in SQL token order for providers with positional binding.
    /// </summary>
    /// <param name="cmd">The acquisition command.</param>
    /// <param name="liveNodeCutoff">
    /// Tick value below which a node's last check-in is considered stale, releasing its pinned
    /// triggers to other nodes.
    /// </param>
    protected void AddPreferredNodeParameters(DbCommand cmd, long liveNodeCutoff)
    {
        AddCommandParameter(cmd, "instanceId", instanceId);
        AddCommandParameter(cmd, "autoPinSentinel", AutoPinSentinel);
        AddCommandParameter(cmd, "liveNodeCutoff", liveNodeCutoff);
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> UpdateTriggerPreferredNodeConditional(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        string preferredNode,
        bool preferredNodeAuto,
        string expectedPreferredNode,
        bool expectedPreferredNodeAuto,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateTriggerPreferredNodeConditional));
        // Parameters are added in SQL token order for providers with positional binding
        AddCommandParameter(cmd, "triggerPreferredNode", preferredNode);
        AddCommandParameter(cmd, "triggerPreferredNodeAuto", GetDbBooleanValue(preferredNodeAuto));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);
        AddCommandParameter(cmd, "expectedPreferredNode", expectedPreferredNode);
        AddCommandParameter(cmd, "expectedPreferredNodeAuto", GetDbBooleanValue(expectedPreferredNodeAuto));
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> RepinTriggersFromDeadNode(
        ConnectionAndTransactionHolder conn,
        string oldPreferredNode,
        string newPreferredNode,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlRepinTriggersFromDeadNode));
        // Parameters are added in SQL token order for providers with positional binding.
        // Only auto-claimed pins are released; the reset value ("*") is not itself auto-claimed.
        AddCommandParameter(cmd, "newPreferredNode", newPreferredNode);
        AddCommandParameter(cmd, "newPreferredNodeAuto", GetDbBooleanValue(false));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "oldPreferredNode", oldPreferredNode);
        AddCommandParameter(cmd, "oldPreferredNodeAuto", GetDbBooleanValue(true));
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    protected virtual string GetCountMisfiredTriggersInStateSql()
    {
        return SqlCountMisfiredTriggersInStates;
    }

    /// <inheritdoc />
    public virtual async ValueTask<List<TriggerAcquireResult>> SelectTriggerToAcquire(
        ConnectionAndTransactionHolder conn,
        DateTimeOffset noLaterThan,
        DateTimeOffset noEarlierThan,
        int maxCount,
        Dictionary<string, int?> executionLimits,
        long liveNodeCutoff,
        CancellationToken cancellationToken = default)
    {
        if (maxCount < 1)
        {
            maxCount = 1;
        }

        string sql = GetSelectNextTriggerToAcquireSql(maxCount);

        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(sql));
        List<TriggerAcquireResult> nextTriggers = new();

        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "state", StateWaiting);
        AddCommandParameter(cmd, "noLaterThan", GetDbDateTimeValue(noLaterThan));
        AddCommandParameter(cmd, "noEarlierThan", GetDbDateTimeValue(noEarlierThan));
        AddPreferredNodeParameters(cmd, liveNodeCutoff);

        // Create a working copy to decrement during iteration
        Dictionary<string, int?> limitsWorkingCopy = new(executionLimits, StringComparer.Ordinal);

        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
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
                execGroupOrdinal = rs.GetOrdinal(ColumnExecutionGroup);
            }

            if (nextTriggers.Count < maxCount)
            {
                string? executionGroup = rs.IsDBNull(execGroupOrdinal)
                    ? null
                    : rs.GetString(execGroupOrdinal);

                // Check execution limits
                if (!ExecutionLimits.CheckExecutionLimits(executionGroup, limitsWorkingCopy))
                {
                    continue; // skip this trigger, its group is at limit
                }

                var result = new TriggerAcquireResult(
                    (string) rs[ColumnTriggerName],
                    (string) rs[ColumnTriggerGroup],
                    (string) rs[ColumnJobClass],
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
        string state,
        IJobDetail? job,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlInsertFiredTrigger));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "triggerEntryId", trigger.FireInstanceId);
        AddCommandParameter(cmd, "triggerName", trigger.Key.Name);
        AddCommandParameter(cmd, "triggerGroup", trigger.Key.Group);
        AddCommandParameter(cmd, "triggerInstanceName", instanceId);
        AddCommandParameter(cmd, "triggerFireTime", GetDbDateTimeValue(timeProvider.GetUtcNow()));
        AddCommandParameter(cmd, "triggerScheduledTime", GetDbDateTimeValue(trigger.GetNextFireTimeUtc()));
        AddCommandParameter(cmd, "triggerState", state);
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
        string state,
        IJobDetail job,
        CancellationToken cancellationToken = default)
    {
        var ps = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateFiredTrigger));
        AddCommandParameter(ps, "schedulerName", schedName);
        AddCommandParameter(ps, "instanceName", instanceId);
        AddCommandParameter(ps, "firedTime", GetDbDateTimeValue(timeProvider.GetUtcNow()));
        AddCommandParameter(ps, "scheduledTime", GetDbDateTimeValue(trigger.GetNextFireTimeUtc()));
        AddCommandParameter(ps, "entryState", state);
        AddCommandParameter(ps, "jobName", trigger.JobKey.Name);
        AddCommandParameter(ps, "jobGroup", trigger.JobKey.Group);
        AddCommandParameter(ps, "isNonConcurrent", GetDbBooleanValue(job.ConcurrentExecutionDisallowed));
        AddCommandParameter(ps, "requestsRecover", GetDbBooleanValue(job.RequestsRecovery));
        AddCommandParameter(ps, "entryId", trigger.FireInstanceId);

        return await ps.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<List<FiredTriggerRecord>> SelectFiredTriggerRecords(ConnectionAndTransactionHolder conn,
        string triggerName,
        string groupName,
        CancellationToken cancellationToken = default)
    {
        DbCommand cmd;

        List<FiredTriggerRecord> lst = [];

        if (triggerName is not null)
        {
            cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectFiredTrigger));
            AddCommandParameter(cmd, "schedulerName", schedName);
            AddCommandParameter(cmd, "triggerName", triggerName);
            AddCommandParameter(cmd, "triggerGroup", groupName);
        }
        else
        {
            cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectFiredTriggerGroup));
            AddCommandParameter(cmd, "schedulerName", schedName);
            AddCommandParameter(cmd, "triggerGroup", groupName);
        }

        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            FiredTriggerRecord rec = new FiredTriggerRecord();

            rec.FireInstanceId = rs.GetString(ColumnEntryId)!;
            rec.FireInstanceState = rs.GetString(ColumnEntryState)!;
            rec.FireTimestamp = GetDateTimeFromDbValue(rs[ColumnFiredTime]) ?? DateTimeOffset.MinValue;
            rec.ScheduleTimestamp = GetDateTimeFromDbValue(rs[ColumnScheduledTime]) ?? DateTimeOffset.MinValue;
            rec.Priority = Convert.ToInt32(rs[ColumnPriority], CultureInfo.InvariantCulture);
            rec.SchedulerInstanceId = rs.GetString(ColumnInstanceName)!;
            rec.TriggerKey = new TriggerKey(rs.GetString(ColumnTriggerName)!, rs.GetString(ColumnTriggerGroup)!);
            if (rec.FireInstanceState != (StateAcquired))
            {
                rec.JobDisallowsConcurrentExecution = GetBooleanFromDbValue(rs[ColumnIsNonConcurrent]);
                rec.JobRequestsRecovery = GetBooleanFromDbValue(rs[ColumnRequestsRecovery]);
                rec.JobKey = new JobKey(rs.GetString(ColumnJobName)!, rs.GetString(ColumnJobGroup)!);
            }

            lst.Add(rec);
        }

        return lst;
    }

    /// <inheritdoc />
    public virtual async ValueTask<List<FiredTriggerRecord>> SelectFiredTriggerRecordsByJob(
        ConnectionAndTransactionHolder conn,
        string jobName,
        string groupName,
        CancellationToken cancellationToken = default)
    {
        List<FiredTriggerRecord> lst = [];

        DbCommand cmd;
        if (jobName is not null)
        {
            cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectFiredTriggersOfJob));
            AddCommandParameter(cmd, "schedulerName", schedName);
            AddCommandParameter(cmd, "jobName", jobName);
            AddCommandParameter(cmd, "jobGroup", groupName);
        }
        else
        {
            cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectFiredTriggersOfJobGroup));
            AddCommandParameter(cmd, "schedulerName", schedName);
            AddCommandParameter(cmd, "jobGroup", groupName);
        }

        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            FiredTriggerRecord rec = new FiredTriggerRecord();

            rec.FireInstanceId = rs.GetString(ColumnEntryId)!;
            rec.FireInstanceState = rs.GetString(ColumnEntryState)!;
            rec.FireTimestamp = GetDateTimeFromDbValue(rs[ColumnFiredTime]) ?? DateTimeOffset.MinValue;
            rec.ScheduleTimestamp = GetDateTimeFromDbValue(rs[ColumnScheduledTime]) ?? DateTimeOffset.MinValue;
            rec.Priority = Convert.ToInt32(rs[ColumnPriority], CultureInfo.InvariantCulture);
            rec.SchedulerInstanceId = rs.GetString(ColumnInstanceName)!;
            rec.TriggerKey = new TriggerKey(rs.GetString(ColumnTriggerName)!, rs.GetString(ColumnTriggerGroup)!);
            if (rec.FireInstanceState != StateAcquired)
            {
                rec.JobDisallowsConcurrentExecution = GetBooleanFromDbValue(rs[ColumnIsNonConcurrent]);
                rec.JobRequestsRecovery = GetBooleanFromDbValue(rs[ColumnRequestsRecovery]);
                rec.JobKey = new JobKey(rs.GetString(ColumnJobName)!, rs.GetString(ColumnJobGroup)!);
            }

            lst.Add(rec);
        }

        return lst;
    }

    /// <inheritdoc />
    public virtual async ValueTask<List<FiredTriggerRecord>> SelectInstancesFiredTriggerRecords(ConnectionAndTransactionHolder conn, string instanceName, CancellationToken cancellationToken = default)
    {
        List<FiredTriggerRecord> lst = [];

        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectInstancesFiredTriggers));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "instanceName", instanceName);
        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            FiredTriggerRecord rec = new FiredTriggerRecord();

            rec.FireInstanceId = rs.GetString(ColumnEntryId)!;
            rec.FireInstanceState = rs.GetString(ColumnEntryState)!;
            rec.FireTimestamp = GetDateTimeFromDbValue(rs[ColumnFiredTime]) ?? DateTimeOffset.MinValue;
            rec.ScheduleTimestamp = GetDateTimeFromDbValue(rs[ColumnScheduledTime]) ?? DateTimeOffset.MinValue;
            rec.SchedulerInstanceId = rs.GetString(ColumnInstanceName)!;
            rec.TriggerKey = new TriggerKey(rs.GetString(ColumnTriggerName)!, rs.GetString(ColumnTriggerGroup)!);
            if (rec.FireInstanceState != StateAcquired)
            {
                rec.JobDisallowsConcurrentExecution = GetBooleanFromDbValue(rs[ColumnIsNonConcurrent]);
                rec.JobRequestsRecovery = GetBooleanFromDbValue(rs[ColumnRequestsRecovery]);
                rec.JobKey = new JobKey(rs.GetString(ColumnJobName)!, rs.GetString(ColumnJobGroup)!);
            }

            lst.Add(rec);
        }

        return lst;
    }

    /// <inheritdoc />
    public virtual async ValueTask<List<string>> SelectFiredTriggerInstanceNames(ConnectionAndTransactionHolder conn, CancellationToken cancellationToken = default)
    {
        List<string> instanceNames = [];
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectFiredTriggerInstanceNames));
        AddCommandParameter(cmd, "schedulerName", schedName);
        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            instanceNames.Add(rs.GetString(ColumnInstanceName)!);
        }

        return instanceNames;
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> DeleteFiredTrigger(
        ConnectionAndTransactionHolder conn,
        string entryId,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteFiredTrigger));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "triggerEntryId", entryId);
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public virtual void AddTriggerPersistenceDelegate(ITriggerPersistenceDelegate del)
    {
        logger.LogDebug("Adding TriggerPersistenceDelegate of type: {Type}", del.GetType());
        del.Initialize(tablePrefix, schedName, this);
        triggerPersistenceDelegates.Add(del);
    }

    protected virtual ITriggerPersistenceDelegate? FindTriggerPersistenceDelegate(IOperableTrigger trigger)
    {
        foreach (var del in triggerPersistenceDelegates)
        {
            if (del.CanHandleTriggerType(trigger))
            {
                return del;
            }
        }

        return null;
    }

    protected virtual ITriggerPersistenceDelegate? FindTriggerPersistenceDelegate(string discriminator)
    {
        foreach (var del in triggerPersistenceDelegates)
        {
            if (del.GetHandledTriggerTypeDiscriminator() == discriminator)
            {
                return del;
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
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggerExistence));
        AddCommandParameter(cmd, "schedulerName", schedName);
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
    public virtual async ValueTask<int> SelectNumTriggersForJob(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectNumTriggersForJob));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "jobName", jobKey.Name);
        AddCommandParameter(cmd, "jobGroup", jobKey.Group);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public virtual async ValueTask<List<IOperableTrigger>> SelectTriggersForJob(ConnectionAndTransactionHolder conn, JobKey jobKey, CancellationToken cancellationToken = default)
    {
        List<IOperableTrigger> trigList = [];
        List<TriggerKey> keys = [];

        using (var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggersForJob)))
        {
            AddCommandParameter(cmd, "schedulerName", schedName);
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
        using (var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggersForCalendar)))
        {
            AddCommandParameter(cmd, "schedulerName", schedName);
            AddCommandParameter(cmd, "calendarName", calendarName);
            using (var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    keys.Add(new TriggerKey(rs.GetString(ColumnTriggerName)!, rs.GetString(ColumnTriggerGroup)!));
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
    public virtual async ValueTask<List<string>> SelectPausedTriggerGroups(ConnectionAndTransactionHolder conn, CancellationToken cancellationToken = default)
    {
        List<string> retValue = [];
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectPausedTriggerGroups));
        AddCommandParameter(cmd, "schedulerName", schedName);
        using var dr = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await dr.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string groupName = (string) dr[ColumnTriggerGroup];
            retValue.Add(groupName);
        }

        return retValue;
    }

    /// <inheritdoc />
    public virtual async ValueTask UpdateMisfireOriginalFireTime(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        DateTimeOffset? fireTime,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateMisfireOrigFireTime));
        AddCommandParameter(cmd, "schedulerName", schedName);
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
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateMisfireOrigFireTime));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);
        AddCommandParameter(cmd, "misfireOrigFireTime", null);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask UpdateMisfiredTrigger(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        string newState,
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
        string state,
        DateTimeOffset ts,
        int count,
        CancellationToken cancellationToken = default)
    {
        // Always read one past the limit so we can tell the caller whether the limit truncated the
        // result, the same way HasMisfiredTriggersInState does.
        var sql = ReplaceTablePrefix(GetSelectMisfiredTriggersToRecoverSql(count != -1 ? count + 1 : count));

        List<TriggerKey> keys = [];
        List<TriggerRow> rows = [];
        bool hasMore = false;

        using (var cmd = PrepareCommand(conn, sql))
        {
            AddCommandParameter(cmd, "schedulerName", schedName);
            AddCommandParameter(cmd, "nextFireTime", GetDbDateTimeValue(ts));
            AddCommandParameter(cmd, "state1", state);

            using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (count != -1 && keys.Count == count)
                {
                    hasMore = true;
                    break;
                }

                keys.Add(new TriggerKey(rs.GetString(ColumnTriggerName)!, rs.GetString(ColumnTriggerGroup)!));
                rows.Add(await ReadTriggerRow(rs).ConfigureAwait(false));
            }
        }

        // Slots stay index-aligned with keys/rows while the follow-up queries fill them in; a slot left
        // null means the type-specific row was missing, and the trigger is dropped from the result.
        var built = new IOperableTrigger?[rows.Count];
        List<TriggerKey>? blobKeys = null;
        List<TriggerKey>? simpropKeys = null;

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];

            // SIMPLE and CRON triggers came back complete from the joined row.
            if (row.Props is not null)
            {
                built[i] = BuildTrigger(keys[i], row, row.Props);
                continue;
            }

            if (row.TriggerType == TriggerTypeBlob)
            {
                (blobKeys ??= []).Add(keys[i]);
                continue;
            }

            var tDel = FindTriggerPersistenceDelegate(row.TriggerType);
            if (tDel is null)
            {
                Throw.JobPersistenceException("No TriggerPersistenceDelegate for trigger discriminator type: " + row.TriggerType);
            }

            if (tDel is SimplePropertiesTriggerPersistenceDelegateSupport)
            {
                (simpropKeys ??= []).Add(keys[i]);
                continue;
            }

            // A custom persistence delegate storing into its own table. There is no batch read for
            // those, so fall back to reading it on its own rather than dropping the trigger.
            try
            {
                var props = await tDel.LoadExtendedTriggerProperties(conn, keys[i], cancellationToken).ConfigureAwait(false);
                built[i] = BuildTrigger(keys[i], row, props);
            }
            catch (InvalidOperationException)
            {
                // No row for this trigger (QTZ-386, deleted concurrently). Leave the slot empty so this
                // one trigger is skipped, rather than failing the whole batch over it.
            }
        }

        if (blobKeys is not null)
        {
            await SelectBlobTriggersForBatch(conn, keys, rows, built, blobKeys, cancellationToken).ConfigureAwait(false);
        }

        if (simpropKeys is not null)
        {
            await SelectSimpropTriggersForBatch(conn, keys, rows, built, simpropKeys, cancellationToken).ConfigureAwait(false);
        }

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

    /// <summary>
    /// Prepares a statement matching a chunk of trigger keys, by appending the parameterized key-set
    /// predicate to <paramref name="sqlPrefix" />.
    /// </summary>
    private DbCommand PrepareTriggerKeySetCommand(
        ConnectionAndTransactionHolder conn,
        string sqlPrefix,
        List<TriggerKey> keys,
        int offset,
        int length)
    {
        var paddedCount = AdoUtil.RoundUpTriggerKeyCount(length);
        var cmd = PrepareCommand(conn, ReplaceTablePrefix(sqlPrefix + AdoUtil.BuildTriggerKeyPredicate(paddedCount)));
        AddCommandParameter(cmd, "schedulerName", schedName);

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

            using var cmd = PrepareTriggerKeySetCommand(conn, SqlSelectBlobTriggersByKeysPrefix, blobKeys, offset, length);
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

            using var cmd = PrepareTriggerKeySetCommand(conn, SqlSelectSimpropTriggersByKeysPrefix, simpropKeys, offset, length);
            using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var key = new TriggerKey(rs.GetString(ColumnTriggerName)!, rs.GetString(ColumnTriggerGroup)!);
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
            new("schedulerName", schedName),
            new("triggerNextFireTime", GetDbDateTimeValue(trigger.GetNextFireTimeUtc())),
            new("triggerPreviousFireTime", GetDbDateTimeValue(trigger.GetPreviousFireTimeUtc())),
            new("triggerState", update.NewState),
            new("triggerStartTime", GetDbDateTimeValue(trigger.StartTimeUtc))
        ];

        if (writeMisfireOrigFireTime)
        {
            parameters.Add(new SqlStatementParameter("triggerMisfireOrigFireTime", GetDbDateTimeValue(update.MisfireOriginalFireTime)));
        }

        parameters.Add(new SqlStatementParameter("triggerName", trigger.Key.Name));
        parameters.Add(new SqlStatementParameter("triggerGroup", trigger.Key.Group));

        into.Add(new SqlStatement(
            ReplaceTablePrefix(writeMisfireOrigFireTime ? SqlUpdateTriggerMisfireWithOrigFireTime : SqlUpdateTriggerMisfire),
            parameters));

        // Update type-specific table: SimpleTrigger may have modified RepeatCount/TimesTriggered
        // via RescheduleNowWith* policies; blob triggers need re-serialization to persist all
        // in-memory changes. Other built-in types (Cron, CalendarInterval, DailyTimeInterval)
        // do not change extended properties during misfire.
        var persistenceDelegate = FindTriggerPersistenceDelegate(trigger);
        if (trigger is ISimpleTrigger simpleTrigger && persistenceDelegate is not null)
        {
            into.Add(new SqlStatement(ReplaceTablePrefix(SqlUpdateSimpleTrigger),
            [
                new SqlStatementParameter("schedulerName", schedName),
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
