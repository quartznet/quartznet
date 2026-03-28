using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Impl.Matchers;
using Quartz.Impl.Triggers;
using Quartz.Logging;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Impl.AdoJobStore;

public partial class StdAdoDelegate
{
    // Parameter name constants for trigger-related queries
    private const string ParamBlob = "blob";
    private const string ParamCalendarName = "calendarName";
    private const string ParamFireTime = "fireTime";
    private const string ParamGroupName = "groupName";
    private const string ParamInstanceName = "instanceName";
    private const string ParamJobGroup = "jobGroup";
    private const string ParamJobName = "jobName";
    private const string ParamNewState = "newState";
    private const string ParamNextFireTime = "nextFireTime";
    private const string ParamNoEarlierThan = "noEarlierThan";
    private const string ParamNoLaterThan = "noLaterThan";
    private const string ParamOldState = "oldState";
    private const string ParamOldState1 = "oldState1";
    private const string ParamOldState2 = "oldState2";
    private const string ParamOldState3 = "oldState3";
    private const string ParamRequestsRecovery = "requestsRecovery";
    private const string ParamSchedulerName = "schedulerName";
    private const string ParamState = "state";
    private const string ParamState1 = "state1";
    private const string ParamTimestamp = "timestamp";
    private const string ParamTriggerCalendarName = "triggerCalendarName";
    private const string ParamTriggerDescription = "triggerDescription";
    private const string ParamTriggerEndTime = "triggerEndTime";
    private const string ParamTriggerEntryId = "triggerEntryId";
    private const string ParamTriggerFireTime = "triggerFireTime";
    private const string ParamTriggerGroup = "triggerGroup";
    private const string ParamTriggerInstanceName = "triggerInstanceName";
    private const string ParamTriggerJobGroup = "triggerJobGroup";
    private const string ParamTriggerJobJobDataMap = "triggerJobJobDataMap";
    private const string ParamTriggerJobName = "triggerJobName";
    private const string ParamTriggerJobRequestsRecovery = "triggerJobRequestsRecovery";
    private const string ParamTriggerJobStateful = "triggerJobStateful";
    private const string ParamTriggerMisfireInstruction = "triggerMisfireInstruction";
    private const string ParamTriggerName = "triggerName";
    private const string ParamTriggerNextFireTime = "triggerNextFireTime";
    private const string ParamTriggerPreviousFireTime = "triggerPreviousFireTime";
    private const string ParamTriggerPriority = "triggerPriority";
    private const string ParamTriggerScheduledTime = "triggerScheduledTime";
    private const string ParamTriggerStartTime = "triggerStartTime";
    private const string ParamTriggerState = "triggerState";
    private const string ParamTriggerType = "triggerType";
    /// <inheritdoc />
    public virtual async Task<int> UpdateTriggerStatesFromOtherStates(
        ConnectionAndTransactionHolder conn,
        string newState,
        string oldState1,
        string oldState2,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateTriggerStatesFromOtherStates));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamNewState, newState);
        AddCommandParameter(cmd, ParamOldState1, oldState1);
        AddCommandParameter(cmd, ParamOldState2, oldState2);
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyCollection<TriggerKey>> SelectMisfiredTriggers(
        ConnectionAndTransactionHolder conn,
        DateTimeOffset ts,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectMisfiredTriggers));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamTimestamp, GetDbDateTimeValue(ts));
        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        List<TriggerKey> list = new List<TriggerKey>();
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string triggerName = rs.GetString(ColumnTriggerName)!;
            string groupName = rs.GetString(ColumnTriggerGroup)!;
            list.Add(new TriggerKey(triggerName, groupName));
        }

        return list;
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyCollection<TriggerKey>> SelectTriggersInState(
        ConnectionAndTransactionHolder conn,
        string state,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggersInState));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamState, state);
        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        List<TriggerKey> list = new List<TriggerKey>();
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new TriggerKey(rs.GetString(0), rs.GetString(1)));
        }

        return list;
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyCollection<TriggerKey>> HasMisfiredTriggersInState(
        ConnectionAndTransactionHolder conn,
        string state,
        DateTimeOffset ts,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectMisfiredTriggersInState));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamTimestamp, GetDbDateTimeValue(ts));
        AddCommandParameter(cmd, ParamState, state);

        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        List<TriggerKey> list = new List<TriggerKey>();
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string triggerName = rs.GetString(ColumnTriggerName)!;
            string groupName = rs.GetString(ColumnTriggerGroup)!;
            list.Add(new TriggerKey(triggerName, groupName));
        }

        return list;
    }

    /// <inheritdoc />
    public virtual async Task<bool> HasMisfiredTriggersInState(
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
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamNextFireTime, GetDbDateTimeValue(ts));
        AddCommandParameter(cmd, ParamState1, state1);

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
    public virtual async Task<int> CountMisfiredTriggersInState(
        ConnectionAndTransactionHolder conn,
        string state1,
        DateTimeOffset ts,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlCountMisfiredTriggersInStates));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamNextFireTime, GetDbDateTimeValue(ts));
        AddCommandParameter(cmd, ParamState1, state1);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyCollection<TriggerKey>> SelectMisfiredTriggersInGroupInState(
        ConnectionAndTransactionHolder conn,
        string groupName,
        string state,
        DateTimeOffset ts,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectMisfiredTriggersInGroupInState));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamTimestamp, GetDbDateTimeValue(ts));
        AddCommandParameter(cmd, ParamTriggerGroup, groupName);
        AddCommandParameter(cmd, ParamState, state);

        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        List<TriggerKey> list = new List<TriggerKey>();
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string triggerName = rs.GetString(ColumnTriggerName)!;
            list.Add(new TriggerKey(triggerName, groupName));
        }

        return list;
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyCollection<IOperableTrigger>> SelectTriggersForRecoveringJobs(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        List<IOperableTrigger> triggers = new List<IOperableTrigger>();
        List<FiredTriggerRecord> triggerData = new List<FiredTriggerRecord>();
        List<TriggerKey> keys = new List<TriggerKey>();

        using (var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectInstancesRecoverableFiredTriggers)))
        {
            AddCommandParameter(cmd, ParamSchedulerName, schedName);
            AddCommandParameter(cmd, ParamInstanceName, instanceId);
            AddCommandParameter(cmd, ParamRequestsRecovery, GetDbBooleanValue(true));

            using (var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                long dumId = SystemTime.UtcNow().Ticks;

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
                    rcvryTrig.JobName = jobName;
                    rcvryTrig.JobGroup = jobGroup;
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
    public virtual async Task<int> DeleteFiredTriggers(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteFiredTriggers));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<int> DeleteFiredTriggers(
        ConnectionAndTransactionHolder conn,
        string instanceName,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteInstancesFiredTriggers));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamInstanceName, instanceName);
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<int> InsertTrigger(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        string state,
        IJobDetail jobDetail,
        CancellationToken cancellationToken = default)
    {
        var jobData = SerializeJobData(trigger.JobDataMap);

        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlInsertTrigger));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamTriggerName, trigger.Key.Name);
        AddCommandParameter(cmd, ParamTriggerGroup, trigger.Key.Group);
        AddCommandParameter(cmd, ParamTriggerJobName, trigger.JobKey.Name);
        AddCommandParameter(cmd, ParamTriggerJobGroup, trigger.JobKey.Group);
        AddCommandParameter(cmd, ParamTriggerDescription, trigger.Description);
        AddCommandParameter(cmd, ParamTriggerNextFireTime, GetDbDateTimeValue(trigger.GetNextFireTimeUtc()));
        AddCommandParameter(cmd, ParamTriggerPreviousFireTime, GetDbDateTimeValue(trigger.GetPreviousFireTimeUtc()));
        AddCommandParameter(cmd, ParamTriggerState, state);

        var tDel = FindTriggerPersistenceDelegate(trigger);
        string type = TriggerTypeBlob;
        if (tDel != null)
        {
            type = tDel.GetHandledTriggerTypeDiscriminator();
        }

        AddCommandParameter(cmd, ParamTriggerType, type);
        AddCommandParameter(cmd, ParamTriggerStartTime, GetDbDateTimeValue(trigger.StartTimeUtc));
        AddCommandParameter(cmd, ParamTriggerEndTime, GetDbDateTimeValue(trigger.EndTimeUtc));
        AddCommandParameter(cmd, ParamTriggerCalendarName, trigger.CalendarName);
        AddCommandParameter(cmd, ParamTriggerMisfireInstruction, trigger.MisfireInstruction);
        AddCommandParameter(cmd, ParamTriggerJobJobDataMap, jobData, DbProvider.Metadata.DbBinaryType);

        AddCommandParameter(cmd, ParamTriggerPriority, trigger.Priority);

        int insertResult = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        if (tDel == null)
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
    public virtual async Task<int> InsertBlobTrigger(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlInsertBlobTrigger));
        // update the blob
        byte[]? buf = SerializeObject(trigger);
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamTriggerName, trigger.Key.Name);
        AddCommandParameter(cmd, ParamTriggerGroup, trigger.Key.Group);
        AddCommandParameter(cmd, ParamBlob, buf, DbProvider.Metadata.DbBinaryType);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<int> UpdateTrigger(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        string state,
        IJobDetail jobDetail,
        CancellationToken cancellationToken = default)
    {
        var existingType = await SelectTriggerType(conn, trigger.Key, cancellationToken).ConfigureAwait(false);

        // No need to continue if the trigger type is not found - there's nothing to update.
        if (existingType == null) return 0;

        // save some clock cycles by unnecessarily writing job data blob ...
        var updateJobData = trigger.JobDataMap.Dirty;
        var jobData = updateJobData ? SerializeJobData(trigger.JobDataMap) : null;

        var sqlUpdate = updateJobData ? SqlUpdateTrigger : SqlUpdateTriggerSkipData;
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(sqlUpdate));

        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamTriggerJobName, trigger.JobKey.Name);
        AddCommandParameter(cmd, ParamTriggerJobGroup, trigger.JobKey.Group);
        AddCommandParameter(cmd, ParamTriggerDescription, trigger.Description);
        AddCommandParameter(cmd, ParamTriggerNextFireTime, GetDbDateTimeValue(trigger.GetNextFireTimeUtc()));
        AddCommandParameter(cmd, ParamTriggerPreviousFireTime, GetDbDateTimeValue(trigger.GetPreviousFireTimeUtc()));

        AddCommandParameter(cmd, ParamTriggerState, state);

        var tDel = FindTriggerPersistenceDelegate(trigger);

        string type = TriggerTypeBlob;
        if (tDel != null)
        {
            type = tDel.GetHandledTriggerTypeDiscriminator();
        }

        AddCommandParameter(cmd, ParamTriggerType, type);

        AddCommandParameter(cmd, ParamTriggerStartTime, GetDbDateTimeValue(trigger.StartTimeUtc));
        AddCommandParameter(cmd, ParamTriggerEndTime, GetDbDateTimeValue(trigger.EndTimeUtc));
        AddCommandParameter(cmd, ParamTriggerCalendarName, trigger.CalendarName);
        AddCommandParameter(cmd, ParamTriggerMisfireInstruction, trigger.MisfireInstruction);
        AddCommandParameter(cmd, ParamTriggerPriority, trigger.Priority);

        const string JobDataMapParameter = "triggerJobJobDataMap";
        if (updateJobData)
        {
            AddCommandParameter(cmd, JobDataMapParameter, jobData, DbProvider.Metadata.DbBinaryType);
            AddCommandParameter(cmd, ParamTriggerName, trigger.Key.Name);
            AddCommandParameter(cmd, ParamTriggerGroup, trigger.Key.Group);
        }
        else
        {
            AddCommandParameter(cmd, ParamTriggerName, trigger.Key.Name);
            AddCommandParameter(cmd, ParamTriggerGroup, trigger.Key.Group);
        }

        var updateResult = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        if (type == existingType)
        {
            if (tDel == null)
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

            if (existingDel == null)
            {
                await DeleteBlobTrigger(conn, trigger.Key, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await existingDel.DeleteExtendedTriggerProperties(conn, trigger.Key, cancellationToken).ConfigureAwait(false);
            }

            if (tDel == null)
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
    public virtual async Task<int> UpdateBlobTrigger(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateBlobTrigger));
        // update the blob
        byte[]? os = SerializeObject(trigger);

        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamBlob, os, DbProvider.Metadata.DbBinaryType);
        AddCommandParameter(cmd, ParamTriggerName, trigger.Key.Name);
        AddCommandParameter(cmd, ParamTriggerGroup, trigger.Key.Group);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<int> UpdateTriggerState(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        string state,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateTriggerState));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamState, state);
        AddCommandParameter(cmd, ParamTriggerName, triggerKey.Name);
        AddCommandParameter(cmd, ParamTriggerGroup, triggerKey.Group);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<int> UpdateTriggerStateFromOtherStates(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        string newState,
        string oldState1,
        string oldState2,
        string oldState3,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateTriggerStateFromStates));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamNewState, newState);
        AddCommandParameter(cmd, ParamTriggerName, triggerKey.Name);
        AddCommandParameter(cmd, ParamTriggerGroup, triggerKey.Group);
        AddCommandParameter(cmd, ParamOldState1, oldState1);
        AddCommandParameter(cmd, ParamOldState2, oldState2);
        AddCommandParameter(cmd, ParamOldState3, oldState3);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<int> UpdateTriggerGroupStateFromOtherStates(
        ConnectionAndTransactionHolder conn,
        GroupMatcher<TriggerKey> matcher,
        string newState,
        string oldState1,
        string oldState2,
        string oldState3,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateTriggerGroupStateFromStates));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamNewState, newState);
        AddCommandParameter(cmd, ParamGroupName, ToSqlLikeClause(matcher));
        AddCommandParameter(cmd, ParamOldState1, oldState1);
        AddCommandParameter(cmd, ParamOldState2, oldState2);
        AddCommandParameter(cmd, ParamOldState3, oldState3);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<int> UpdateTriggerStateFromOtherState(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        string newState,
        string oldState,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateTriggerStateFromState));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamNewState, newState);
        AddCommandParameter(cmd, ParamTriggerName, triggerKey.Name);
        AddCommandParameter(cmd, ParamTriggerGroup, triggerKey.Group);
        AddCommandParameter(cmd, ParamOldState, oldState);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> UpdateTriggerStateFromOtherStateWithNextFireTime(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        string newState,
        string oldState,
        DateTimeOffset nextFireTime,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateTriggerStateFromStateWithNextFireTime));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamNewState, newState);
        AddCommandParameter(cmd, ParamTriggerName, triggerKey.Name);
        AddCommandParameter(cmd, ParamTriggerGroup, triggerKey.Group);
        AddCommandParameter(cmd, ParamOldState, oldState);
        AddCommandParameter(cmd, ParamNextFireTime, GetDbDateTimeValue(nextFireTime));

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<int> UpdateTriggerGroupStateFromOtherState(
        ConnectionAndTransactionHolder conn,
        GroupMatcher<TriggerKey> matcher,
        string newState,
        string oldState,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateTriggerGroupStateFromState));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamNewState, newState);
        AddCommandParameter(cmd, ParamTriggerGroup, ToSqlLikeClause(matcher));
        AddCommandParameter(cmd, ParamOldState, oldState);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<int> UpdateTriggerStatesForJob(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        string state,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateJobTriggerStates));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamState, state);
        AddCommandParameter(cmd, ParamJobName, jobKey.Name);
        AddCommandParameter(cmd, ParamJobGroup, jobKey.Group);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<int> UpdateTriggerStatesForJobFromOtherState(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        string state,
        string oldState,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateJobTriggerStatesFromOtherState));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamState, state);
        AddCommandParameter(cmd, ParamJobName, jobKey.Name);
        AddCommandParameter(cmd, ParamJobGroup, jobKey.Group);
        AddCommandParameter(cmd, ParamOldState, oldState);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<int> DeleteBlobTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteBlobTrigger));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamTriggerName, triggerKey.Name);
        AddCommandParameter(cmd, ParamTriggerGroup, triggerKey.Group);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<int> DeleteTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        await DeleteTriggerExtension(conn, triggerKey, cancellationToken).ConfigureAwait(false);

        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteTrigger));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamTriggerName, triggerKey.Name);
        AddCommandParameter(cmd, ParamTriggerGroup, triggerKey.Group);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    protected virtual async Task DeleteTriggerExtension(
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
    public virtual async Task<IOperableTrigger?> SelectTrigger(
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
            AddCommandParameter(cmd, ParamSchedulerName, schedName);
            AddCommandParameter(cmd, ParamTriggerName, triggerKey.Name);
            AddCommandParameter(cmd, ParamTriggerGroup, triggerKey.Group);

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
                if (triggerType.Equals(TriggerTypeCron) || triggerType.Equals(TriggerTypeSimple))
                {
                    tDel = FindTriggerPersistenceDelegate(triggerType);
                    triggerProps = tDel!.ReadTriggerPropertyBundle(rs);
                }
            }
        }

        IOperableTrigger? trigger = null;
        if (triggerType.Equals(TriggerTypeBlob))
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

                if (tDel == null)
                {
                    throw new JobPersistenceException("No TriggerPersistenceDelegate for trigger discriminator type: " + triggerType);
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

            if (map != null)
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

    private async Task<bool> IsTriggerStillPresent(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTrigger));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamTriggerName, triggerKey.Name);
        AddCommandParameter(cmd, ParamTriggerGroup, triggerKey.Group);

        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await rs.ReadAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void SetTriggerStateProperties(IOperableTrigger trigger, TriggerPropertyBundle props)
    {
        if (props.StatePropertyNames == null)
        {
            return;
        }

        ObjectUtils.SetObjectProperties(trigger, props.StatePropertyNames, props.StatePropertyValues);
    }

    /// <inheritdoc />
    public virtual async Task<JobDataMap> SelectTriggerJobDataMap(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggerData));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamTriggerName, triggerKey.Name);
        AddCommandParameter(cmd, ParamTriggerGroup, triggerKey.Group);

        using var rs = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false);
        if (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var map = await ReadMapFromReader(rs, 0).ConfigureAwait(false);
            if (map != null)
            {
                return map as JobDataMap ?? new JobDataMap(map);
            }
        }

        return new JobDataMap();
    }

    /// <inheritdoc />
    public virtual async Task<string> SelectTriggerState(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggerState));

        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamTriggerName, triggerKey.Name);
        AddCommandParameter(cmd, ParamTriggerGroup, triggerKey.Group);

        var state = (string?) await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        return state ?? StateDeleted;
    }

    /// <inheritdoc />
    public virtual async Task<TriggerStatus?> SelectTriggerStatus(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggerStatus));
        TriggerStatus? status = null;

        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamTriggerName, triggerKey.Name);
        AddCommandParameter(cmd, ParamTriggerGroup, triggerKey.Group);
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

    private async Task<string?> SelectTriggerType(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggerType));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamTriggerName, triggerKey.Name);
        AddCommandParameter(cmd, ParamTriggerGroup, triggerKey.Group);

        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return rs.GetString(ColumnTriggerType)!;
        }
        return null;
    }

    /// <inheritdoc />
    public virtual async Task<int> SelectNumTriggers(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectNumTriggers));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);

        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        return count;
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyCollection<string>> SelectTriggerGroups(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggerGroups));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);

        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        List<string> list = new List<string>();
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add((string) rs[0]);
        }

        return list;
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyCollection<string>> SelectTriggerGroups(
        ConnectionAndTransactionHolder conn,
        GroupMatcher<TriggerKey> matcher,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggerGroupsFiltered));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamTriggerGroup, ToSqlLikeClause(matcher));
        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        List<string> list = new List<string>();
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add((string) rs[0]);
        }

        return list;
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyCollection<TriggerKey>> SelectTriggersInGroup(
        ConnectionAndTransactionHolder conn,
        GroupMatcher<TriggerKey> matcher,
        CancellationToken cancellationToken = default)
    {
        string sql;
        string parameter;
        if (IsMatcherEquals(matcher))
        {
            sql = ReplaceTablePrefix(SqlSelectTriggersInGroup);
            parameter = ToSqlEqualsClause(matcher);
        }
        else
        {
            sql = ReplaceTablePrefix(SqlSelectTriggersInGroupLike);
            parameter = ToSqlLikeClause(matcher);
        }

        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(sql));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamTriggerGroup, parameter);
        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var keys = new HashSet<TriggerKey>();
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            keys.Add(new TriggerKey(rs.GetString(0), rs.GetString(1)));
        }

        return keys;
    }

    /// <inheritdoc />
    public virtual async Task<int> InsertPausedTriggerGroup(
        ConnectionAndTransactionHolder conn,
        string groupName,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlInsertPausedTriggerGroup));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamTriggerGroup, groupName);
        int rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return rows;
    }

    /// <inheritdoc />
    public virtual async Task<int> DeletePausedTriggerGroup(
        ConnectionAndTransactionHolder conn,
        string groupName,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlDeletePausedTriggerGroup));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamTriggerGroup, groupName);
        int rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return rows;
    }

    /// <inheritdoc />
    public virtual async Task<int> DeletePausedTriggerGroup(
        ConnectionAndTransactionHolder conn,
        GroupMatcher<TriggerKey> matcher,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlDeletePausedTriggerGroup));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamTriggerGroup, ToSqlLikeClause(matcher));
        int rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return rows;
    }

    /// <inheritdoc />
    public virtual async Task<int> DeleteAllPausedTriggerGroups(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlDeletePausedTriggerGroups));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        int rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return rows;
    }

    /// <inheritdoc />
    public virtual async Task<bool> IsTriggerGroupPaused(
        ConnectionAndTransactionHolder conn,
        string groupName,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectPausedTriggerGroup));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamTriggerGroup, groupName);

        return await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) != null;
    }

    /// <inheritdoc />
    public virtual async Task<bool> IsExistingTriggerGroup(
        ConnectionAndTransactionHolder conn,
        string groupName,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectNumTriggersInGroup));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamTriggerGroup, groupName);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) > 0;
    }

    /// <inheritdoc />
    public virtual async Task<TriggerKey?> SelectTriggerForFireTime(
        ConnectionAndTransactionHolder conn,
        DateTimeOffset fireTime,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggerForFireTime));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamState, StateWaiting);
        AddCommandParameter(cmd, ParamFireTime, GetDbDateTimeValue(fireTime));

        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return new TriggerKey(rs.GetString(ColumnTriggerName)!, rs.GetString(ColumnTriggerGroup)!);
        }

        return null;
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyCollection<TriggerAcquireResult>> SelectTriggerToAcquire(
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

        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamState, StateWaiting);
        AddCommandParameter(cmd, ParamNoLaterThan, GetDbDateTimeValue(noLaterThan));
        AddCommandParameter(cmd, ParamNoEarlierThan, GetDbDateTimeValue(noEarlierThan));

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
    public virtual async Task<int> InsertFiredTrigger(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        string state,
        IJobDetail? job,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlInsertFiredTrigger));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamTriggerEntryId, trigger.FireInstanceId);
        AddCommandParameter(cmd, ParamTriggerName, trigger.Key.Name);
        AddCommandParameter(cmd, ParamTriggerGroup, trigger.Key.Group);
        AddCommandParameter(cmd, ParamTriggerInstanceName, instanceId);
        AddCommandParameter(cmd, ParamTriggerFireTime, GetDbDateTimeValue(SystemTime.UtcNow()));
        AddCommandParameter(cmd, ParamTriggerScheduledTime, GetDbDateTimeValue(trigger.GetNextFireTimeUtc()));
        AddCommandParameter(cmd, ParamTriggerState, state);
        if (job != null)
        {
            AddCommandParameter(cmd, ParamTriggerJobName, trigger.JobKey.Name);
            AddCommandParameter(cmd, ParamTriggerJobGroup, trigger.JobKey.Group);
            AddCommandParameter(cmd, ParamTriggerJobStateful, GetDbBooleanValue(job.ConcurrentExecutionDisallowed));
            AddCommandParameter(cmd, ParamTriggerJobRequestsRecovery, GetDbBooleanValue(job.RequestsRecovery));
        }
        else
        {
            AddCommandParameter(cmd, ParamTriggerJobName, null);
            AddCommandParameter(cmd, ParamTriggerJobGroup, null);
            AddCommandParameter(cmd, ParamTriggerJobStateful, GetDbBooleanValue(false));
            AddCommandParameter(cmd, ParamTriggerJobRequestsRecovery, GetDbBooleanValue(false));
        }

        AddCommandParameter(cmd, ParamTriggerPriority, trigger.Priority);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual Task<int> UpdateFiredTrigger(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        string state,
        IJobDetail job,
        CancellationToken cancellationToken = default)
    {
        var ps = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateFiredTrigger));
        AddCommandParameter(ps, "schedulerName", schedName);
        AddCommandParameter(ps, "instanceName", instanceId);
        AddCommandParameter(ps, "firedTime", GetDbDateTimeValue(SystemTime.UtcNow()));
        AddCommandParameter(ps, "scheduledTime", GetDbDateTimeValue(trigger.GetNextFireTimeUtc()));
        AddCommandParameter(ps, "entryState", state);
        AddCommandParameter(ps, "jobName", trigger.JobKey.Name);
        AddCommandParameter(ps, "jobGroup", trigger.JobKey.Group);
        AddCommandParameter(ps, "isNonConcurrent", GetDbBooleanValue(job.ConcurrentExecutionDisallowed));
        AddCommandParameter(ps, "requestsRecover", GetDbBooleanValue(job.RequestsRecovery));
        AddCommandParameter(ps, "entryId", trigger.FireInstanceId);

        return ps.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyCollection<FiredTriggerRecord>> SelectFiredTriggerRecords(
        ConnectionAndTransactionHolder conn,
        string triggerName,
        string groupName,
        CancellationToken cancellationToken = default)
    {
        DbCommand cmd;

        List<FiredTriggerRecord> lst = new List<FiredTriggerRecord>();

        if (triggerName != null)
        {
            cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectFiredTrigger));
            AddCommandParameter(cmd, ParamSchedulerName, schedName);
            AddCommandParameter(cmd, ParamTriggerName, triggerName);
            AddCommandParameter(cmd, ParamTriggerGroup, groupName);
        }
        else
        {
            cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectFiredTriggerGroup));
            AddCommandParameter(cmd, ParamSchedulerName, schedName);
            AddCommandParameter(cmd, ParamTriggerGroup, groupName);
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
            if (!rec.FireInstanceState.Equals(StateAcquired))
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
    public virtual async Task<IReadOnlyCollection<FiredTriggerRecord>> SelectFiredTriggerRecordsByJob(
        ConnectionAndTransactionHolder conn,
        string jobName,
        string groupName,
        CancellationToken cancellationToken = default)
    {
        List<FiredTriggerRecord> lst = new List<FiredTriggerRecord>();

        DbCommand cmd;
        if (jobName != null)
        {
            cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectFiredTriggersOfJob));
            AddCommandParameter(cmd, ParamSchedulerName, schedName);
            AddCommandParameter(cmd, ParamJobName, jobName);
            AddCommandParameter(cmd, ParamJobGroup, groupName);
        }
        else
        {
            cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectFiredTriggersOfJobGroup));
            AddCommandParameter(cmd, ParamSchedulerName, schedName);
            AddCommandParameter(cmd, ParamJobGroup, groupName);
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
            if (!rec.FireInstanceState.Equals(StateAcquired))
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
    public virtual async Task<IReadOnlyCollection<FiredTriggerRecord>> SelectInstancesFiredTriggerRecords(
        ConnectionAndTransactionHolder conn,
        string instanceName,
        CancellationToken cancellationToken = default)
    {
        List<FiredTriggerRecord> lst = new List<FiredTriggerRecord>();

        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectInstancesFiredTriggers));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamInstanceName, instanceName);
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
            if (!rec.FireInstanceState.Equals(StateAcquired))
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
    public virtual async Task<IReadOnlyCollection<string>> SelectFiredTriggerInstanceNames(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        var instanceNames = new HashSet<string>();
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectFiredTriggerInstanceNames));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            instanceNames.Add(rs.GetString(ColumnInstanceName)!);
        }

        return instanceNames;
    }

    /// <inheritdoc />
    public virtual async Task<int> DeleteFiredTrigger(
        ConnectionAndTransactionHolder conn,
        string entryId,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteFiredTrigger));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamTriggerEntryId, entryId);
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public virtual void AddTriggerPersistenceDelegate(ITriggerPersistenceDelegate del)
    {
        logger.Debug("Adding TriggerPersistenceDelegate of type: " + del.GetType());
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
    public virtual async Task<bool> TriggerExists(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggerExistence));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamTriggerName, triggerKey.Name);
        AddCommandParameter(cmd, ParamTriggerGroup, triggerKey.Group);

        using var dr = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await dr.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public virtual async Task<int> SelectNumTriggersForJob(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectNumTriggersForJob));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        AddCommandParameter(cmd, ParamJobName, jobKey.Name);
        AddCommandParameter(cmd, ParamJobGroup, jobKey.Group);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyCollection<IOperableTrigger>> SelectTriggersForJob(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        List<IOperableTrigger> trigList = new List<IOperableTrigger>();
        List<TriggerKey> keys = new List<TriggerKey>();

        using (var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggersForJob)))
        {
            AddCommandParameter(cmd, ParamSchedulerName, schedName);
            AddCommandParameter(cmd, ParamJobName, jobKey.Name);
            AddCommandParameter(cmd, ParamJobGroup, jobKey.Group);

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
            if (t != null)
            {
                trigList.Add(t);
            }
        }

        return trigList;
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyCollection<IOperableTrigger>> SelectTriggersForCalendar(
        ConnectionAndTransactionHolder conn,
        string calName,
        CancellationToken cancellationToken = default)
    {
        List<TriggerKey> keys = new List<TriggerKey>();
        using (var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggersForCalendar)))
        {
            AddCommandParameter(cmd, ParamSchedulerName, schedName);
            AddCommandParameter(cmd, ParamCalendarName, calName);
            using (var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    keys.Add(new TriggerKey(rs.GetString(ColumnTriggerName)!, rs.GetString(ColumnTriggerGroup)!));
                }
            }
        }

        var triggers = new List<IOperableTrigger>();
        foreach (var key in keys)
        {
            var trigger = await SelectTrigger(conn, key, cancellationToken).ConfigureAwait(false);
            if (trigger != null)
            {
                triggers.Add(trigger);
            }
        }
        return triggers;
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyCollection<string>> SelectPausedTriggerGroups(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        var retValue = new HashSet<string>();
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectPausedTriggerGroups));
        AddCommandParameter(cmd, ParamSchedulerName, schedName);
        using var dr = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await dr.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string groupName = (string) dr[ColumnTriggerGroup];
            retValue.Add(groupName);
        }

        return retValue;
    }
}