﻿using System.Data;
using System.Data.Common;
using System.Reflection;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Logging;
using Quartz.Spi;
using Quartz.Util;
using Quartz.Web.Api.Dto;

namespace Quartz.Web.History;

public class JobHistoryDelegate
{
    private static readonly ILogger<JobHistoryDelegate> log = LogProvider.CreateLogger<JobHistoryDelegate>();
    private StdAdoDelegate? driverDelegate;
    private readonly string delegateTypeName;
    private Type delegateType = typeof (StdAdoDelegate);
    private readonly string tablePrefix;
    private readonly string dataSource;

    private const string SqlInsertJobExecuted =
        "INSERT INTO {0}JOB_HISTORY (SCHED_NAME, INSTANCE_NAME, TRIGGER_NAME, TRIGGER_GROUP, JOB_NAME, JOB_GROUP, SCHED_TIME, FIRED_TIME, RUN_TIME, ERROR, ERROR_MESSAGE)  VALUES (@schedulerName, @instanceName, @triggerName, @triggerGroup, @jobName, @jobGroup, @scheduledTime, @firedTime, @runTime, @error, @errorMessage)";

    private const string SqlSelectHistoryEntry =
        "SELECT TOP 25 SCHED_NAME, INSTANCE_NAME, TRIGGER_NAME, TRIGGER_GROUP, JOB_NAME, JOB_GROUP, FIRED_TIME, SCHED_TIME, RUN_TIME, ERROR, ERROR_MESSAGE FROM {0}JOB_HISTORY WHERE SCHED_NAME = @schedulerName";

    public JobHistoryDelegate(string dataSource, string delegateTypeName, string tablePrefix)
    {
        this.dataSource = dataSource;
        this.delegateTypeName = delegateTypeName;
        this.tablePrefix = tablePrefix ?? AdoConstants.DefaultTablePrefix;
    }

    /// <summary>
    /// Get the driver delegate for DB operations.
    /// </summary>
    protected virtual IDbAccessor Delegate
    {
        get
        {
            lock (this)
            {
                if (driverDelegate == null)
                {
                    try
                    {
                        if (delegateTypeName != null)
                        {
                            delegateType = TypeLoadHelper!.LoadType(delegateTypeName)!;
                        }

                        IDbProvider dbProvider = DBConnectionManager.Instance.GetDbProvider(dataSource);
                        var args = new DelegateInitializationArgs();
                        args.DbProvider = dbProvider;

                        ConstructorInfo ctor = delegateType.GetConstructor(Type.EmptyTypes)!;
                        if (ctor == null)
                        {
                            throw new InvalidConfigurationException("Configured delegate does not have public constructor that takes no arguments");
                        }

                        driverDelegate = (StdAdoDelegate) ctor.Invoke(null);
                        driverDelegate.Initialize(args);
                    }
                    catch (Exception e)
                    {
                        throw new NoSuchDelegateException("Couldn't instantiate delegate: " + e.Message, e);
                    }
                }
            }
            return driverDelegate;
        }
    }

    public ITypeLoadHelper? TypeLoadHelper { get; set; }

    public async ValueTask InsertJobHistoryEntry(
        IJobExecutionContext context,
        JobExecutionException? jobException,
        CancellationToken cancellationToken = default)
    {
        var sql = AdoJobStoreUtil.ReplaceTablePrefix(SqlInsertJobExecuted, tablePrefix, "");
        using var connection = GetConnection(IsolationLevel.ReadUncommitted);
        await using var command = Delegate.PrepareCommand(connection, sql);
        Delegate.AddCommandParameter(command, "schedulerName", context.Scheduler.SchedulerName);
        Delegate.AddCommandParameter(command, "instanceName", context.Scheduler.SchedulerInstanceId);
        Delegate.AddCommandParameter(command, "jobName", context.JobDetail.Key.Name);
        Delegate.AddCommandParameter(command, "jobGroup", context.JobDetail.Key.Group);
        Delegate.AddCommandParameter(command, "triggerName", context.Trigger.Key.Name);
        Delegate.AddCommandParameter(command, "triggerGroup", context.Trigger.Key.Group);
        Delegate.AddCommandParameter(command, "scheduledTime", Delegate.GetDbDateTimeValue(context.ScheduledFireTimeUtc));
        Delegate.AddCommandParameter(command, "firedTime", Delegate.GetDbDateTimeValue(context.FireTimeUtc));
        Delegate.AddCommandParameter(command, "runTime", Delegate.GetDbTimeSpanValue(context.JobRunTime));
        Delegate.AddCommandParameter(command, "error", Delegate.GetDbBooleanValue(jobException != null));
        Delegate.AddCommandParameter(command, "errorMessage", jobException?.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        connection.Commit(false);
    }

    public async ValueTask<IReadOnlyList<JobHistoryEntryDto>> SelectJobHistoryEntries(
        string schedulerName,
        CancellationToken cancellationToken = default)
    {
        var sql = AdoJobStoreUtil.ReplaceTablePrefix(SqlSelectHistoryEntry, tablePrefix, "");
        List<JobHistoryEntryDto> entries = new();
        using var dbConnection = GetConnection(IsolationLevel.ReadUncommitted);
        await using var dbCommand = Delegate.PrepareCommand(dbConnection, sql);
        Delegate.AddCommandParameter(dbCommand, "schedulerName", schedulerName);
        await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            JobHistoryEntryDto entry = new()
            {
                JobName = reader.GetString("JOB_NAME"),
                JobGroup = reader.GetString("JOB_GROUP"),
                TriggerName = reader.GetString("TRIGGER_NAME"),
                TriggerGroup = reader.GetString("TRIGGER_GROUP"),
                FiredTime = Delegate.GetDateTimeFromDbValue(reader["FIRED_TIME"]).GetValueOrDefault(),
                ScheduledTime = Delegate.GetDateTimeFromDbValue(reader["SCHED_TIME"]).GetValueOrDefault(),
                RunTime = Delegate.GetTimeSpanFromDbValue(reader["RUN_TIME"]).GetValueOrDefault(),
                Error = Delegate.GetBooleanFromDbValue(reader["ERROR"]),
                ErrorMessage = reader.GetString("ERROR_MESSAGE")
            };
            entries.Add(entry);
        }

        return entries;
    }

    /// <summary>
    /// Gets the connection and starts a new transaction.
    /// </summary>
    /// <param name="isolationLevel"></param>
    /// <returns></returns>
    protected virtual ConnectionAndTransactionHolder GetConnection(IsolationLevel isolationLevel)
    {
        DbConnection conn;
        DbTransaction tx;
        try
        {
            conn = DBConnectionManager.Instance.GetConnection(dataSource);
            conn.Open();
        }
        catch (Exception e)
        {
            throw new JobPersistenceException(
                $"Failed to obtain DB connection from data source '{dataSource}': {e}", e);
        }
        if (conn == null)
        {
            throw new JobPersistenceException($"Could not get connection from DataSource '{dataSource}'");
        }

        try
        {
            tx = conn.BeginTransaction(isolationLevel);
        }
        catch (Exception e)
        {
            conn.Close();
            throw new JobPersistenceException("Failure setting up connection.", e);
        }

        return new ConnectionAndTransactionHolder(conn, tx);
    }
}