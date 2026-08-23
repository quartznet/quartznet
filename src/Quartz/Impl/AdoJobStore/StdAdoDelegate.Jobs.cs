using System.Data.Common;
using System.Runtime.Serialization;

using Microsoft.Extensions.Logging;

using Quartz.Extensibility;
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

        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlUpdateJobDetail));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
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
    public virtual async ValueTask<List<TriggerKey>> SelectTriggerKeysForJob(ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectTriggersForJob));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "jobName", jobKey.Name);
        AddCommandParameter(cmd, "jobGroup", jobKey.Group);
        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        List<TriggerKey> list = [];
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string trigName = rs.GetString(AdoConstants.ColumnTriggerName)!;
            string trigGroup = rs.GetString(AdoConstants.ColumnTriggerGroup)!;
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
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlDeleteJobDetail));
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Deleting job: {JobKey}", jobKey);
        }

        AddCommandParameter(cmd, "schedulerName", schedulerName);
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
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectJobExistence));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
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

        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlUpdateJobData));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "jobDataMap", jobData, DbProvider.Metadata.DbBinaryType);
        AddCommandParameter(cmd, "jobName", job.Key.Name);
        AddCommandParameter(cmd, "jobGroup", job.Key.Group);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<IJobDetail?> SelectJobDetail(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        ITypeLoader typeLoader,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectJobDetail));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "jobName", jobKey.Name);
        AddCommandParameter(cmd, "jobGroup", jobKey.Group);
        using var rs = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false);
        IJobDetail? job = null;

        if (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            job = await ReadJobDetail(rs, new JobDetailRowOrdinals(rs), typeLoader).ConfigureAwait(false);
        }

        return job;
    }

    /// <inheritdoc />
    public virtual async ValueTask<List<IJobDetail>> SelectJobDetails(
        ConnectionAndTransactionHolder conn,
        IReadOnlyCollection<JobKey> jobKeys,
        ITypeLoader typeLoader,
        CancellationToken cancellationToken = default)
    {
        List<IJobDetail> jobs = new(jobKeys.Count);
        if (jobKeys.Count == 0)
        {
            return jobs;
        }

        // A repeated key would come back as a repeated row, and the predicate is a disjunction that
        // cannot tell the difference, so fold duplicates away before building it.
        List<JobKey> keys = Deduplicate(jobKeys);

        for (int offset = 0; offset < keys.Count; offset += AdoUtil.MaxJobKeysPerPredicate)
        {
            int length = Math.Min(AdoUtil.MaxJobKeysPerPredicate, keys.Count - offset);

            using DbCommand cmd = PrepareJobKeySetCommand(conn, StdAdoConstants.SqlSelectJobDetailsByKeysPrefix, keys, offset, length);
            using DbDataReader rs = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false);
            JobDetailRowOrdinals? ordinals = null;
            while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                ordinals ??= new JobDetailRowOrdinals(rs);
                jobs.Add(await ReadJobDetail(rs, ordinals.Value, typeLoader).ConfigureAwait(false));
            }
        }

        SortByRequestedOrder(jobs, keys, static job => job.Key);
        return jobs;
    }

    /// <summary>
    /// Prepares a statement matching a chunk of job keys, by appending the parameterized key-set
    /// predicate to <paramref name="sqlPrefix" />.
    /// </summary>
    private DbCommand PrepareJobKeySetCommand(
        ConnectionAndTransactionHolder conn,
        string sqlPrefix,
        List<JobKey> keys,
        int offset,
        int length)
    {
        int paddedCount = AdoUtil.RoundUpJobKeyCount(length);
        DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(sqlPrefix + AdoUtil.BuildJobKeyPredicate(paddedCount)));
        AddCommandParameter(cmd, "schedulerName", schedulerName);

        for (int i = 0; i < paddedCount; i++)
        {
            // Pad up to the bucket size by repeating the chunk's last key. The predicate is a
            // disjunction, so a repeated term cannot change which rows match.
            JobKey key = keys[offset + Math.Min(i, length - 1)];
            AddCommandParameter(cmd, AdoUtil.JobKeyNameParameter(i), key.Name);
            AddCommandParameter(cmd, AdoUtil.JobKeyGroupParameter(i), key.Group);
        }

        return cmd;
    }

    /// <summary>
    /// Where each column of a job detail select sits, worked out once for a reader rather than once per
    /// column per row. Same reasoning as <see cref="TriggerRowOrdinals" />.
    /// </summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly struct JobDetailRowOrdinals
    {
        public JobDetailRowOrdinals(DbDataReader rs)
        {
            JobName = rs.GetOrdinal(AdoConstants.ColumnJobName);
            JobGroup = rs.GetOrdinal(AdoConstants.ColumnJobGroup);
            Description = rs.GetOrdinal(AdoConstants.ColumnDescription);
            JobClass = rs.GetOrdinal(AdoConstants.ColumnJobClass);
            IsDurable = rs.GetOrdinal(AdoConstants.ColumnIsDurable);
            RequestsRecovery = rs.GetOrdinal(AdoConstants.ColumnRequestsRecovery);
            IsNonConcurrent = rs.GetOrdinal(AdoConstants.ColumnIsNonConcurrent);
            IsUpdateData = rs.GetOrdinal(AdoConstants.ColumnIsUpdateData);
        }

        public int JobName { get; }
        public int JobGroup { get; }
        public int Description { get; }
        public int JobClass { get; }
        public int IsDurable { get; }
        public int RequestsRecovery { get; }
        public int IsNonConcurrent { get; }
        public int IsUpdateData { get; }
    }

    /// <summary>
    /// Reads the current row of a job detail select. Shared by the single-job and batch read paths so
    /// the two cannot drift apart.
    /// </summary>
    private async ValueTask<IJobDetail> ReadJobDetail(DbDataReader rs, JobDetailRowOrdinals ordinals, ITypeLoader typeLoader)
    {
        // Due to CommandBehavior.SequentialAccess, columns must be read in order. Asking for a column's
        // position does not read it, so the ordinals may be taken in any order beforehand.

        var jobBuilder = JobBuilder.Create()
            .WithIdentity(new JobKey(rs.GetString(ordinals.JobName), rs.GetString(ordinals.JobGroup)))
            .WithDescription(ReadNullableString(rs, ordinals.Description))
            .OfType(CreateJobType(rs.GetString(ordinals.JobClass), typeLoader))
            .StoreDurably(GetBooleanFromDbValue(rs.GetValue(ordinals.IsDurable)))
            .RequestRecovery(GetBooleanFromDbValue(rs.GetValue(ordinals.RequestsRecovery)));

        var map = await ReadMapFromReader(rs, 6).ConfigureAwait(false);

        if (map is not null)
        {
            jobBuilder.ReplaceJobData(new JobDataMap(map));
        }

        jobBuilder.DisallowConcurrentExecution(GetBooleanFromDbValue(rs.GetValue(ordinals.IsNonConcurrent)))
            .PersistJobDataAfterExecution(GetBooleanFromDbValue(rs.GetValue(ordinals.IsUpdateData)));

        return jobBuilder.Build();
    }

    /// <summary>
    /// Builds the job type for a stored <c>JOB_CLASS_NAME</c>, resolved through the scheduler's
    /// <see cref="ITypeLoader" /> rather than by <see cref="Type.GetType(string)" /> alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The stored name is whatever wrote the row, so a table carried over from 2.x or 3.x names types
    /// the way those versions spelled them. The helper is the only thing that knows the namespace,
    /// type and assembly renames, so resolving without it leaves such a job loading and listing
    /// perfectly well - <see cref="JobType" /> resolves lazily - and then failing at its first fire,
    /// with nothing logged to say the name had merely moved.
    /// </para>
    /// <para>
    /// Only the resolution is delegated, not the name: <see cref="JobType.FullName" /> keeps reporting
    /// the stored spelling, so reading a job never rewrites the column behind the user's back.
    /// </para>
    /// </remarks>
    private static JobType CreateJobType(string jobClassName, ITypeLoader typeLoader)
    {
        return new JobType(jobClassName, name =>
        {
            Type? resolved;
            try
            {
                resolved = typeLoader.LoadType(name);
            }
            catch (TypeLoadException)
            {
                // A helper that cannot resolve the name is required to throw, and a fault-tolerant one
                // returns null instead. Either way the name means nothing to it, and falling back leaves
                // an unresolvable job type failing exactly the way it did before the helper was consulted.
                resolved = null;
            }

            return resolved ?? Type.GetType(name);
        });
    }

    /// <inheritdoc />
    public virtual async ValueTask<IJobDetail?> SelectJobForTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        ITypeLoader typeLoader,
        bool loadJobType,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectJobForTrigger));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);
        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string jobClassName = rs.GetString(AdoConstants.ColumnJobClass)!;

            var jobBuilder = JobBuilder.Create()
                .WithIdentity(new JobKey(rs.GetString(AdoConstants.ColumnJobName)!, rs.GetString(AdoConstants.ColumnJobGroup)!))
                .RequestRecovery(GetBooleanFromDbValue(rs[AdoConstants.ColumnRequestsRecovery]))
                .OfType(CreateJobType(jobClassName, typeLoader))
                .StoreDurably(GetBooleanFromDbValue(rs[AdoConstants.ColumnIsDurable]));

            if (loadJobType)
            {
                jobBuilder.OfType(typeLoader.LoadType(jobClassName)!);
            }

            return jobBuilder.Build();
        }

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("No job for trigger '{TriggerKey}'", triggerKey);
        }

        return null;
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
            Throw.SerializationException($"Unable to serialize JobDataMap for insertion into database because the value of property '{GetKeyOfNonSerializableValue(data)}' is not serializable: {e.Message}");
            return default;
        }
    }

    /// <summary>
    /// This method should be overridden by any delegate subclasses that need
    /// special handling for BLOBs for job details.
    /// </summary>
    /// <param name="rs">The result set, already queued to the correct row.</param>
    /// <param name="colIndex">The column index for the BLOB.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The deserialized Object from the ResultSet BLOB.</returns>
    protected virtual ValueTask<T?> GetJobDataFromBlob<T>(
        DbDataReader rs,
        int colIndex,
        CancellationToken cancellationToken = default) where T : class
    {
        if (CanUseProperties)
        {
            if (!rs.IsDBNull(colIndex))
            {
                // should be NameValueCollection
                return GetObjectFromBlob<T>(rs, colIndex, cancellationToken);
            }

            return new((T?) null);
        }

        return GetObjectFromBlob<T>(rs, colIndex, cancellationToken);
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

        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlInsertJobDetail));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
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

    /// <inheritdoc />
    public virtual async ValueTask<int> InsertPausedJobGroup(
        ConnectionAndTransactionHolder conn,
        string groupName,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlInsertPausedJobGroup));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "jobGroup", groupName);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> DeletePausedJobGroup(
        ConnectionAndTransactionHolder conn,
        GroupMatcher<JobKey> matcher,
        CancellationToken cancellationToken = default)
    {
        (string sql, string parameter) = MatchGroup(matcher, StdAdoConstants.SqlDeletePausedJobGroupEquals, StdAdoConstants.SqlDeletePausedJobGroupLike);

        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(sql));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "jobGroup", parameter);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<bool> IsJobGroupPaused(
        ConnectionAndTransactionHolder conn,
        string groupName,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectPausedJobGroup));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "jobGroup", groupName);

        return await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }
}