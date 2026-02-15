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

    /// <inheritdoc />
    public virtual async ValueTask<int> CountMisfiredTriggersInState(
        ConnectionAndTransactionHolder conn,
        string state1,
        DateTimeOffset ts,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlCountMisfiredTriggersInStates));
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
            jd.Put(SchedulerConstants.FailedJobOriginalTriggerName, key.Name);
            jd.Put(SchedulerConstants.FailedJobOriginalTriggerGroup, key.Group);
            jd.Put(SchedulerConstants.FailedJobOriginalTriggerFiretime, Convert.ToString(dataHolder.FireTimestamp, CultureInfo.InvariantCulture)!);
            jd.Put(SchedulerConstants.FailedJobOriginalTriggerScheduledFiretime, Convert.ToString(dataHolder.ScheduleTimestamp, CultureInfo.InvariantCulture)!);
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

        var sqlUpdate = updateJobData ? SqlUpdateTrigger : SqlUpdateTriggerSkipData;
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
            AddCommandParameter(cmd, "triggerName", trigger.Key.Name);
            AddCommandParameter(cmd, "triggerGroup", trigger.Key.Group);
        }
        else
        {
            AddCommandParameter(cmd, "triggerName", trigger.Key.Name);
            AddCommandParameter(cmd, "triggerGroup", trigger.Key.Group);
        }

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

    /// <inheritdoc />
    public virtual async ValueTask<IOperableTrigger?> SelectTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        string jobName;
        string jobGroup;
        string? description;
        string triggerType;
        string? calendarName;
        int misFireInstr;
        int priority;

        IDictionary? map;

        DateTimeOffset? nextFireTimeUtc;
        DateTimeOffset? previousFireTimeUtc;
        DateTimeOffset startTimeUtc;
        DateTimeOffset? endTimeUtc;

        ITriggerPersistenceDelegate? tDel = null;
        TriggerPropertyBundle? triggerProps = null;

        using (var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTrigger)))
        {
            AddCommandParameter(cmd, "schedulerName", schedName);
            AddCommandParameter(cmd, "triggerName", triggerKey.Name);
            AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

            using (var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    return null;
                }

                jobName = rs.GetString(ColumnJobName)!;
                jobGroup = rs.GetString(ColumnJobGroup)!;
                description = rs.GetString(ColumnDescription);
                triggerType = rs.GetString(ColumnTriggerType)!;
                calendarName = rs.GetString(ColumnCalendarName);
                misFireInstr = rs.GetInt32(ColumnMifireInstruction);
                priority = rs.GetInt32(ColumnPriority);

                map = await ReadMapFromReader(rs, 11).ConfigureAwait(false);

                nextFireTimeUtc = GetDateTimeFromDbValue(rs[ColumnNextFireTime]);
                previousFireTimeUtc = GetDateTimeFromDbValue(rs[ColumnPreviousFireTime]);
                startTimeUtc = GetDateTimeFromDbValue(rs[ColumnStartTime]) ?? DateTimeOffset.MinValue;
                endTimeUtc = GetDateTimeFromDbValue(rs[ColumnEndTime]);

                // check if we access fast path
                if (triggerType is TriggerTypeCron or TriggerTypeSimple)
                {
                    tDel = FindTriggerPersistenceDelegate(triggerType);
                    triggerProps = tDel!.ReadTriggerPropertyBundle(rs);
                }
            }
        }

        IOperableTrigger? trigger = null;
        if (triggerType == TriggerTypeBlob)
        {
            using var cmd2 = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectBlobTrigger));
            AddCommandParameter(cmd2, "schedulerName", schedName);
            AddCommandParameter(cmd2, "triggerName", triggerKey.Name);
            AddCommandParameter(cmd2, "triggerGroup", triggerKey.Group);
            using var rs2 = await cmd2.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false);
            if (await rs2.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                trigger = await GetObjectFromBlob<IOperableTrigger>(rs2, 0, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            if (triggerProps is null)
            {
                // fast path didn't succeed
                tDel ??= FindTriggerPersistenceDelegate(triggerType);

                if (tDel is null)
                {
                    Throw.JobPersistenceException("No TriggerPersistenceDelegate for trigger discriminator type: " + triggerType);
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

            TriggerBuilder tb = TriggerBuilder.Create()
                .WithDescription(description)
                .WithPriority(priority)
                .StartAt(startTimeUtc)
                .EndAt(endTimeUtc)
                .WithIdentity(triggerKey)
                .ModifiedByCalendar(calendarName)
                .WithSchedule(triggerProps.ScheduleBuilder)
                .ForJob(new JobKey(jobName, jobGroup));

            if (map is not null)
            {
                bool clearDirtyFlag = !map.Contains(SchedulerConstants.ForceJobDataMapDirty);
                tb.UsingJobData(new JobDataMap(map));
                if (clearDirtyFlag)
                {
                    tb.ClearDirty();
                }
            }

            trigger = (IOperableTrigger) tb.Build();

            trigger.MisfireInstruction = misFireInstr;
            trigger.SetNextFireTimeUtc(nextFireTimeUtc);
            trigger.SetPreviousFireTimeUtc(previousFireTimeUtc);

            SetTriggerStateProperties(trigger, triggerProps);
        }

        return trigger;
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

        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        // signal cancel, otherwise ADO.NET might have trouble handling partial reads from open reader
        var shouldStop = false;
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (shouldStop)
            {
                cmd.Cancel();
                break;
            }

            if (nextTriggers.Count < maxCount)
            {
                var result = new TriggerAcquireResult(
                    (string) rs[ColumnTriggerName],
                    (string) rs[ColumnTriggerGroup],
                    (string) rs[ColumnJobClass]);
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
}