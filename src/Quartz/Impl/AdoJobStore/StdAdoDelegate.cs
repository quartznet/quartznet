#region License
/* 
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved. 
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

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

using Common.Logging;

using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// This is meant to be an abstract base class for most, if not all, <see cref="IDriverDelegate" />
    /// implementations. Subclasses should override only those methods that need
    /// special handling for the DBMS driver in question.
    /// </summary>
    /// <author><a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a></author>
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    public class StdAdoDelegate : StdAdoConstants, IDriverDelegate
    {
        protected const int DefaultTriggersToAcquireLimit = 5;
        protected ILog logger = null;
        protected string tablePrefix = DefaultTablePrefix;
        protected string instanceId;
        protected bool useProperties;
        protected IDbProvider dbProvider;
        protected AdoUtil adoUtil;

        /// <summary>
        /// Create new StdAdoDelegate instance.
        /// </summary>
        /// <param name="logger">the logger to use during execution</param>
        /// <param name="tablePrefix">the prefix of all table names</param>
        /// <param name="instanceId">The instance id.</param>
        /// <param name="dbProvider">The db provider.</param>
        public StdAdoDelegate(ILog logger, string tablePrefix, string instanceId, IDbProvider dbProvider)
        {
            this.logger = logger;
            this.tablePrefix = tablePrefix;
            this.instanceId = instanceId;
            this.dbProvider = dbProvider;
            adoUtil = new AdoUtil(dbProvider);
        }

        /// <summary>
        /// Create new StdAdoDelegate instance.
        /// </summary>
        /// <param name="logger">the logger to use during execution</param>
        /// <param name="tablePrefix">the prefix of all table names</param>
        /// <param name="instanceId">The instance id.</param>
        /// <param name="dbProvider">The db provider.</param>
        /// <param name="useProperties">if set to <c>true</c> [use properties].</param>
        public StdAdoDelegate(ILog logger, string tablePrefix, string instanceId, IDbProvider dbProvider,
                              bool useProperties)
        {
            this.logger = logger;
            this.tablePrefix = tablePrefix;
            this.instanceId = instanceId;
            this.dbProvider = dbProvider;
            adoUtil = new AdoUtil(dbProvider);
            this.useProperties = useProperties;
        }

        protected virtual bool CanUseProperties
        {
            get { return useProperties; }
        }

        //---------------------------------------------------------------------------
        // startup / recovery
        //---------------------------------------------------------------------------

        /// <summary>
        /// Insert the job detail record.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="newState">the new state for the triggers</param>
        /// <param name="oldState1">the first old state to update</param>
        /// <param name="oldState2">the second old state to update</param>
        /// <returns>number of rows updated</returns>
        public virtual int UpdateTriggerStatesFromOtherStates(ConnectionAndTransactionHolder conn, string newState,
                                                              string oldState1,
                                                              string oldState2)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateTriggerStatesFromOtherStates)))
            {
                AddCommandParameter(cmd, "newState", newState);
                AddCommandParameter(cmd, "oldState1", oldState1);
                AddCommandParameter(cmd, "oldState2", oldState2);
                return cmd.ExecuteNonQuery();
            }
        }


        /// <summary>
        /// Get the names of all of the triggers that have misfired.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="ts">The ts.</param>
        /// <returns>an array of <see cref="Key" /> objects</returns>
        public virtual IList<Key> SelectMisfiredTriggers(ConnectionAndTransactionHolder conn, long ts)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectMisfiredTriggers)))
            {
                AddCommandParameter(cmd, "timestamp", ts);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    List<Key> list = new List<Key>();
                    while (rs.Read())
                    {
                        string triggerName = GetString(rs[ColumnTriggerName]);
                        string groupName = GetString(rs[ColumnTriggerGroup]);
                        list.Add(new Key(triggerName, groupName));
                    }
                    return list.ToArray();
                }
            }
        }

        /// <summary> 
        /// Select all of the triggers in a given state.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="state">The state the triggers must be in</param>
        /// <returns> an array of trigger <see cref="Key" />s </returns>
        public virtual IList<Key> SelectTriggersInState(ConnectionAndTransactionHolder conn, string state)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggersInState)))
            {
                AddCommandParameter(cmd, "state", state);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    List<Key> list = new List<Key>();
                    while (rs.Read())
                    {
                        list.Add(new Key(GetString(rs[0]), GetString(rs[1])));
                    }

                    return list;
                }
            }
        }


        /// <summary>
        /// Get the names of all of the triggers in the given state that have
        /// misfired - according to the given timestamp.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="state">The state.</param>
        /// <param name="ts">The time stamp.</param>
        /// <returns>An array of <see cref="Key" /> objects</returns>
        public virtual IList<Key> HasMisfiredTriggersInState(ConnectionAndTransactionHolder conn, string state, long ts)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectMisfiredTriggersInState)))
            {
                AddCommandParameter(cmd, "timestamp", ts);
                AddCommandParameter(cmd, "state", state);

                using (IDataReader rs = cmd.ExecuteReader())
                {
                    List<Key> list = new List<Key>();
                    while (rs.Read())
                    {
                        string triggerName = GetString(rs[ColumnTriggerName]);
                        string groupName = GetString(rs[ColumnTriggerGroup]);
                        list.Add(new Key(triggerName, groupName));
                    }
                    return list.ToArray();
                }
            }
        }


        /// <summary>
        /// Get the names of all of the triggers in the given state that have
        /// misfired - according to the given timestamp.  No more than count will
        /// be returned.
        /// </summary>
        /// <param name="conn">The conn.</param>
        /// <param name="state1">The state1.</param>
        /// <param name="ts">The ts.</param>
        /// <param name="count">The most misfired triggers to return, negative for all</param>
        /// <param name="resultList">
        ///   Output parameter.  A List of <see cref="Key" /> objects.  Must not be null
        /// </param>
        /// <returns>Whether there are more misfired triggers left to find beyond the given count.</returns>
        public virtual bool HasMisfiredTriggersInState(ConnectionAndTransactionHolder conn, string state1, DateTimeOffset ts, int count, IList<Key> resultList)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectHasMisfiredTriggersInState)))
            {
                AddCommandParameter(cmd, "nextFireTime", Convert.ToDecimal(ts.Ticks));
                AddCommandParameter(cmd, "state1", state1);

                using (IDataReader rs = cmd.ExecuteReader())
                {
                    bool hasReachedLimit = false;
                    while (rs.Read() && (hasReachedLimit == false))
                    {
                        if (resultList.Count == count)
                        {
                            hasReachedLimit = true;
                        }
                        else
                        {
                            string triggerName = GetString(rs[ColumnTriggerName]);
                            string groupName = GetString(rs[ColumnTriggerGroup]);
                            resultList.Add(new Key(triggerName, groupName));
                        }
                    }
                    return hasReachedLimit;
                }
            }
        }

        /// <summary>
        /// Get the number of triggers in the given state that have
        /// misfired - according to the given timestamp.
        /// </summary>
        /// <param name="conn"></param>
        /// <param name="state1"></param>
        /// <param name="ts"></param>
        /// <returns></returns>
        public int CountMisfiredTriggersInState(ConnectionAndTransactionHolder conn, string state1, DateTimeOffset ts)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlCountMisfiredTriggersInStates)))
            {
                AddCommandParameter(cmd, "nextFireTime", Convert.ToDecimal(ts.Ticks));
                AddCommandParameter(cmd, "state1", state1);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        return Convert.ToInt32(rs.GetValue(0), CultureInfo.InvariantCulture);
                    }
                }

                throw new Exception("No misfired trigger count returned.");
            }
        }

        /// <summary>
        /// Get the names of all of the triggers in the given group and state that
        /// have misfired.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="groupName">Name of the group.</param>
        /// <param name="state">The state.</param>
        /// <param name="ts">The timestamp.</param>
        /// <returns>an array of <see cref="Key" /> objects</returns>
        public virtual IList<Key> SelectMisfiredTriggersInGroupInState(ConnectionAndTransactionHolder conn, string groupName, string state, long ts)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectMisfiredTriggersInGroupInState))
                )
            {
                AddCommandParameter(cmd, "timestamp", Convert.ToDecimal(ts));
                AddCommandParameter(cmd, "triggerGroup", groupName);
                AddCommandParameter(cmd, "state", state);

                using (IDataReader rs = cmd.ExecuteReader())
                {
                    List<Key> list = new List<Key>();
                    while (rs.Read())
                    {
                        string triggerName = GetString(rs[ColumnTriggerName]);
                        list.Add(new Key(triggerName, groupName));
                    }
                    return list.ToArray();
                }
            }
        }

        /// <summary>
        /// Select all of the triggers for jobs that are requesting recovery. The
        /// returned trigger objects will have unique "recoverXXX" trigger names and
        /// will be in the <see cref="SchedulerConstants.DefaultRecoveryGroup" />
        /// trigger group.
        /// </summary>
        /// <remarks>
        /// In order to preserve the ordering of the triggers, the fire time will be
        /// set from the <i>ColumnFiredTime</i> column in the <i>TableFiredTriggers</i>
        /// table. The caller is responsible for calling <see cref="Trigger.ComputeFirstFireTimeUtc" />
        /// on each returned trigger. It is also up to the caller to insert the
        /// returned triggers to ensure that they are fired.
        /// </remarks>
        /// <param name="conn">The DB Connection</param>
        /// <returns> an array of <see cref="Trigger" /> objects</returns>
        public virtual IList<Trigger> SelectTriggersForRecoveringJobs(ConnectionAndTransactionHolder conn)
        {
            List<Trigger> list = new List<Trigger>();

            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectInstancesRecoverableFiredTriggers)))
            {
                AddCommandParameter(cmd, "instanceName", instanceId);
                AddCommandParameter(cmd, "requestsRecovery", GetDbBooleanValue(true));

                using (IDataReader rs = cmd.ExecuteReader())
                {
                    long dumId = SystemTime.UtcNow().Ticks;

                    while (rs.Read())
                    {
                        string jobName = GetString(rs[ColumnJobName]);
                        string jobGroup = GetString(rs[ColumnJobGroup]);
                        // string trigName = GetString(rs[ColumnTriggerName]);
                        // string trigGroup = GetString(rs[ColumnTriggerGroup]);
                        long firedTimeInTicks = Convert.ToInt64(rs[ColumnFiredTime], CultureInfo.InvariantCulture);
                        int priority = Convert.ToInt32(rs[ColumnPriority], CultureInfo.InvariantCulture);
                        DateTimeOffset firedTime = new DateTimeOffset(firedTimeInTicks, TimeSpan.Zero);
                        SimpleTrigger rcvryTrig =
                            new SimpleTrigger("recover_" + instanceId + "_" + Convert.ToString(dumId++, CultureInfo.InvariantCulture),
                                              SchedulerConstants.DefaultRecoveryGroup, firedTime);
                        rcvryTrig.JobName = jobName;
                        rcvryTrig.JobGroup = jobGroup;
                        rcvryTrig.Priority = priority;
                        rcvryTrig.MisfireInstruction = MisfireInstruction.SimpleTrigger.FireNow;

                        list.Add(rcvryTrig);
                    }
                }
            }

            // read JobDataMaps with different reader..
            foreach (SimpleTrigger trigger in list)
            {
                JobDataMap jd = SelectTriggerJobDataMap(conn, trigger.Name, trigger.Group);
                jd.Put(SchedulerConstants.FailedJobOriginalTriggerName, trigger.Name);
                jd.Put(SchedulerConstants.FailedJobOriginalTriggerGroup, trigger.Group);
                jd.Put(SchedulerConstants.FailedJobOriginalTriggerFiretimeInMillisecoonds, Convert.ToString(trigger.StartTimeUtc, CultureInfo.InvariantCulture));
                trigger.JobDataMap = jd;
            }

            return list.ToArray();
        }

        /// <summary>
        /// Delete all fired triggers.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <returns>The number of rows deleted.</returns>
        public virtual int DeleteFiredTriggers(ConnectionAndTransactionHolder conn)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteFiredTriggers)))
            {
                return cmd.ExecuteNonQuery();
            }
        }


        /// <summary>
        /// Delete all fired triggers of the given instance.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="instanceName">The instance id.</param>
        /// <returns>The number of rows deleted</returns>
        public virtual int DeleteFiredTriggers(ConnectionAndTransactionHolder conn, string instanceName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteInstancesFiredTriggers)))
            {
                AddCommandParameter(cmd, "instanceName", instanceName);
                return cmd.ExecuteNonQuery();
            }
        }

        //---------------------------------------------------------------------------
        // jobs
        //---------------------------------------------------------------------------

        /// <summary>
        /// Insert the job detail record.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <param name="job">The job to insert.</param>
        /// <returns>Number of rows inserted.</returns>
        public virtual int InsertJobDetail(ConnectionAndTransactionHolder conn, JobDetail job)
        {
            byte[] baos = SerializeJobData(job.JobDataMap);

            int insertResult;

            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlInsertJobDetail)))
            {
                AddCommandParameter(cmd, "jobName", job.Name);
                AddCommandParameter(cmd, "jobGroup", job.Group);
                AddCommandParameter(cmd, "jobDescription", job.Description);
                AddCommandParameter(cmd, "jobType", GetStorableJobTypeName(job.JobType));
                AddCommandParameter(cmd, "jobDurable", GetDbBooleanValue(job.Durable));
                AddCommandParameter(cmd, "jobVolatile", GetDbBooleanValue(job.Volatile));
                AddCommandParameter(cmd, "jobStateful", GetDbBooleanValue(job.Stateful));
                AddCommandParameter(cmd, "jobRequestsRecovery", GetDbBooleanValue(job.RequestsRecovery));
                AddCommandParameter(cmd, "jobDataMap", baos, dbProvider.Metadata.DbBinaryType);

                insertResult = cmd.ExecuteNonQuery();
            }


            return insertResult;
        }

        /// <summary>
        /// Gets the db presentation for boolean value. Subclasses can overwrite this behaviour.
        /// </summary>
        /// <param name="booleanValue">Value to map to database.</param>
        /// <returns></returns>
        protected virtual object GetDbBooleanValue(bool booleanValue)
        {
            // works nicely for databases we have currently supported
            return booleanValue ? 1 : 0;
        }

        protected virtual string GetStorableJobTypeName(Type jobType)
        {
            int idx = jobType.AssemblyQualifiedName.IndexOf(',');
            // find next
            idx = jobType.AssemblyQualifiedName.IndexOf(',', idx + 1);

            string retValue = jobType.AssemblyQualifiedName.Substring(0, idx);

            return retValue;
        }

        /// <summary>
        /// Update the job detail record.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <param name="job">The job to update.</param>
        /// <returns>Number of rows updated.</returns>
        public virtual int UpdateJobDetail(ConnectionAndTransactionHolder conn, JobDetail job)
        {
            byte[] baos = SerializeJobData(job.JobDataMap);

            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateJobDetail)))
            {
                AddCommandParameter(cmd, "jobDescription", job.Description);
                AddCommandParameter(cmd, "jobType", GetStorableJobTypeName(job.JobType));
                AddCommandParameter(cmd, "jobDurable", GetDbBooleanValue(job.Durable));
                AddCommandParameter(cmd, "jobVolatile", GetDbBooleanValue(job.Volatile));
                AddCommandParameter(cmd, "jobStateful", GetDbBooleanValue(job.Stateful));
                AddCommandParameter(cmd, "jobRequestsRecovery", GetDbBooleanValue(job.RequestsRecovery));
                AddCommandParameter(cmd, "jobDataMap", baos, dbProvider.Metadata.DbBinaryType);
                AddCommandParameter(cmd, "jobName", job.Name);
                AddCommandParameter(cmd, "jobGroup", job.Group);

                int insertResult = cmd.ExecuteNonQuery();

                return insertResult;
            }
        }

        /// <summary>
        /// Get the names of all of the triggers associated with the given job.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <param name="jobName">The name of the job.</param>
        /// <param name="groupName">The group containing the job.</param>
        /// <returns>An array of <see cref="Key" /> objects</returns>
        public virtual IList<Key> SelectTriggerNamesForJob(ConnectionAndTransactionHolder conn, string jobName, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggersForJob)))
            {
                AddCommandParameter(cmd, "jobName", jobName);
                AddCommandParameter(cmd, "jobGroup", groupName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    List<Key> list = new List<Key>(10);
                    while (rs.Read())
                    {
                        string trigName = GetString(rs[ColumnTriggerName]);
                        string trigGroup = GetString(rs[ColumnTriggerGroup]);
                        list.Add(new Key(trigName, trigGroup));
                    }
                    return list;
                }
            }
        }

        /// <summary>
        /// Delete the job detail record for the given job.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="jobName">the name of the job</param>
        /// <param name="groupName">the group containing the job</param>
        /// <returns>the number of rows deleted</returns>
        public virtual int DeleteJobDetail(ConnectionAndTransactionHolder conn, string jobName, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteJobDetail)))
            {
                if (logger.IsDebugEnabled)
                {
                    logger.Debug("Deleting job: " + groupName + "." + jobName);
                }
                AddCommandParameter(cmd, "jobName", jobName);
                AddCommandParameter(cmd, "jobGroup", groupName);
                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Check whether or not the given job is stateful.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="jobName">the name of the job</param>
        /// <param name="groupName">the group containing the job</param>
        /// <returns>
        /// true if the job exists and is stateful, false otherwise
        /// </returns>
        public virtual bool IsJobStateful(ConnectionAndTransactionHolder conn, string jobName, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectJobStateful)))
            {
                AddCommandParameter(cmd, "jobName", jobName);
                AddCommandParameter(cmd, "jobGroup", groupName);

                object o = cmd.ExecuteScalar();
                if (o != null)
                {
                    return (bool) o;
                }
                
                return false;
            }
        }

        /// <summary>
        /// Check whether or not the given job exists.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="jobName">the name of the job</param>
        /// <param name="groupName">the group containing the job</param>
        /// <returns>true if the job exists, false otherwise</returns>
        public virtual bool JobExists(ConnectionAndTransactionHolder conn, string jobName, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectJobExistence)))
            {
                AddCommandParameter(cmd, "jobName", jobName);
                AddCommandParameter(cmd, "jobGroup", groupName);
                using (IDataReader dr = cmd.ExecuteReader())
                {
                    if (dr.Read())
                    {
                        return true;
                    }
                    
                    return false;
                }
            }
        }

        /// <summary>
        /// Update the job data map for the given job.
        /// </summary>
        /// <param name="conn">The conn.</param>
        /// <param name="job">the job to update</param>
        /// <returns>the number of rows updated</returns>
        public virtual int UpdateJobData(ConnectionAndTransactionHolder conn, JobDetail job)
        {
            byte[] baos = SerializeJobData(job.JobDataMap);

            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateJobData)))
            {
                AddCommandParameter(cmd, "jobDataMap", baos, dbProvider.Metadata.DbBinaryType);
                AddCommandParameter(cmd, "jobName", job.Name);
                AddCommandParameter(cmd, "jobGroup", job.Group);

                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Select the JobDetail object for a given job name / group name.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <param name="jobName">The job name whose listeners are wanted.</param>
        /// <param name="groupName">The group containing the job.</param>
        /// <param name="loadHelper">The load helper.</param>
        /// <returns>The populated JobDetail object.</returns>
        public virtual JobDetail SelectJobDetail(ConnectionAndTransactionHolder conn, string jobName, string groupName,
                                                 ITypeLoadHelper loadHelper)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectJobDetail)))
            {
                AddCommandParameter(cmd, "jobName", jobName);
                AddCommandParameter(cmd, "jobGroup", groupName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    JobDetail job = null;

                    if (rs.Read())
                    {
                        job = new JobDetail();

                        job.Name = GetString(rs[ColumnJobName]);
                        job.Group = GetString(rs[ColumnJobGroup]);
                        job.Description = GetString(rs[ColumnDescription]);
                        job.JobType = loadHelper.LoadType(GetString(rs[ColumnJobClass]));
                        job.Durable = GetBoolean(rs[ColumnIsDurable]);
                        job.Volatile = GetBoolean(rs[ColumnIsVolatile]);
                        job.RequestsRecovery = GetBoolean(rs[ColumnRequestsRecovery]);

                        IDictionary map;
                        if (CanUseProperties)
                        {
                            map = GetMapFromProperties(rs, 8);
                        }
                        else
                        {
                            map = (IDictionary) GetObjectFromBlob(rs, 8);
                        }

                        if (null != map)
                        {
                            job.JobDataMap = new JobDataMap(map);
                        }
                    }

                    return job;
                }
            }
        }

        /// <summary> build Map from java.util.Properties encoding.</summary>
        private IDictionary GetMapFromProperties(IDataReader rs, int idx)
        {
            IDictionary map;
            NameValueCollection properties = (NameValueCollection)GetJobDetailFromBlob(rs, idx);
            if (properties == null)
            {
                return null;
            }
            map = ConvertFromProperty(properties);
            return map;
        }

        /// <summary>
        /// Select the total number of jobs stored.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <returns>The total number of jobs stored.</returns>
        public virtual int SelectNumJobs(ConnectionAndTransactionHolder conn)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectNumJobs)))
            {
                return (int)cmd.ExecuteScalar();
            }
        }

        /// <summary>
        /// Select all of the job group names that are stored.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <returns>An array of <see cref="String" /> group names.</returns>
        public virtual IList<string> SelectJobGroups(ConnectionAndTransactionHolder conn)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectJobGroups)))
            {
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    List<string> list = new List<string>();
                    while (rs.Read())
                    {
                        list.Add((string) rs[0]);
                    }

                    return list.ToArray();
                }
            }
        }

        /// <summary>
        /// Select all of the jobs contained in a given group.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <param name="groupName">The group containing the jobs.</param>
        /// <returns>An array of <see cref="String" /> job names.</returns>
        public virtual IList<string> SelectJobsInGroup(ConnectionAndTransactionHolder conn, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectJobsInGroup)))
            {
                AddCommandParameter(cmd, "jobGroup", groupName);

                using (IDataReader rs = cmd.ExecuteReader())
                {
                    List<string> list = new List<string>();
                    while (rs.Read())
                    {
                        list.Add(GetString(rs[0]));
                    }

                    return list.ToArray();
                }
            }
        }

        //---------------------------------------------------------------------------
        // triggers
        //---------------------------------------------------------------------------

        /// <summary>
        /// Insert the base trigger data.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="trigger">the trigger to insert</param>
        /// <param name="state">the state that the trigger should be stored in</param>
        /// <param name="jobDetail">The job detail.</param>
        /// <returns>the number of rows inserted</returns>
        public virtual int InsertTrigger(ConnectionAndTransactionHolder conn, Trigger trigger, string state,
                                         JobDetail jobDetail)
        {
            byte[] baos = null;
            if (trigger.JobDataMap.Count > 0)
            {
                baos = SerializeJobData(trigger.JobDataMap);
            }

            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlInsertTrigger)))
            {
                AddCommandParameter(cmd, "triggerName", trigger.Name);
                AddCommandParameter(cmd, "triggerGroup", trigger.Group);
                AddCommandParameter(cmd, "triggerJobName", trigger.JobName);
                AddCommandParameter(cmd, "triggerJobGroup", trigger.JobGroup);
                AddCommandParameter(cmd, "triggerVolatile", GetDbBooleanValue(trigger.Volatile));
                AddCommandParameter(cmd, "triggerDescription", trigger.Description);
                
                if (trigger.GetNextFireTimeUtc().HasValue)
                {
                    AddCommandParameter(cmd, "triggerNextFireTime",
                                    Convert.ToDecimal(trigger.GetNextFireTimeUtc().Value.Ticks));
                }
                else
                {
                    AddCommandParameter(cmd, "triggerNextFireTime", null);
                }
                long prevFireTime = -1;
                if (trigger.GetPreviousFireTimeUtc().HasValue)
                {
                    prevFireTime = trigger.GetPreviousFireTimeUtc().Value.Ticks;
                }
                AddCommandParameter(cmd, "triggerPreviousFireTime", Convert.ToDecimal(prevFireTime));
                AddCommandParameter(cmd, "triggerState", state);
                string paramName = "triggerType";
                if (trigger is SimpleTrigger && !trigger.HasAdditionalProperties)
                {
                    AddCommandParameter(cmd, paramName, TriggerTypeSimple);
                }
                else if (trigger is CronTrigger && !trigger.HasAdditionalProperties)
                {
                    AddCommandParameter(cmd, paramName, TriggerTypeCron);
                }
                else
                {
                    // (trigger instanceof BlobTrigger or additional properties in sub-class
                    AddCommandParameter(cmd, paramName, TriggerTypeBlob);
                }
                AddCommandParameter(cmd, "triggerStartTime", Convert.ToDecimal(trigger.StartTimeUtc.Ticks));
                long endTime = 0;
                if (trigger.EndTimeUtc.HasValue)
                {
                    endTime = trigger.EndTimeUtc.Value.Ticks;
                }
                AddCommandParameter(cmd, "triggerEndTime", Convert.ToDecimal(endTime));
                AddCommandParameter(cmd, "triggerCalendarName", trigger.CalendarName);
                AddCommandParameter(cmd, "triggerMisfireInstruction", trigger.MisfireInstruction);

                paramName = "triggerJobJobDataMap";
                if (baos != null)
                {
                    AddCommandParameter(cmd, paramName, baos, dbProvider.Metadata.DbBinaryType);
                }
                else
                {
                    AddCommandParameter(cmd, paramName, null, dbProvider.Metadata.DbBinaryType);
                }
                AddCommandParameter(cmd, "triggerPriority", trigger.Priority);

                int insertResult = cmd.ExecuteNonQuery();

                return insertResult;
            }
        }

        /// <summary>
        /// Insert the simple trigger data.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <param name="trigger">The trigger to insert.</param>
        /// <returns>The number of rows inserted.</returns>
        public virtual int InsertSimpleTrigger(ConnectionAndTransactionHolder conn, SimpleTrigger trigger)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlInsertSimpleTrigger)))
            {
                AddCommandParameter(cmd, "triggerName", trigger.Name);
                AddCommandParameter(cmd, "triggerGroup", trigger.Group);
                AddCommandParameter(cmd, "triggerRepeatCount", trigger.RepeatCount);
                AddCommandParameter(cmd, "triggerRepeatInterval", trigger.RepeatInterval.TotalMilliseconds);
                AddCommandParameter(cmd, "triggerTimesTriggered", trigger.TimesTriggered);

                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Insert the cron trigger data.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="trigger">the trigger to insert</param>
        /// <returns>the number of rows inserted</returns>
        public virtual int InsertCronTrigger(ConnectionAndTransactionHolder conn, CronTrigger trigger)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlInsertCronTrigger)))
            {
                AddCommandParameter(cmd, "triggerName", trigger.Name);
                AddCommandParameter(cmd, "triggerGroup", trigger.Group);
                AddCommandParameter(cmd, "triggerCronExpression", trigger.CronExpressionString);
                AddCommandParameter(cmd, "triggerTimeZone", trigger.TimeZone.Id);

                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Insert the blob trigger data.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <param name="trigger">The trigger to insert.</param>
        /// <returns>The number of rows inserted.</returns>
        public virtual int InsertBlobTrigger(ConnectionAndTransactionHolder conn, Trigger trigger)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlInsertBlobTrigger)))
            {
                // update the blob
                byte[] buf = SerializeObject(trigger);
                AddCommandParameter(cmd, "triggerName", trigger.Name);
                AddCommandParameter(cmd, "triggerGroup", trigger.Group);
                AddCommandParameter(cmd, "blob", buf, dbProvider.Metadata.DbBinaryType);

                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Update the base trigger data.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <param name="trigger">The trigger to insert.</param>
        /// <param name="state">The state that the trigger should be stored in.</param>
        /// <param name="jobDetail">The job detail.</param>
        /// <returns>The number of rows updated.</returns>
        public virtual int UpdateTrigger(ConnectionAndTransactionHolder conn, Trigger trigger, string state,
                                         JobDetail jobDetail)
        {
            // save some clock cycles by unnecessarily writing job data blob ...
            bool updateJobData = trigger.JobDataMap.Dirty;
            byte[] baos = null;
            if (updateJobData && trigger.JobDataMap.Count > 0)
            {
                baos = SerializeJobData(trigger.JobDataMap);
            }

            IDbCommand cmd;

            int insertResult;

            if (updateJobData)
            {
                cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateTrigger));
            }
            else
            {
                cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateTriggerSkipData));
            }

            AddCommandParameter(cmd, "triggerJobName", trigger.JobName);
            AddCommandParameter(cmd, "triggerJobGroup", trigger.JobGroup);
            AddCommandParameter(cmd, "triggerVolatile", GetDbBooleanValue(trigger.Volatile));
            AddCommandParameter(cmd, "triggerDescription", trigger.Description);
            long nextFireTime = -1;

            if (trigger.GetNextFireTimeUtc().HasValue)
            {
                nextFireTime = trigger.GetNextFireTimeUtc().Value.Ticks;
            }
            AddCommandParameter(cmd, "triggerNextFireTime", Convert.ToDecimal(nextFireTime));
            long prevFireTime = -1;

            if (trigger.GetPreviousFireTimeUtc().HasValue)
            {
                prevFireTime = trigger.GetPreviousFireTimeUtc().Value.Ticks;
            }
            AddCommandParameter(cmd, "triggerPreviousFireTime", Convert.ToDecimal(prevFireTime));
            AddCommandParameter(cmd, "triggerState", state);
            string paramName = "triggerType";
            if (trigger is SimpleTrigger && !trigger.HasAdditionalProperties)
            {
                // UpdateSimpleTrigger(conn, (SimpleTrigger)trigger);
                AddCommandParameter(cmd, paramName, TriggerTypeSimple);
            }
            else if (trigger is CronTrigger && !trigger.HasAdditionalProperties)
            {
                // UpdateCronTrigger(conn, (CronTrigger)trigger);
                AddCommandParameter(cmd, paramName, TriggerTypeCron);
            }
            else
            {
                // UpdateBlobTrigger(conn, trigger);
                AddCommandParameter(cmd, paramName, TriggerTypeBlob);
            }

            AddCommandParameter(cmd, "triggerStartTime", Convert.ToDecimal(trigger.StartTimeUtc.Ticks));
            long endTime = 0;
            if (trigger.EndTimeUtc.HasValue)
            {
                endTime = trigger.EndTimeUtc.Value.Ticks;
            }
            AddCommandParameter(cmd, "triggerEndTime", Convert.ToDecimal(endTime));
            AddCommandParameter(cmd, "triggerCalendarName", trigger.CalendarName);
            AddCommandParameter(cmd, "triggerMisfireInstruction", trigger.MisfireInstruction);
            AddCommandParameter(cmd, "triggerPriority", trigger.Priority);
            paramName = "triggerJobJobDataMap";
            if (updateJobData)
            {
                if (baos != null)
                {
                    AddCommandParameter(cmd, paramName, baos, dbProvider.Metadata.DbBinaryType);
                }
                else
                {
                    AddCommandParameter(cmd, paramName, null, dbProvider.Metadata.DbBinaryType);
                }
                AddCommandParameter(cmd, "triggerName", trigger.Name);
                AddCommandParameter(cmd, "triggerGroup", trigger.Group);
            }
            else
            {
                AddCommandParameter(cmd, "triggerName", trigger.Name);
                AddCommandParameter(cmd, "triggerGroup", trigger.Group);
            }

            insertResult = cmd.ExecuteNonQuery();

            return insertResult;
        }

        /// <summary>
        /// Update the simple trigger data.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <param name="trigger">The trigger to insert.</param>
        /// <returns>The number of rows updated.</returns>
        public virtual int UpdateSimpleTrigger(ConnectionAndTransactionHolder conn, SimpleTrigger trigger)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateSimpleTrigger)))
            {
                AddCommandParameter(cmd, "triggerRepeatCount", trigger.RepeatCount);
                AddCommandParameter(cmd, "triggerRepeatInterval", trigger.RepeatInterval.TotalMilliseconds);
                AddCommandParameter(cmd, "triggerTimesTriggered", trigger.TimesTriggered);
                AddCommandParameter(cmd, "triggerName", trigger.Name);
                AddCommandParameter(cmd, "triggerGroup", trigger.Group);

                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Update the cron trigger data.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <param name="trigger">The trigger to insert.</param>
        /// <returns>The number of rows updated.</returns>
        public virtual int UpdateCronTrigger(ConnectionAndTransactionHolder conn, CronTrigger trigger)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateCronTrigger)))
            {
                AddCommandParameter(cmd, "triggerCronExpression", trigger.CronExpressionString);
                AddCommandParameter(cmd, "triggerName", trigger.Name);
                AddCommandParameter(cmd, "triggerGroup", trigger.Group);

                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Update the blob trigger data.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <param name="trigger">The trigger to insert.</param>
        /// <returns>The number of rows updated.</returns>
        public virtual int UpdateBlobTrigger(ConnectionAndTransactionHolder conn, Trigger trigger)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateBlobTrigger)))
            {
                // update the blob
                byte[] os = SerializeObject(trigger);

                AddCommandParameter(cmd, "blob", os, dbProvider.Metadata.DbBinaryType);
                AddCommandParameter(cmd, "triggerName", trigger.Name);
                AddCommandParameter(cmd, "triggerGroup", trigger.Group);

                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Check whether or not a trigger exists.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <param name="triggerName">The name of the trigger.</param>
        /// <param name="groupName">The group containing the trigger.</param>
        /// <returns>true if the trigger exists, false otherwise</returns>
        public virtual bool TriggerExists(ConnectionAndTransactionHolder conn, string triggerName, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggerExistence)))
            {
                AddCommandParameter(cmd, "triggerName", triggerName);
                AddCommandParameter(cmd, "triggerGroup", groupName);

                using (IDataReader dr = cmd.ExecuteReader())
                {
                    if (dr.Read())
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
        }

        /// <summary>
        /// Update the state for a given trigger.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <param name="triggerName">The name of the trigger.</param>
        /// <param name="groupName">The group containing the trigger.</param>
        /// <param name="state">The new state for the trigger.</param>
        /// <returns>The number of rows updated.</returns>
        public virtual int UpdateTriggerState(ConnectionAndTransactionHolder conn, string triggerName, string groupName,
                                              string state)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateTriggerState)))
            {
                AddCommandParameter(cmd, "state", state);
                AddCommandParameter(cmd, "triggerName", triggerName);
                AddCommandParameter(cmd, "triggerGroup", groupName);

                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Update the given trigger to the given new state, if it is one of the
        /// given old states.
        /// </summary>
        /// <param name="conn">The DB connection.</param>
        /// <param name="triggerName">The name of the trigger.</param>
        /// <param name="groupName">The group containing the trigger.</param>
        /// <param name="newState">The new state for the trigger.</param>
        /// <param name="oldState1">One of the old state the trigger must be in.</param>
        /// <param name="oldState2">One of the old state the trigger must be in.</param>
        /// <param name="oldState3">One of the old state the trigger must be in.</param>
        /// <returns>The number of rows updated.</returns>
        public virtual int UpdateTriggerStateFromOtherStates(ConnectionAndTransactionHolder conn, string triggerName,
                                                             string groupName,
                                                             string newState, string oldState1, string oldState2,
                                                             string oldState3)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateTriggerStateFromStates)))
            {
                AddCommandParameter(cmd, "newState", newState);
                AddCommandParameter(cmd, "triggerName", triggerName);
                AddCommandParameter(cmd, "triggerGroup", groupName);
                AddCommandParameter(cmd, "oldState1", oldState1);
                AddCommandParameter(cmd, "oldState2", oldState2);
                AddCommandParameter(cmd, "oldState3", oldState3);

                return cmd.ExecuteNonQuery();
            }
        }


        /// <summary>
        /// Update the all triggers to the given new state, if they are in one of
        /// the given old states AND its next fire time is before the given time.
        /// </summary>
        /// <param name="conn">The DB connection</param>
        /// <param name="newState">The new state for the trigger</param>
        /// <param name="oldState1">One of the old state the trigger must be in</param>
        /// <param name="oldState2">One of the old state the trigger must be in</param>
        /// <param name="time">The time before which the trigger's next fire time must be</param>
        /// <returns>int the number of rows updated</returns>
        public virtual int UpdateTriggerStateFromOtherStatesBeforeTime(ConnectionAndTransactionHolder conn,
                                                                       string newState,
                                                                       string oldState1,
                                                                       string oldState2, long time)
        {
            using (
                IDbCommand cmd =
                    PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateTriggerStateFromOtherStatesBeforeTime)))
            {
                AddCommandParameter(cmd, "newState", newState);
                AddCommandParameter(cmd, "oldState1", oldState1);
                AddCommandParameter(cmd, "oldState2", oldState2);
                AddCommandParameter(cmd, "time", time);

                return cmd.ExecuteNonQuery();
            }
        }


        /// <summary>
        /// Update all triggers in the given group to the given new state, if they
        /// are in one of the given old states.
        /// </summary>
        /// <param name="conn">The DB connection.</param>
        /// <param name="groupName">The group containing the trigger.</param>
        /// <param name="newState">The new state for the trigger.</param>
        /// <param name="oldState1">One of the old state the trigger must be in.</param>
        /// <param name="oldState2">One of the old state the trigger must be in.</param>
        /// <param name="oldState3">One of the old state the trigger must be in.</param>
        /// <returns>The number of rows updated.</returns>
        public virtual int UpdateTriggerGroupStateFromOtherStates(ConnectionAndTransactionHolder conn, string groupName,
                                                                  string newState,
                                                                  string oldState1, string oldState2, string oldState3)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateTriggerGroupStateFromStates)))
            {
                AddCommandParameter(cmd, "newState", newState);
                AddCommandParameter(cmd, "groupName", groupName);
                AddCommandParameter(cmd, "oldState1", oldState1);
                AddCommandParameter(cmd, "oldState2", oldState2);
                AddCommandParameter(cmd, "oldState3", oldState3);

                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Update the given trigger to the given new state, if it is in the given
        /// old state.
        /// </summary>
        /// <param name="conn">the DB connection</param>
        /// <param name="triggerName">the name of the trigger</param>
        /// <param name="groupName">the group containing the trigger</param>
        /// <param name="newState">the new state for the trigger</param>
        /// <param name="oldState">the old state the trigger must be in</param>
        /// <returns>int the number of rows updated</returns>
        public virtual int UpdateTriggerStateFromOtherState(ConnectionAndTransactionHolder conn, string triggerName,
                                                            string groupName,
                                                            string newState, string oldState)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateTriggerStateFromState)))
            {
                AddCommandParameter(cmd, "newState", newState);
                AddCommandParameter(cmd, "triggerName", triggerName);
                AddCommandParameter(cmd, "triggerGroup", groupName);
                AddCommandParameter(cmd, "oldState", oldState);

                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Update all of the triggers of the given group to the given new state, if
        /// they are in the given old state.
        /// </summary>
        /// <param name="conn">the DB connection</param>
        /// <param name="groupName">the group containing the triggers</param>
        /// <param name="newState">the new state for the trigger group</param>
        /// <param name="oldState">the old state the triggers must be in</param>
        /// <returns>int the number of rows updated</returns>
        public virtual int UpdateTriggerGroupStateFromOtherState(ConnectionAndTransactionHolder conn, string groupName,
                                                                 string newState,
                                                                 string oldState)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlTriggerGroupStateFromState)))
            {
                AddCommandParameter(cmd, "newState", newState);
                AddCommandParameter(cmd, "triggerGroup", groupName);
                AddCommandParameter(cmd, "oldState", oldState);

                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Update the states of all triggers associated with the given job.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="jobName">the name of the job</param>
        /// <param name="groupName">the group containing the job</param>
        /// <param name="state">the new state for the triggers</param>
        /// <returns>the number of rows updated</returns>
        public virtual int UpdateTriggerStatesForJob(ConnectionAndTransactionHolder conn, string jobName,
                                                     string groupName, string state)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateJobTriggerStates)))
            {
                AddCommandParameter(cmd, "state", state);
                AddCommandParameter(cmd, "jobName", jobName);
                AddCommandParameter(cmd, "jobGroup", groupName);

                return cmd.ExecuteNonQuery();
            }
        }


        /// <summary>
        /// Updates the state of the trigger states for job from other.
        /// </summary>
        /// <param name="conn">The conn.</param>
        /// <param name="jobName">Name of the job.</param>
        /// <param name="groupName">Name of the group.</param>
        /// <param name="state">The state.</param>
        /// <param name="oldState">The old state.</param>
        /// <returns></returns>
        public virtual int UpdateTriggerStatesForJobFromOtherState(ConnectionAndTransactionHolder conn, string jobName,
                                                                   string groupName,
                                                                   string state, string oldState)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateJobTriggerStatesFromOtherState))
                )
            {
                AddCommandParameter(cmd, "state", state);
                AddCommandParameter(cmd, "jobName", jobName);
                AddCommandParameter(cmd, "jobGroup", groupName);
                AddCommandParameter(cmd, "oldState", oldState);

                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Delete the simple trigger data for a trigger.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="triggerName">the name of the trigger</param>
        /// <param name="groupName">the group containing the trigger</param>
        /// <returns>the number of rows deleted</returns>
        public virtual int DeleteSimpleTrigger(ConnectionAndTransactionHolder conn, string triggerName, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteSimpleTrigger)))
            {
                AddCommandParameter(cmd, "triggerName", triggerName);
                AddCommandParameter(cmd, "triggerGroup", groupName);

                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Delete the cron trigger data for a trigger.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="triggerName">the name of the trigger</param>
        /// <param name="groupName">the group containing the trigger</param>
        /// <returns>the number of rows deleted</returns>
        public virtual int DeleteCronTrigger(ConnectionAndTransactionHolder conn, string triggerName, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteCronTrigger)))
            {
                AddCommandParameter(cmd, "triggerName", triggerName);
                AddCommandParameter(cmd, "triggerGroup", groupName);

                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Delete the cron trigger data for a trigger.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="triggerName">the name of the trigger</param>
        /// <param name="groupName">the group containing the trigger</param>
        /// <returns>the number of rows deleted</returns>
        public virtual int DeleteBlobTrigger(ConnectionAndTransactionHolder conn, string triggerName, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteBlobTrigger)))
            {
                AddCommandParameter(cmd, "triggerName", triggerName);
                AddCommandParameter(cmd, "triggerGroup", groupName);

                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Delete the base trigger data for a trigger.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="triggerName">the name of the trigger</param>
        /// <param name="groupName">the group containing the trigger</param>
        /// <returns>the number of rows deleted</returns>
        public virtual int DeleteTrigger(ConnectionAndTransactionHolder conn, string triggerName, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteTrigger)))
            {
                AddCommandParameter(cmd, "triggerName", triggerName);
                AddCommandParameter(cmd, "triggerGroup", groupName);

                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Select the number of triggers associated with a given job.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="jobName">the name of the job</param>
        /// <param name="groupName">the group containing the job</param>
        /// <returns>the number of triggers for the given job</returns>
        public virtual int SelectNumTriggersForJob(ConnectionAndTransactionHolder conn, string jobName, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectNumTriggersForJob)))
            {
                AddCommandParameter(cmd, "jobName", jobName);
                AddCommandParameter(cmd, "jobGroup", groupName);

                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        return Convert.ToInt32(rs.GetValue(0), CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        return 0;
                    }
                }
            }
        }

        /// <summary>
        /// Select the job to which the trigger is associated.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="triggerName">the name of the trigger</param>
        /// <param name="groupName">the group containing the trigger</param>
        /// <param name="loadHelper">The load helper.</param>
        /// <returns>The <see cref="JobDetail" /> object associated with the given trigger</returns>
        public virtual JobDetail SelectJobForTrigger(ConnectionAndTransactionHolder conn, string triggerName,
                                                     string groupName,
                                                     ITypeLoadHelper loadHelper)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectJobForTrigger)))
            {
                AddCommandParameter(cmd, "triggerName", triggerName);
                AddCommandParameter(cmd, "triggerGroup", groupName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        JobDetail job = new JobDetail();
                        job.Name = GetString(rs[0]);
                        job.Group = GetString(rs[1]);
                        job.Durable = GetBoolean(rs[2]);
                        job.JobType = loadHelper.LoadType(GetString(rs[3]));
                        job.RequestsRecovery = GetBoolean(rs[4]);

                        return job;
                    }
                    else
                    {
                        if (logger.IsDebugEnabled)
                        {
                            logger.Debug("No job for trigger '" + groupName + "." + triggerName + "'.");
                        }
                        return null;
                    }
                }
            }
        }

        /// <summary>
        /// Select the triggers for a job
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="jobName">the name of the trigger</param>
        /// <param name="groupName">the group containing the trigger</param>
        /// <returns>
        /// an array of <see cref="Trigger" /> objects
        /// associated with a given job.
        /// </returns>
        public virtual IList<Trigger> SelectTriggersForJob(ConnectionAndTransactionHolder conn, string jobName,
                                                      string groupName)
        {
            List<Pair<string, string>> triggerIdentifiers = new List<Pair<string, string>>();
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggersForJob)))
            {
                AddCommandParameter(cmd, "jobName", jobName);
                AddCommandParameter(cmd, "jobGroup", groupName);

                using (IDataReader rs = cmd.ExecuteReader())
                {
                    while (rs.Read())
                    {
                        Pair<string, string> p = new Pair<string, string>();
                        p.First = rs.GetString(0);
                        p.Second = rs.GetString(1);
                        triggerIdentifiers.Add(p);
                    }
                }
            }

            List<Trigger> trigList = new List<Trigger>();
            foreach (Pair<string, string> p in triggerIdentifiers)
            {
                Trigger t = SelectTrigger(conn, p.First, p.Second);
                if (t != null)
                {
                    trigList.Add(t);
                }
            }

            return trigList.ToArray();
        }


        /// <summary>
        /// Select the triggers for a calendar
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <param name="calName">Name of the calendar.</param>
        /// <returns>
        /// An array of <see cref="Trigger" /> objects associated with a given job.
        /// </returns>
        public virtual IList<Trigger> SelectTriggersForCalendar(ConnectionAndTransactionHolder conn, string calName)
        {
            List<Trigger> trigList = new List<Trigger>();
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggersForCalendar)))
            {
                NameValueCollection triggers = new NameValueCollection();
                AddCommandParameter(cmd, "calendarName", calName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    while (rs.Read())
                    {
                        triggers.Add(GetString(rs[ColumnTriggerName]), GetString(rs[ColumnTriggerGroup]));
                    }
                }
                foreach (string key in triggers)
                {
                    trigList.Add(SelectTrigger(conn, key, triggers[key]));
                }
            }

            return trigList.ToArray();
        }


        /// <summary>
        /// Selects the stateful jobs of trigger group.
        /// </summary>
        /// <param name="conn">The database connection.</param>
        /// <param name="groupName">Name of the group.</param>
        /// <returns></returns>
        public virtual IList<Key> SelectStatefulJobsOfTriggerGroup(ConnectionAndTransactionHolder conn, string groupName)
        {
            List<Key> jobList = new List<Key>();
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectStatefulJobsOfTriggerGroup)))
            {
                AddCommandParameter(cmd, "jobGroup", groupName);
                AddCommandParameter(cmd, "isStateful", GetDbBooleanValue(true));
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    while (rs.Read())
                    {
                        jobList.Add(
                            new Key(GetString(rs[ColumnJobName]), GetString(rs[ColumnJobGroup])));
                    }
                }
            }

            return jobList;
        }

        /// <summary>
        /// Select a trigger.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="triggerName">the name of the trigger</param>
        /// <param name="groupName">the group containing the trigger</param>
        /// <returns>The <see cref="Trigger" /> object</returns>
        public virtual Trigger SelectTrigger(ConnectionAndTransactionHolder conn, string triggerName, string groupName)
        {
            Trigger trigger = null;
            string jobName = null;
            string jobGroup = null;
            bool volatility = false;
            string description = null;
            string triggerType = "";
            string calendarName = null;
            int misFireInstr = Int32.MinValue;
            int priority = Int32.MinValue;
            IDictionary map = null;
            DateTimeOffset? pft = null;
            DateTimeOffset? endTimeD = null;
            DateTimeOffset? nft = null;
            DateTimeOffset startTimeD = DateTimeOffset.MinValue;

            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTrigger)))
            {
                AddCommandParameter(cmd, "triggerName", triggerName);
                AddCommandParameter(cmd, "triggerGroup", groupName);

                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        jobName = GetString(rs[ColumnJobName]);
                        jobGroup = GetString(rs[ColumnJobGroup]);
                        volatility = GetBoolean(rs[ColumnIsVolatile]);
                        description = GetString(rs[ColumnDescription]);
                        long nextFireTime = Convert.ToInt64(rs[ColumnNextFireTime], CultureInfo.InvariantCulture);
                        long prevFireTime = Convert.ToInt64(rs[ColumnPreviousFireTime], CultureInfo.InvariantCulture);
                        triggerType = GetString(rs[ColumnTriggerType]);
                        long startTime = Convert.ToInt64(rs[ColumnStartTime], CultureInfo.InvariantCulture);
                        long endTime = Convert.ToInt64(rs[ColumnEndTime], CultureInfo.InvariantCulture);
                        calendarName = GetString(rs[ColumnCalendarName]);
                        misFireInstr = Convert.ToInt32(rs[ColumnMifireInstruction], CultureInfo.InvariantCulture);
                        priority = Convert.ToInt32(rs[ColumnPriority], CultureInfo.InvariantCulture);

                        if (CanUseProperties)
                        {
                            map = GetMapFromProperties(rs, 15);
                        }
                        else
                        {
                            map = (IDictionary)GetObjectFromBlob(rs, 15);
                        }


                        if (nextFireTime > 0)
                        {
                            nft = new DateTimeOffset(nextFireTime, TimeSpan.Zero);
                        }

                        if (prevFireTime > 0)
                        {
                            pft = new DateTimeOffset(prevFireTime, TimeSpan.Zero);
                        }

                        startTimeD = new DateTimeOffset(startTime, TimeSpan.Zero);

                        if (endTime > 0)
                        {
                            endTimeD = new DateTimeOffset(endTime, TimeSpan.Zero);
                        }

                        // done reading
                        rs.Close();

                        if (triggerType.Equals(TriggerTypeSimple))
                        {
                            using (IDbCommand cmd2 = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectSimpleTrigger)))
                            {
                                AddCommandParameter(cmd2, "triggerName", triggerName);
                                AddCommandParameter(cmd2, "triggerGroup", groupName);
                                using (IDataReader rs2 = cmd2.ExecuteReader())
                                {
                                    if (rs2.Read())
                                    {
                                        int repeatCount = Convert.ToInt32(rs2[ColumnRepeatCount], CultureInfo.InvariantCulture);
                                        long repeatInterval = Convert.ToInt64(rs2[ColumnRepeatInterval], CultureInfo.InvariantCulture);
                                        int timesTriggered = Convert.ToInt32(rs2[ColumnTimesTriggered], CultureInfo.InvariantCulture);

                                        SimpleTrigger st =
                                            new SimpleTrigger(triggerName, groupName, jobName, jobGroup, startTimeD, endTimeD,
                                                              repeatCount,
                                                              TimeSpan.FromMilliseconds(repeatInterval));
                                        st.CalendarName = calendarName;
                                        st.MisfireInstruction = misFireInstr;
                                        st.TimesTriggered = timesTriggered;
                                        st.Volatile = volatility;
                                        st.SetNextFireTime(nft);
                                        st.SetPreviousFireTime(pft);
                                        st.Description = description;
                                        st.Priority = priority;
                                        if (null != map)
                                        {
                                            st.JobDataMap = new JobDataMap(map);
                                        }
                                        trigger = st;
                                    }
                                }
                            }
                        }
                        else if (triggerType.Equals(TriggerTypeCron))
                        {
                            using (IDbCommand cmd2 = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectCronTriggers)))
                            {
                                AddCommandParameter(cmd2, "triggerName", triggerName);
                                AddCommandParameter(cmd2, "triggerGroup", groupName);
                                using (IDataReader rs2 = cmd2.ExecuteReader())
                                {
                                    if (rs2.Read())
                                    {
                                        string cronExpr = GetString(rs2[ColumnCronExpression]);
                                        string timeZoneId = GetString(rs2[ColumnTimeZoneId]);

                                        CronTrigger ct = null;
                                        try
                                        {
                                            TimeZoneInfo timeZone = null;
                                            if (timeZoneId != null)
                                            {
                                                timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                                            }
                                            ct = new CronTrigger(triggerName, groupName, jobName, jobGroup, startTimeD, endTimeD, cronExpr, timeZone);
                                        }
                                        catch (Exception ex)
                                        {
                                            logger.Warn("Got error from expression, still continuing", ex);
                                            // expr must be valid, or it never would have
                                            // gotten to the store...
                                        }
                                        if (null != ct)
                                        {
                                            ct.CalendarName = calendarName;
                                            ct.MisfireInstruction = misFireInstr;
                                            ct.Volatile = volatility;
                                            ct.SetNextFireTime(nft);
                                            ct.SetPreviousFireTime(pft);
                                            ct.Description = description;
                                            ct.Priority = priority;
                                            if (null != map)
                                            {
                                                ct.JobDataMap = new JobDataMap(map);
                                            }
                                            trigger = ct;
                                        }
                                    }
                                }
                            }
                        }
                        else if (triggerType.Equals(TriggerTypeBlob))
                        {
                            using (IDbCommand cmd2 = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectBlobTrigger)))
                            {
                                AddCommandParameter(cmd2, "triggerName", triggerName);
                                AddCommandParameter(cmd2, "triggerGroup", groupName);
                                using (IDataReader rs2 = cmd2.ExecuteReader())
                                {
                                    if (rs2.Read())
                                    {
                                        trigger = (Trigger)GetObjectFromBlob(rs2, 2);
                                    }
                                }
                            }
                        }
                        else
                        {
                            throw new Exception("Class for trigger type '" + triggerType + "' not found.");
                        }

                    }
                }
            }

            return trigger;
        }

        protected virtual string GetString(object columnValue)
        {
            if (columnValue == DBNull.Value)
            {
                return null;
            }
            else
            {
                return (string)columnValue;
            }
        }

        protected virtual bool GetBoolean(object columnValue)
        {
            // default to treat values as ints
            if (columnValue != null)
            {
                return Convert.ToInt32(columnValue, CultureInfo.InvariantCulture) == 1;
            }
            else
            {
                throw new ArgumentException("Value must be non-null.", "columnValue");
            }
        }
        /// <summary>
        /// Select a trigger's JobDataMap.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="triggerName">the name of the trigger</param>
        /// <param name="groupName">the group containing the trigger</param>
        /// <returns>The <see cref="JobDataMap" /> of the Trigger, never null, but possibly empty. </returns>
        public virtual JobDataMap SelectTriggerJobDataMap(ConnectionAndTransactionHolder conn, string triggerName,
                                                          string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggerData)))
            {
                AddCommandParameter(cmd, "triggerName", triggerName);
                AddCommandParameter(cmd, "triggerGroup", groupName);

                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        IDictionary map;
                        if (CanUseProperties)
                        {
                            map = GetMapFromProperties(rs, 0);
                        }
                        else
                        {
                            map = (IDictionary) GetObjectFromBlob(rs, 0);
                        }

                        if (null != map)
                        {
                            return new JobDataMap(map);
                        }
                    }
                }
            }

            return new JobDataMap();
        }


        /// <summary>
        /// Select a trigger's state value.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="triggerName">the name of the trigger</param>
        /// <param name="groupName">the group containing the trigger</param>
        /// <returns>The <see cref="Trigger" /> object</returns>
        public virtual string SelectTriggerState(ConnectionAndTransactionHolder conn, string triggerName,
                                                 string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggerState)))
            {
                string state;

                AddCommandParameter(cmd, "triggerName", triggerName);
                AddCommandParameter(cmd, "triggerGroup", groupName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        state = GetString(rs[ColumnTriggerState]);
                    }
                    else
                    {
                        state = StateDeleted;
                    }
                }
                return String.Intern(state);
            }
        }

        /// <summary>
        /// Select a trigger status (state and next fire time).
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="triggerName">the name of the trigger</param>
        /// <param name="groupName">the group containing the trigger</param>
        /// <returns>
        /// a <see cref="TriggerStatus" /> object, or null
        /// </returns>
        public virtual TriggerStatus SelectTriggerStatus(ConnectionAndTransactionHolder conn, string triggerName,
                                                         string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggerStatus)))
            {
                TriggerStatus status = null;

                AddCommandParameter(cmd, "triggerName", triggerName);
                AddCommandParameter(cmd, "triggerGroup", groupName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        string state = GetString(rs[ColumnTriggerState]);
                        long nextFireTime = Convert.ToInt64(rs[ColumnNextFireTime], CultureInfo.InvariantCulture);
                        string jobName = GetString(rs[ColumnJobName]);
                        string jobGroup = GetString(rs[ColumnJobGroup]);

                        DateTimeOffset? nft = null;

                        if (nextFireTime > 0)
                        {
                            nft = new DateTimeOffset(nextFireTime, TimeSpan.Zero);
                        }

                        status = new TriggerStatus(state, nft);
                        status.Key = new Key(triggerName, groupName);
                        status.JobKey = new Key(jobName, jobGroup);
                    }
                }
                return status;
            }
        }

        /// <summary>
        /// Select the total number of triggers stored.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <returns>the total number of triggers stored</returns>
        public virtual int SelectNumTriggers(ConnectionAndTransactionHolder conn)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectNumTriggers)))
            {
                int count = 0;

                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        count = Convert.ToInt32(rs.GetInt32(0));
                    }
                }

                return count;
            }
        }

        /// <summary>
        /// Select all of the trigger group names that are stored.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <returns>
        /// an array of <see cref="String" /> group names
        /// </returns>
        public virtual IList<string> SelectTriggerGroups(ConnectionAndTransactionHolder conn)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggerGroups)))
            {
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    List<string> list = new List<string>();
                    while (rs.Read())
                    {
                        list.Add((string) rs[0]);
                    }

                    return list.ToArray();
                }
            }
        }

        /// <summary>
        /// Select all of the triggers contained in a given group.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="groupName">the group containing the triggers</param>
        /// <returns>
        /// an array of <see cref="String" /> trigger names
        /// </returns>
        public virtual IList<string> SelectTriggersInGroup(ConnectionAndTransactionHolder conn, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggersInGroup)))
            {
                AddCommandParameter(cmd, "triggerGroup", groupName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    List<string> list = new List<string>();
                    while (rs.Read())
                    {
                        list.Add((string) rs[0]);
                    }

                    return list.ToArray();
                }
            }
        }


        /// <summary>
        /// Inserts the paused trigger group.
        /// </summary>
        /// <param name="conn">The conn.</param>
        /// <param name="groupName">Name of the group.</param>
        /// <returns></returns>
        public virtual int InsertPausedTriggerGroup(ConnectionAndTransactionHolder conn, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlInsertPausedTriggerGroup)))
            {
                AddCommandParameter(cmd, "triggerGroup", groupName);
                int rows = cmd.ExecuteNonQuery();

                return rows;
            }
        }


        /// <summary>
        /// Deletes the paused trigger group.
        /// </summary>
        /// <param name="conn">The conn.</param>
        /// <param name="groupName">Name of the group.</param>
        /// <returns></returns>
        public virtual int DeletePausedTriggerGroup(ConnectionAndTransactionHolder conn, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlDeletePausedTriggerGroup)))
            {
                AddCommandParameter(cmd, "triggerGroup", groupName);
                int rows = cmd.ExecuteNonQuery();

                return rows;
            }
        }


        /// <summary>
        /// Deletes all paused trigger groups.
        /// </summary>
        /// <param name="conn">The conn.</param>
        /// <returns></returns>
        public virtual int DeleteAllPausedTriggerGroups(ConnectionAndTransactionHolder conn)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlDeletePausedTriggerGroups)))
            {
                int rows = cmd.ExecuteNonQuery();
                return rows;
            }
        }


        /// <summary>
        /// Determines whether the specified trigger group is paused.
        /// </summary>
        /// <param name="conn">The conn.</param>
        /// <param name="groupName">Name of the group.</param>
        /// <returns>
        /// 	<c>true</c> if trigger group is paused; otherwise, <c>false</c>.
        /// </returns>
        public virtual bool IsTriggerGroupPaused(ConnectionAndTransactionHolder conn, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectPausedTriggerGroup)))
            {
                AddCommandParameter(cmd, "triggerGroup", groupName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    return rs.Read();
                }
            }
        }


        /// <summary>
        /// Determines whether given trigger group already exists.
        /// </summary>
        /// <param name="conn">The conn.</param>
        /// <param name="groupName">Name of the group.</param>
        /// <returns>
        /// 	<c>true</c> if trigger group exists; otherwise, <c>false</c>.
        /// </returns>
        public virtual bool IsExistingTriggerGroup(ConnectionAndTransactionHolder conn, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectNumTriggersInGroup)))
            {
                AddCommandParameter(cmd, "triggerGroup", groupName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (!rs.Read())
                    {
                        return false;
                    }

                    return (Convert.ToInt32(rs.GetInt32(0)) > 0);
                }
            }
        }

        //---------------------------------------------------------------------------
        // calendars
        //---------------------------------------------------------------------------

        /// <summary>
        /// Insert a new calendar.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="calendarName">The name for the new calendar.</param>
        /// <param name="calendar">The calendar.</param>
        /// <returns>the number of rows inserted</returns>
        /// <throws>  IOException </throws>
        public virtual int InsertCalendar(ConnectionAndTransactionHolder conn, string calendarName, ICalendar calendar)
        {
            byte[] baos = SerializeObject(calendar);

            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlInsertCalendar)))
            {
                AddCommandParameter(cmd, "calendarName", calendarName);
                AddCommandParameter(cmd, "calendar", baos, dbProvider.Metadata.DbBinaryType);

                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Update a calendar.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="calendarName">The name for the new calendar.</param>
        /// <param name="calendar">The calendar.</param>
        /// <returns>the number of rows updated</returns>
        /// <throws>  IOException </throws>
        public virtual int UpdateCalendar(ConnectionAndTransactionHolder conn, string calendarName, ICalendar calendar)
        {
            byte[] baos = SerializeObject(calendar);

            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateCalendar)))
            {
                AddCommandParameter(cmd, "calendar", baos, dbProvider.Metadata.DbBinaryType);
                AddCommandParameter(cmd, "calendarName", calendarName);

                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Check whether or not a calendar exists.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="calendarName">The name of the calendar.</param>
        /// <returns>
        /// true if the trigger exists, false otherwise
        /// </returns>
        public virtual bool CalendarExists(ConnectionAndTransactionHolder conn, string calendarName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectCalendarExistence)))
            {
                AddCommandParameter(cmd, "calendarName", calendarName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        return true;
                    }
                    
                    return false;
                }
            }
        }

        /// <summary>
        /// Select a calendar.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="calendarName">The name of the calendar.</param>
        /// <returns>the Calendar</returns>
        /// <throws>  ClassNotFoundException </throws>
        /// <throws>  IOException </throws>
        public virtual ICalendar SelectCalendar(ConnectionAndTransactionHolder conn, string calendarName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectCalendar)))
            {
                AddCommandParameter(cmd, "calendarName", calendarName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    ICalendar cal = null;
                    if (rs.Read())
                    {
                        cal = (ICalendar)GetObjectFromBlob(rs, 1);
                    }
                    if (null == cal)
                    {
                        logger.Warn("Couldn't find calendar with name '" + calendarName + "'.");
                    }
                    return cal;
                }
            }
        }

        /// <summary>
        /// Check whether or not a calendar is referenced by any triggers.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="calendarName">The name of the calendar.</param>
        /// <returns>
        /// true if any triggers reference the calendar, false otherwise
        /// </returns>
        public virtual bool CalendarIsReferenced(ConnectionAndTransactionHolder conn, string calendarName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectReferencedCalendar)))
            {
                AddCommandParameter(cmd, "calendarName", calendarName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        return true;
                    }
                    
                    return false;
                }
            }
        }

        /// <summary>
        /// Delete a calendar.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="calendarName">The name of the trigger.</param>
        /// <returns>the number of rows deleted</returns>
        public virtual int DeleteCalendar(ConnectionAndTransactionHolder conn, string calendarName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteCalendar)))
            {
                AddCommandParameter(cmd, "calendarName", calendarName);
                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Select the total number of calendars stored.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <returns>the total number of calendars stored</returns>
        public virtual int SelectNumCalendars(ConnectionAndTransactionHolder conn)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectNumCalendars)))
            {
                int count = 0;
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        count = Convert.ToInt32(rs.GetValue(0), CultureInfo.InvariantCulture);
                    }
                    return count;
                }
            }
        }

        /// <summary>
        /// Select all of the stored calendars.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <returns>
        /// an array of <see cref="String" /> calendar names
        /// </returns>
        public virtual IList<string> SelectCalendars(ConnectionAndTransactionHolder conn)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectCalendars)))
            {
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    List<string> list = new List<string>();
                    while (rs.Read())
                    {
                        list.Add((string) rs[0]);
                    }
                    return list.ToArray();
                }
            }
        }

        //---------------------------------------------------------------------------
        // trigger firing
        //---------------------------------------------------------------------------

        /// <summary>
        /// Select the trigger that will be fired at the given fire time.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="fireTime">the time that the trigger will be fired</param>
        /// <returns>
        /// a <see cref="Key" /> representing the
        /// trigger that will be fired at the given fire time, or null if no
        /// trigger will be fired at that time
        /// </returns>
        public virtual Key SelectTriggerForFireTime(ConnectionAndTransactionHolder conn, DateTimeOffset fireTime)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggerForFireTime)))
            {
                AddCommandParameter(cmd, "state", StateWaiting);
                AddCommandParameter(cmd, "fireTime", Convert.ToDecimal(fireTime.Ticks));

                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        return
                            new Key(GetString(rs[ColumnTriggerName]), GetString(rs[ColumnTriggerGroup]));
                    }
                    
                    return null;
                }
            }
        }

        /// <summary>
        /// Select the next trigger which will fire to fire between the two given timestamps
        /// in ascending order of fire time, and then descending by priority.
        /// </summary>
        /// <param name="conn">The conn.</param>
        /// <param name="noLaterThan">highest value of <see cref="Trigger.GetNextFireTimeUtc"/> of the triggers (exclusive)</param>
        /// <param name="noEarlierThan">highest value of <see cref="Trigger.GetNextFireTimeUtc"/> of the triggers (inclusive)</param>
        /// <returns></returns>
        public virtual IList<Key> SelectTriggerToAcquire(ConnectionAndTransactionHolder conn, DateTimeOffset noLaterThan, DateTimeOffset noEarlierThan)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectNextTriggerToAcquire)))
            {
                List<Key> nextTriggers = new List<Key>();

                AddCommandParameter(cmd, "state", StateWaiting);
                AddCommandParameter(cmd, "noLaterThan", Convert.ToDecimal(noLaterThan.Ticks));
                AddCommandParameter(cmd, "noEarlierThan", Convert.ToDecimal(noEarlierThan.Ticks));
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    int limit = TriggersToAcquireLimit;
                    while (rs.Read() && nextTriggers.Count < limit)
                    {
                        nextTriggers.Add(new Key((string) rs[ColumnTriggerName] , (string) rs[ColumnTriggerGroup]));
                    }
                }
                return nextTriggers;
            }
        }

        /// <summary>
        /// Gets the triggers to acquire limit.
        /// </summary>
        /// <value>The triggers to acquire limit.</value>
        protected virtual int TriggersToAcquireLimit
        {
            get { return DefaultTriggersToAcquireLimit; }
            }

        /// <summary>
        /// Gets the select next trigger to acquire SQL clause.
        /// This can be overriden for a more performant, result limiting 
        /// SQL. For Example SQL Server, MySQL and SQLite support limiting returned rows. 
        /// </summary>
        /// <returns></returns>
        protected virtual string GetSelectNextTriggerToAcquireSql()
        {
            return SqlSelectNextTriggerToAcquire;
        }

        /// <summary>
        /// Insert a fired trigger.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="trigger">the trigger</param>
        /// <param name="state">the state that the trigger should be stored in</param>
        /// <param name="job">The job.</param>
        /// <returns>the number of rows inserted</returns>
        public virtual int InsertFiredTrigger(ConnectionAndTransactionHolder conn, Trigger trigger, string state,
                                              JobDetail job)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlInsertFiredTrigger)))
            {
                AddCommandParameter(cmd, "triggerEntryId", trigger.FireInstanceId);
                AddCommandParameter(cmd, "triggerName", trigger.Name);
                AddCommandParameter(cmd, "triggerGroup", trigger.Group);
                AddCommandParameter(cmd, "triggerVolatile", GetDbBooleanValue(trigger.Volatile));
                AddCommandParameter(cmd, "triggerInstanceName", instanceId);
                AddCommandParameter(cmd, "triggerFireTime",
                                    Convert.ToDecimal(trigger.GetNextFireTimeUtc().Value.Ticks));
                AddCommandParameter(cmd, "triggerState", state);
                if (job != null)
                {
                    AddCommandParameter(cmd, "triggerJobName", trigger.JobName);
                    AddCommandParameter(cmd, "triggerJobGroup", trigger.JobGroup);
                    AddCommandParameter(cmd, "triggerJobStateful", GetDbBooleanValue(job.Stateful));
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

                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Select the states of all fired-trigger records for a given trigger, or
        /// trigger group if trigger name is <see langword="null" />.
        /// </summary>
        /// <param name="conn">The DB connection.</param>
        /// <param name="triggerName">Name of the trigger.</param>
        /// <param name="groupName">Name of the group.</param>
        /// <returns>a List of <see cref="FiredTriggerRecord" /> objects.</returns>
        public virtual IList<FiredTriggerRecord> SelectFiredTriggerRecords(ConnectionAndTransactionHolder conn, string triggerName,
                                                       string groupName)
        {
            IDbCommand cmd;

            IList<FiredTriggerRecord> lst = new List<FiredTriggerRecord>();

            if (triggerName != null)
            {
                cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectFiredTrigger));
                AddCommandParameter(cmd, "triggerName", triggerName);
                AddCommandParameter(cmd, "triggerGroup", groupName);
            }
            else
            {
                cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectFiredTriggerGroup));
                AddCommandParameter(cmd, "triggerGroup", groupName);
            }

            using (IDataReader rs = cmd.ExecuteReader())
            {
                while (rs.Read())
                {
                    FiredTriggerRecord rec = new FiredTriggerRecord();

                    rec.FireInstanceId = GetString(rs[ColumnEntryId]);
                    rec.FireInstanceState = GetString(rs[ColumnEntryState]);
                    rec.FireTimestamp = Convert.ToInt64(rs[ColumnFiredTime], CultureInfo.InvariantCulture);
                    rec.Priority = Convert.ToInt32(rs[ColumnPriority], CultureInfo.InvariantCulture);
                    rec.SchedulerInstanceId = GetString(rs[ColumnInstanceName]);
                    rec.TriggerIsVolatile = GetBoolean(rs[ColumnIsVolatile]);
                    rec.TriggerKey = new Key(GetString(rs[ColumnTriggerName]), GetString(rs[ColumnTriggerGroup]));
                    if (!rec.FireInstanceState.Equals(StateAcquired))
                    {
                        rec.JobIsStateful = GetBoolean(rs[ColumnIsStateful]);
                        rec.JobRequestsRecovery = GetBoolean(rs[ColumnRequestsRecovery]);
                        rec.JobKey =
                            new Key(GetString(rs[ColumnJobName]), GetString(rs[ColumnJobGroup]));
                    }
                    lst.Add(rec);
                }
            }
            return lst;
        }

        /// <summary>
        /// Select the states of all fired-trigger records for a given job, or job
        /// group if job name is <see langword="null" />.
        /// </summary>
        /// <param name="conn">The DB connection.</param>
        /// <param name="jobName">Name of the job.</param>
        /// <param name="groupName">Name of the group.</param>
        /// <returns>a List of <see cref="FiredTriggerRecord" /> objects.</returns>
        public virtual IList<FiredTriggerRecord> SelectFiredTriggerRecordsByJob(ConnectionAndTransactionHolder conn, string jobName,
                                                            string groupName)
        {
            IList<FiredTriggerRecord> lst = new List<FiredTriggerRecord>();

            IDbCommand cmd;
            if (jobName != null)
            {
                cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectFiredTriggersOfJob));
                AddCommandParameter(cmd, "jobName", jobName);
                AddCommandParameter(cmd, "jobGroup", groupName);
            }
            else
            {
                cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectFiredTriggersOfJobGroup));
                AddCommandParameter(cmd, "jobGroup", groupName);
            }

            using (IDataReader rs = cmd.ExecuteReader())
            {
                while (rs.Read())
                {
                    FiredTriggerRecord rec = new FiredTriggerRecord();

                    rec.FireInstanceId = GetString(rs[ColumnEntryId]);
                    rec.FireInstanceState = GetString(rs[ColumnEntryState]);
                    rec.FireTimestamp = Convert.ToInt64(rs[ColumnFiredTime], CultureInfo.InvariantCulture);
                    rec.Priority = Convert.ToInt32(rs[ColumnPriority], CultureInfo.InvariantCulture);
                    rec.SchedulerInstanceId = GetString(rs[ColumnInstanceName]);
                    rec.TriggerIsVolatile = GetBoolean(rs[ColumnIsVolatile]);
                    rec.TriggerKey = new Key(GetString(rs[ColumnTriggerName]), GetString(rs[ColumnTriggerGroup]));
                    if (!rec.FireInstanceState.Equals(StateAcquired))
                    {
                        rec.JobIsStateful = GetBoolean(rs[ColumnIsStateful]);
                        rec.JobRequestsRecovery = GetBoolean(rs[ColumnRequestsRecovery]);
                        rec.JobKey = new Key(GetString(rs[ColumnJobName]), GetString(rs[ColumnJobGroup]));
                    }
                    lst.Add(rec);
                }
            }
            return lst;
        }


        /// <summary>
        /// Select the states of all fired-trigger records for a given scheduler
        /// instance.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="instanceName">Name of the instance.</param>
        /// <returns>A list of FiredTriggerRecord objects.</returns>
        public virtual IList<FiredTriggerRecord> SelectInstancesFiredTriggerRecords(ConnectionAndTransactionHolder conn, string instanceName)
        {
            IList<FiredTriggerRecord> lst = new List<FiredTriggerRecord>();

            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectInstancesFiredTriggers)))
            {
                AddCommandParameter(cmd, "instanceName", instanceName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    while (rs.Read())
                    {
                        FiredTriggerRecord rec = new FiredTriggerRecord();

                        rec.FireInstanceId = GetString(rs[ColumnEntryId]);
                        rec.FireInstanceState = GetString(rs[ColumnEntryState]);
                        rec.FireTimestamp = Convert.ToInt64(rs[ColumnFiredTime], CultureInfo.InvariantCulture);
                        rec.SchedulerInstanceId = GetString(rs[ColumnInstanceName]);
                        rec.TriggerIsVolatile = GetBoolean(rs[ColumnIsVolatile]);
                        rec.TriggerKey = new Key(GetString(rs[ColumnTriggerName]), GetString(rs[ColumnTriggerGroup]));
                        if (!rec.FireInstanceState.Equals(StateAcquired))
                        {
                            rec.JobIsStateful = GetBoolean(rs[ColumnIsStateful]);
                            rec.JobRequestsRecovery = GetBoolean(rs[ColumnRequestsRecovery]);
                            rec.JobKey = new Key(GetString(rs[ColumnJobName]), GetString(rs[ColumnJobGroup]));
                        }
                        lst.Add(rec);
                    }
                }

                return lst;
            }
        }




        /// <summary>
        /// Select the distinct instance names of all fired-trigger records.
        /// </summary>
        /// <param name="conn">The conn.</param>
        /// <returns></returns>
        /// <remarks>
        /// This is useful when trying to identify orphaned fired triggers (a
        /// fired trigger without a scheduler state record.)
        /// </remarks>
        public Collection.ISet<string> SelectFiredTriggerInstanceNames(ConnectionAndTransactionHolder conn)
        {
            Collection.HashSet<string> instanceNames = new Collection.HashSet<string>();
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectFiredTriggerInstanceNames)))
            {
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    while (rs.Read())
                    {
                        instanceNames.Add((string) rs[ColumnInstanceName]);
                    }

                    return instanceNames;
                }
            }
        }

        /// <summary>
        /// Delete a fired trigger.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="entryId">the fired trigger entry to delete</param>
        /// <returns>the number of rows deleted</returns>
        public virtual int DeleteFiredTrigger(ConnectionAndTransactionHolder conn, string entryId)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteFiredTrigger)))
            {
                AddCommandParameter(cmd, "triggerEntryId", entryId);
                return cmd.ExecuteNonQuery();
            }
        }


        /// <summary>
        /// Selects the job execution count.
        /// </summary>
        /// <param name="conn">The DB connection.</param>
        /// <param name="jobName">Name of the job.</param>
        /// <param name="jobGroup">The job group.</param>
        /// <returns></returns>
        public virtual int SelectJobExecutionCount(ConnectionAndTransactionHolder conn, string jobName, string jobGroup)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectJobExecutionCount)))
            {
                AddCommandParameter(cmd, "jobName", jobName);
                AddCommandParameter(cmd, "jobGroup", jobGroup);

                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        return Convert.ToInt32(rs.GetValue(0), CultureInfo.InvariantCulture);
                    }
                    
                    return 0;
                }
            }
        }


        /// <summary>
        /// Delete all volatile fired triggers.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <returns>The number of rows deleted</returns>
        public virtual int DeleteVolatileFiredTriggers(ConnectionAndTransactionHolder conn)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteVolatileFiredTriggers)))
            {
                AddCommandParameter(cmd, "volatile", true);
                return cmd.ExecuteNonQuery();
            }
        }


        /// <summary>
        /// Inserts the state of the scheduler.
        /// </summary>
        /// <param name="conn">The conn.</param>
        /// <param name="instanceName">The instance id.</param>
        /// <param name="checkInTime">The check in time.</param>
        /// <param name="interval">The interval.</param>
        /// <returns></returns>
        public virtual int InsertSchedulerState(ConnectionAndTransactionHolder conn, string instanceName, DateTimeOffset checkInTime, TimeSpan interval)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlInsertSchedulerState)))
            {
                AddCommandParameter(cmd, "instanceName", instanceName);
                AddCommandParameter(cmd, "lastCheckinTime", checkInTime.Ticks);
                AddCommandParameter(cmd, "checkinInterval", interval.TotalMilliseconds);

                return cmd.ExecuteNonQuery();
            }
        }


        /// <summary>
        /// Deletes the state of the scheduler.
        /// </summary>
        /// <param name="conn">The database connection.</param>
        /// <param name="instanceName">The instance id.</param>
        /// <returns></returns>
        public virtual int DeleteSchedulerState(ConnectionAndTransactionHolder conn, string instanceName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteSchedulerState)))
            {
                AddCommandParameter(cmd, "instanceName", instanceName);

                return cmd.ExecuteNonQuery();
            }
        }


        /// <summary>
        /// Updates the state of the scheduler.
        /// </summary>
        /// <param name="conn">The database connection.</param>
        /// <param name="instanceName">The instance id.</param>
        /// <param name="checkInTime">The check in time.</param>
        /// <returns></returns>
        public virtual int UpdateSchedulerState(ConnectionAndTransactionHolder conn, string instanceName, DateTimeOffset checkInTime)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateSchedulerState)))
            {
                AddCommandParameter(cmd, "lastCheckinTime", checkInTime.Ticks);
                AddCommandParameter(cmd, "instanceName", instanceName);

                return cmd.ExecuteNonQuery();
            }
        }


        /// <summary>
        /// A List of all current <see cref="SchedulerStateRecord" />s.
        /// <p>
        /// If instanceId is not null, then only the record for the identified
        /// instance will be returned.
        /// </p>
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="instanceName">The instance id.</param>
        /// <returns></returns>
        public virtual IList<SchedulerStateRecord> SelectSchedulerStateRecords(ConnectionAndTransactionHolder conn, string instanceName)
        {
            IDbCommand cmd;

            List<SchedulerStateRecord> list = new List<SchedulerStateRecord>();

            if (instanceName != null)
            {
                cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectSchedulerState));
                AddCommandParameter(cmd, "instanceName", instanceName);
            }
            else
            {
                cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectSchedulerStates));
            }
            using (IDataReader rs = cmd.ExecuteReader())
            {
                while (rs.Read())
                {
                    SchedulerStateRecord rec = new SchedulerStateRecord();
                    rec.SchedulerInstanceId = GetString(rs[ColumnInstanceName]);
                    rec.CheckinTimestamp = new DateTimeOffset(Convert.ToInt64(rs[ColumnLastCheckinTime], CultureInfo.InvariantCulture), TimeSpan.Zero);
                    rec.CheckinInterval = TimeSpan.FromMilliseconds(Convert.ToInt64(rs[ColumnCheckinInterval], CultureInfo.InvariantCulture));
                    list.Add(rec);
                }
            }
            return list;
        }

        //---------------------------------------------------------------------------
        // protected methods that can be overridden by subclasses
        //---------------------------------------------------------------------------

        /// <summary>
        /// Replace the table prefix in a query by replacing any occurrences of
        /// "{0}" with the table prefix.
        /// </summary>
        /// <param name="query">The unsubstitued query</param>
        /// <returns>The query, with proper table prefix substituted</returns>
        protected internal string ReplaceTablePrefix(string query)
        {
            return AdoJobStoreUtil.ReplaceTablePrefix(query, tablePrefix);
        }

        /// <summary>
        /// Create a serialized <see lanword="byte[]"/> version of an Object.
        /// </summary>
        /// <param name="obj">the object to serialize</param>
        /// <returns>Serialized object as byte array.</returns>
        protected internal virtual byte[] SerializeObject(object obj)
        {
            byte[] retValue = null;
            if (null != obj)
            {
                MemoryStream ms = new MemoryStream();
                BinaryFormatter bf = new BinaryFormatter();
                bf.Serialize(ms, obj);
                retValue = ms.ToArray();
            }
            return retValue;
        }

        /// <summary>
        /// Remove the transient data from and then create a serialized <see cref="MemoryStream" />
        /// version of a <see cref="JobDataMap" /> and returns the underlying bytes.
        /// </summary>
        /// <param name="data">The data.</param>
        /// <returns>the serialized data as byte array</returns>
        public virtual byte[] SerializeJobData(JobDataMap data)
        {
            if (CanUseProperties)
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
                    GetKeyOfNonSerializableValue((IDictionary) data) +
                    "' is not serializable: " + e.Message);
            }
        }


        protected object GetKeyOfNonSerializableValue(IDictionary data)
        {
                foreach (DictionaryEntry entry in data)
            {
                try
                {
                    SerializeObject(entry.Value);
                }
                catch (Exception)
                {
                    return entry.Key;
                }
            }

            // As long as it is true that the Map was not serializable, we should
            // not hit this case.
            return null;
        }


        /// <summary>
        /// serialize
        /// </summary>
        /// <param name="data">The data.</param>
        /// <returns></returns>
        private byte[] SerializeProperties(JobDataMap data)
        {
            byte[] retValue = null;
            if (null != data)
            {
                NameValueCollection properties = ConvertToProperty(data.WrappedMap);
                retValue = SerializeObject(properties);
            }

            return retValue;
        }

        /// <summary> 
        /// Convert the JobDataMap into a list of properties.
        /// </summary>
        protected virtual IDictionary ConvertFromProperty(NameValueCollection properties)
        {
            IDictionary<string, string> data = new Dictionary<string, string>();
            foreach (string key in properties.AllKeys)
            {
                data[key] = properties[key];
            }

            return (IDictionary) data;
        }

        /// <summary>
        /// Convert the JobDataMap into a list of properties.
        /// </summary>
        protected internal virtual NameValueCollection ConvertToProperty(IDictionary<string, object> data)
        {
            NameValueCollection properties = new NameValueCollection();
            foreach (KeyValuePair<string, object> entry in data)
            {
                object key = entry.Key;
                object val = entry.Value ?? string.Empty;

                if (!(key is string))
                {
                    throw new IOException("JobDataMap keys/values must be Strings " +
                                          "when the 'useProperties' property is set. " +
                                          " offending Key: " + key);
                }
                if (!(val is string))
                {
                    throw new IOException("JobDataMap values must be Strings " +
                                          "when the 'useProperties' property is set. " +
                                          " Key of offending value: " + key);
                }
                properties[(string)key] = (string)val;
            }
            return properties;
        }

        /// <summary>
        /// This method should be overridden by any delegate subclasses that need
        /// special handling for BLOBs. The default implementation uses standard
        /// ADO.NET operations.
        /// </summary>
        /// <param name="rs">The data reader, already queued to the correct row.</param>
        /// <param name="colIndex">The column index for the BLOB.</param>
        /// <returns>The deserialized object from the DataReader BLOB.</returns>
        protected internal virtual object GetObjectFromBlob(IDataReader rs, int colIndex)
        {
            object obj = null;

            byte[] data = ReadBytesFromBlob(rs, colIndex);
            if (data != null && data.Length > 0)
            {
                MemoryStream ms = new MemoryStream(data);
                BinaryFormatter bf = new BinaryFormatter();
                obj = bf.Deserialize(ms);
            }
            return obj;
        }

        protected virtual byte[] ReadBytesFromBlob(IDataReader dr, int colIndex)
        {
            if (dr.IsDBNull(colIndex))
            {
                return null;
            }

            byte[] outbyte = new byte[dr.GetBytes(colIndex, 0, null, 0, Int32.MaxValue)];
            dr.GetBytes(colIndex, 0, outbyte, 0, outbyte.Length);
            using (MemoryStream stream = new MemoryStream())
            {
                stream.Write(outbyte, 0, outbyte.Length);
            }
            return outbyte;
        }


        /// <summary>
        /// Get the names of all of the triggers that are volatile.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <returns>An array of <see cref="Key" /> objects.</returns>
        public virtual IList<Key> SelectVolatileTriggers(ConnectionAndTransactionHolder conn)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectVolatileTriggers)))
            {
                AddCommandParameter(cmd, "isVolatile", GetDbBooleanValue(true));
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    List<Key> list = new List<Key>();
                    while (rs.Read())
                    {
                        string triggerName = GetString(rs[ColumnTriggerName]);
                        string groupName = GetString(rs[ColumnTriggerGroup]);
                        list.Add(new Key(triggerName, groupName));
                    }
                    return list.ToArray();
                }
            }
        }


        /// <summary>
        /// Get the names of all of the jobs that are volatile.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <returns>An array of <see cref="Key" /> objects.</returns>
        public virtual IList<Key> SelectVolatileJobs(ConnectionAndTransactionHolder conn)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectVolatileJobs)))
            {
                AddCommandParameter(cmd, "isVolatile", GetDbBooleanValue(true));
                using (IDataReader dr = cmd.ExecuteReader())
                {
                    List<Key> list = new List<Key>();
                    while (dr.Read())
                    {
                        string triggerName = (string)dr[ColumnJobName];
                        string groupName = (string)dr[ColumnJobGroup];
                        list.Add(new Key(triggerName, groupName));
                    }
                    return list.ToArray();
                }
            }
        }

        /// <summary>
        /// This method should be overridden by any delegate subclasses that need
        /// special handling for BLOBs for job details. 
        /// </summary>
        /// <param name="rs">The result set, already queued to the correct row.</param>
        /// <param name="colIndex">The column index for the BLOB.</param>
        /// <returns>The deserialized Object from the ResultSet BLOB.</returns>
        protected virtual object GetJobDetailFromBlob(IDataReader rs, int colIndex)
        {
            if (CanUseProperties)
            {
                if (!rs.IsDBNull(colIndex))
                {
                    // should be NameValueCollection
                    return GetObjectFromBlob(rs, colIndex);
                }
                
                return null;
            }

            return GetObjectFromBlob(rs, colIndex);
        }


        /// <summary>
        /// Selects the paused trigger groups.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <returns></returns>
        public virtual Collection.ISet<string> SelectPausedTriggerGroups(ConnectionAndTransactionHolder conn)
        {
            Collection.HashSet<string> retValue = new Collection.HashSet<string>();

            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectPausedTriggerGroups)))
            {
                using (IDataReader dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        string groupName = (string)dr[ColumnTriggerGroup];
                        retValue.Add(groupName);
                    }
                }
                return retValue;
            }
        }

        protected virtual IDbCommand PrepareCommand(ConnectionAndTransactionHolder cth, string commandText)
        {
            return adoUtil.PrepareCommand(cth, commandText);
        }


        protected virtual void AddCommandParameter(IDbCommand cmd, string paramName, object paramValue)
        {
            AddCommandParameter(cmd, paramName, paramValue, null);
        }

        protected virtual void AddCommandParameter(IDbCommand cmd, string paramName, object paramValue, Enum dataType)
        {
            adoUtil.AddCommandParameter(cmd, paramName, paramValue, dataType);
        }
    }
}
