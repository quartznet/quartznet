using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Logging;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Impl.AdoJobStore
{
    public partial class StdAdoDelegate
    {
        protected virtual string GetStorableJobTypeName(Type jobType)
        {
            if (jobType.AssemblyQualifiedName == null)
            {
                throw new ArgumentException("Cannot determine job type name when type's AssemblyQualifiedName is null");
            }

            return jobType.AssemblyQualifiedNameWithoutVersion();
        }

        /// <inheritdoc />
        public virtual async Task<int> UpdateJobDetail(
            ConnectionAndTransactionHolder conn,
            IJobDetail job,
            CancellationToken cancellationToken = default)
        {
            var jobData = SerializeJobData(job.JobDataMap);

            using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateJobDetail));
            AddCommandParameter(cmd, "schedulerName", schedName);
            AddCommandParameter(cmd, "jobDescription", job.Description);
            AddCommandParameter(cmd, "jobType", GetStorableJobTypeName(job.JobType));
            AddCommandParameter(cmd, "jobDurable", GetDbBooleanValue(job.Durable));
            AddCommandParameter(cmd, "jobVolatile", GetDbBooleanValue(job.ConcurrentExecutionDisallowed));
            AddCommandParameter(cmd, "jobStateful", GetDbBooleanValue(job.PersistJobDataAfterExecution));
            AddCommandParameter(cmd, "jobRequestsRecovery", GetDbBooleanValue(job.RequestsRecovery));
            AddCommandParameter(cmd, "jobDataMap", jobData, DbProvider.Metadata.DbBinaryType);
            AddCommandParameter(cmd, "jobName", job.Key.Name);
            AddCommandParameter(cmd, "jobGroup", job.Key.Group);

            int insertResult = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            return insertResult;
        }

        /// <inheritdoc />
        public virtual async Task<IReadOnlyCollection<TriggerKey>> SelectTriggerNamesForJob(
            ConnectionAndTransactionHolder conn,
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggersForJob));
            AddCommandParameter(cmd, "schedulerName", schedName);
            AddCommandParameter(cmd, "jobName", jobKey.Name);
            AddCommandParameter(cmd, "jobGroup", jobKey.Group);
            using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            List<TriggerKey> list = new List<TriggerKey>(10);
            while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                string trigName = rs.GetString(ColumnTriggerName)!;
                string trigGroup = rs.GetString(ColumnTriggerGroup)!;
                list.Add(new TriggerKey(trigName, trigGroup));
            }

            return list;
        }

        /// <inheritdoc />
        public virtual async Task<int> DeleteJobDetail(
            ConnectionAndTransactionHolder conn,
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteJobDetail));
            if (logger.IsDebugEnabled())
            {
                logger.Debug("Deleting job: " + jobKey);
            }

            AddCommandParameter(cmd, "schedulerName", schedName);
            AddCommandParameter(cmd, "jobName", jobKey.Name);
            AddCommandParameter(cmd, "jobGroup", jobKey.Group);
            return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public virtual async Task<bool> JobExists(
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
        public virtual async Task<int> UpdateJobData(
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
        public virtual async Task<IJobDetail?> SelectJobDetail(
            ConnectionAndTransactionHolder conn,
            JobKey jobKey,
            ITypeLoadHelper loadHelper,
            CancellationToken cancellationToken = default)
        {
            using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectJobDetail));
            AddCommandParameter(cmd, "schedulerName", schedName);
            AddCommandParameter(cmd, "jobName", jobKey.Name);
            AddCommandParameter(cmd, "jobGroup", jobKey.Group);
            using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            JobDetailImpl? job = null;

            if (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                job = new JobDetailImpl();

                job.Name = rs.GetString(ColumnJobName)!;
                job.Group = rs.GetString(ColumnJobGroup)!;
                job.Description = rs.GetString(ColumnDescription);
                job.JobType = loadHelper.LoadType(rs.GetString(ColumnJobClass)!)!;
                job.Durable = GetBooleanFromDbValue(rs[ColumnIsDurable]);
                job.RequestsRecovery = GetBooleanFromDbValue(rs[ColumnRequestsRecovery]);

                var map = await ReadMapFromReader(rs, 6).ConfigureAwait(false);

                if (map != null)
                {
                    job.JobDataMap = new JobDataMap(map);
                }
            }

            return job;
        }

        /// <inheritdoc />
        public virtual async Task<int> SelectNumJobs(
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
        public virtual async Task<IReadOnlyCollection<string>> SelectJobGroups(
            ConnectionAndTransactionHolder conn,
            CancellationToken cancellationToken = default)
        {
            using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectJobGroups));
            AddCommandParameter(cmd, "schedulerName", schedName);
            using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            List<string> list = new List<string>();
            while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                list.Add(rs.GetString(0));
            }

            return list;
        }

        /// <inheritdoc />
        public virtual Task<IJobDetail?> SelectJobForTrigger(
            ConnectionAndTransactionHolder conn,
            TriggerKey triggerKey,
            ITypeLoadHelper loadHelper,
            CancellationToken cancellationToken = default)
        {
            return SelectJobForTrigger(conn, triggerKey, loadHelper, true, cancellationToken);
        }

        /// <inheritdoc />
        public virtual async Task<IJobDetail?> SelectJobForTrigger(
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
                JobDetailImpl job = new JobDetailImpl();
                job.Name = rs.GetString(ColumnJobName)!;
                job.Group = rs.GetString(ColumnJobGroup)!;
                job.Durable = GetBooleanFromDbValue(rs[ColumnIsDurable]);
                if (loadJobType)
                {
                    job.JobType = loadHelper.LoadType(rs.GetString(ColumnJobClass)!)!;
                }

                job.RequestsRecovery = GetBooleanFromDbValue(rs[ColumnRequestsRecovery]);

                return job;
            }

            if (logger.IsDebugEnabled())
            {
                logger.Debug("No job for trigger '" + triggerKey + "'.");
            }

            return null;
        }

        /// <inheritdoc />
        public virtual async Task<int> SelectJobExecutionCount(
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
                throw new SerializationException(
                    "Unable to serialize JobDataMap for insertion into " +
                    "database because the value of property '" +
                    GetKeyOfNonSerializableValue(data) +
                    "' is not serializable: " + e.Message);
            }
        }

        /// <summary>
        /// This method should be overridden by any delegate subclasses that need
        /// special handling for BLOBs for job details.
        /// </summary>
        /// <param name="rs">The result set, already queued to the correct row.</param>
        /// <param name="colIndex">The column index for the BLOB.</param>
        /// <returns>The deserialized Object from the ResultSet BLOB.</returns>
        protected virtual Task<T?> GetJobDataFromBlob<T>(DbDataReader rs, int colIndex) where T : class
        {
            if (CanUseProperties)
            {
                if (!rs.IsDBNull(colIndex))
                {
                    // should be NameValueCollection
                    return GetObjectFromBlob<T>(rs, colIndex);
                }

                return Task.FromResult<T?>(null);
            }

            return GetObjectFromBlob<T>(rs, colIndex);
        }

        /// <summary>
        /// Insert the job detail record.
        /// </summary>
        /// <returns>Number of rows inserted.</returns>
        public virtual async Task<int> InsertJobDetail(
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
            AddCommandParameter(cmd, "jobType", GetStorableJobTypeName(job.JobType));
            AddCommandParameter(cmd, "jobDurable", GetDbBooleanValue(job.Durable));
            AddCommandParameter(cmd, "jobVolatile", GetDbBooleanValue(job.ConcurrentExecutionDisallowed));
            AddCommandParameter(cmd, "jobStateful", GetDbBooleanValue(job.PersistJobDataAfterExecution));
            AddCommandParameter(cmd, "jobRequestsRecovery", GetDbBooleanValue(job.RequestsRecovery));
            AddCommandParameter(cmd, "jobDataMap", jobData, DbProvider.Metadata.DbBinaryType);

            var insertResult = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            return insertResult;
        }
    }
}