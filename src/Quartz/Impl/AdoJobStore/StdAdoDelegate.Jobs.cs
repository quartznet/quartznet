using System.Data.Common;
using System.Runtime.Serialization;

using Microsoft.Extensions.Logging;

using Quartz.Spi;
using Quartz.Util;

using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Quartz.Impl.AdoJobStore;

public partial class StdAdoDelegate
{


    /// <inheritdoc />
    public virtual async ValueTask<int> UpdateJobDetail(
        ConnectionAndTransactionHolder conn,
        IJobDetail job,
        CancellationToken cancellationToken = default)
    {
        var jobData = SerializeJobData(job.JobDataMap);

        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateJobDetail));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "jobDescription", job.Description);
        AddCommandParameter(cmd, "jobType", job.JobType.FullName);
        AddCommandParameter(cmd, "jobDurable", GetDbBooleanValue(job.Durable));
        AddCommandParameter(cmd, "jobVolatile", GetDbBooleanValue(job.ConcurrentExecutionDisallowed));
        AddCommandParameter(cmd, "jobStateful", GetDbBooleanValue(job.PersistJobDataAfterExecution));
        AddCommandParameter(cmd, "jobRequestsRecovery", GetDbBooleanValue(job.RequestsRecovery));
        AddCommandParameter(cmd, "jobDataMap", jobData, DbProvider.Metadata.DbBinaryType);
        AddCommandParameter(cmd, "jobName", job.Key.Name);
        AddCommandParameter(cmd, "jobGroup", job.Key.Group);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<List<TriggerKey>> SelectTriggerNamesForJob(ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggersForJob));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "jobName", jobKey.Name);
        AddCommandParameter(cmd, "jobGroup", jobKey.Group);
        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        List<TriggerKey> list = [];
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string trigName = rs.GetString(ColumnTriggerName)!;
            string trigGroup = rs.GetString(ColumnTriggerGroup)!;
            list.Add(new(trigName, trigGroup));
        }

        return list;
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> DeleteJobDetail(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteJobDetail));
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Deleting job: {JobKey}", jobKey);
        }

        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "jobName", jobKey.Name);
        AddCommandParameter(cmd, "jobGroup", jobKey.Group);
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<bool> JobExists(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectJobExistence));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "jobName", jobKey.Name);
        AddCommandParameter(cmd, "jobGroup", jobKey.Group);
        using var dr = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await dr.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> UpdateJobData(
        ConnectionAndTransactionHolder conn,
        IJobDetail job,
        CancellationToken cancellationToken = default)
    {
        var jobData = SerializeJobData(job.JobDataMap);

        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateJobData));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "jobDataMap", jobData, DbProvider.Metadata.DbBinaryType);
        AddCommandParameter(cmd, "jobName", job.Key.Name);
        AddCommandParameter(cmd, "jobGroup", job.Key.Group);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<IJobDetail?> SelectJobDetail(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        ITypeLoadHelper loadHelper,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectJobDetail));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "jobName", jobKey.Name);
        AddCommandParameter(cmd, "jobGroup", jobKey.Group);
        using var rs = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false);
        IJobDetail? job = null;

        if (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // Due to CommandBehavior.SequentialAccess, columns must be read in order.

            var jobBuilder = JobBuilder.Create()
                .WithIdentity(new JobKey(rs.GetString(ColumnJobName)!, rs.GetString(ColumnJobGroup)!))
                .WithDescription(rs.GetString(ColumnDescription))
                .OfType(rs.GetString(ColumnJobClass)!)
                .StoreDurably(GetBooleanFromDbValue(rs[ColumnIsDurable]))
                .RequestRecovery(GetBooleanFromDbValue(rs[ColumnRequestsRecovery]));

            var map = await ReadMapFromReader(rs, 6).ConfigureAwait(false);

            if (map is not null)
            {
                jobBuilder.SetJobData(new(map));
            }

            jobBuilder.DisallowConcurrentExecution(GetBooleanFromDbValue(rs[ColumnIsNonConcurrent]))
                .PersistJobDataAfterExecution(GetBooleanFromDbValue(rs[ColumnIsUpdateData]));

            job = jobBuilder.Build();
        }

        return job;
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> SelectNumJobs(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectNumJobs));
        AddCommandParameter(cmd, "schedulerName", schedName);
        var o = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (o is not null)
        {
            return (int) o;
        }

        return 0;
    }

    /// <inheritdoc />
    public virtual async ValueTask<List<string>> SelectJobGroups(ConnectionAndTransactionHolder conn, CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectJobGroups));
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
    public virtual ValueTask<IJobDetail?> SelectJobForTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        ITypeLoadHelper loadHelper,
        CancellationToken cancellationToken = default)
    {
        return SelectJobForTrigger(conn, triggerKey, loadHelper, true, cancellationToken);
    }

    /// <inheritdoc />
    public virtual async ValueTask<IJobDetail?> SelectJobForTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        ITypeLoadHelper loadHelper,
        bool loadJobType,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectJobForTrigger));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);
        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var jobBuilder = JobBuilder.Create()
                .WithIdentity(new JobKey(rs.GetString(ColumnJobName)!, rs.GetString(ColumnJobGroup)!))
                .RequestRecovery(GetBooleanFromDbValue(rs[ColumnRequestsRecovery]))
                .OfType(rs.GetString(ColumnJobClass)!)
                .StoreDurably(GetBooleanFromDbValue(rs[ColumnIsDurable]));

            if (loadJobType)
            {
                jobBuilder.OfType(loadHelper.LoadType(rs.GetString(ColumnJobClass)!)!);
            }

            return jobBuilder.Build();
        }

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("No job for trigger '{TriggerKey}'", triggerKey);
        }

        return null;
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> SelectJobExecutionCount(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectJobExecutionCount));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "jobName", jobKey.Name);
        AddCommandParameter(cmd, "jobGroup", jobKey.Group);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Remove the transient data from and then create a serialized <see cref="MemoryStream" />
    /// version of a <see cref="JobDataMap" /> and returns the underlying bytes.
    /// </summary>
    /// <param name="data">The data.</param>
    /// <returns>the serialized data as byte array</returns>
    public virtual byte[]? SerializeJobData(JobDataMap data)
    {
        if (data.Count == 0)
        {
            return null;
        }

        bool skipStringPropertySerialization = data.ContainsKey(FileScanListenerName) || data.ContainsKey(DirectoryScanListenerName);
        if (CanUseProperties && !skipStringPropertySerialization)
        {
            return SerializeProperties(data);
        }

        try
        {
            return SerializeObject(data);
        }
        catch (SerializationException e)
        {
            ThrowHelper.ThrowSerializationException($"Unable to serialize JobDataMap for insertion into database because the value of property '{GetKeyOfNonSerializableValue(data)}' is not serializable: {e.Message}");
            return default;
        }
    }

    /// <summary>
    /// This method should be overridden by any delegate subclasses that need
    /// special handling for BLOBs for job details.
    /// </summary>
    /// <param name="rs">The result set, already queued to the correct row.</param>
    /// <param name="colIndex">The column index for the BLOB.</param>
    /// <returns>The deserialized Object from the ResultSet BLOB.</returns>
    protected virtual ValueTask<T?> GetJobDataFromBlob<T>(DbDataReader rs, int colIndex) where T : class
    {
        if (CanUseProperties)
        {
            if (!rs.IsDBNull(colIndex))
            {
                // should be NameValueCollection
                return GetObjectFromBlob<T>(rs, colIndex);
            }

            return new((T?) null);
        }

        return GetObjectFromBlob<T>(rs, colIndex);
    }

    /// <summary>
    /// Insert the job detail record.
    /// </summary>
    /// <returns>Number of rows inserted.</returns>
    public virtual async ValueTask<int> InsertJobDetail(
        ConnectionAndTransactionHolder conn,
        IJobDetail job,
        CancellationToken cancellationToken = default)
    {
        var jobData = SerializeJobData(job.JobDataMap);

        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlInsertJobDetail));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "jobName", job.Key.Name);
        AddCommandParameter(cmd, "jobGroup", job.Key.Group);
        AddCommandParameter(cmd, "jobDescription", job.Description);
        AddCommandParameter(cmd, "jobType", job.JobType.FullName);
        AddCommandParameter(cmd, "jobDurable", GetDbBooleanValue(job.Durable));
        AddCommandParameter(cmd, "jobVolatile", GetDbBooleanValue(job.ConcurrentExecutionDisallowed));
        AddCommandParameter(cmd, "jobStateful", GetDbBooleanValue(job.PersistJobDataAfterExecution));
        AddCommandParameter(cmd, "jobRequestsRecovery", GetDbBooleanValue(job.RequestsRecovery));
        AddCommandParameter(cmd, "jobDataMap", jobData, DbProvider.Metadata.DbBinaryType);

        var insertResult = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return insertResult;
    }
}