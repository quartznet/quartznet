/* 
* Copyright 2004-2005 OpenSymphony 
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

/*
* Previously Copyright (c) 2001-2004 James House
*/

using System;
using System.Collections;
using System.Collections.Specialized;
using System.Configuration;
using System.Data;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

using Common.Logging;

using Nullables;

using Quartz.Collection;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// This is meant to be an abstract base class for most, if not all, <code>IDriverDelegate</code>
    /// implementations. Subclasses should override only those methods that need
    /// special handling for the DBMS driver in question.
    /// </summary>
    /// <author><a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a></author>
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    public class StdAdoDelegate : StdAdoConstants, IDriverDelegate
    {
        protected internal ILog logger = null;
        protected internal string tablePrefix = AdoConstants.DEFAULT_TABLE_PREFIX;
        protected internal string instanceId;
        protected internal bool useProperties;
        protected internal IDbProvider dbProvider;

        /*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Constructors.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

        /// <summary>
        /// Create new StdAdoDelegate instance.
        /// </summary>
        /// <param name="logger">the logger to use during execution</param>
        /// <param name="tablePrefix">the prefix of all table names</param>
        /// <param name="instanceId">The instance id.</param>
        public StdAdoDelegate(ILog logger, string tablePrefix, string instanceId)
        {
            this.logger = logger;
            this.tablePrefix = tablePrefix;
            this.instanceId = instanceId;
        }

        /// <summary>
        /// Create new StdAdoDelegate instance.
        /// </summary>
        /// <param name="logger">the logger to use during execution</param>
        /// <param name="tablePrefix">the prefix of all table names</param>
        /// <param name="instanceId">The instance id.</param>
        /// <param name="useProperties">if set to <c>true</c> [use properties].</param>
        public StdAdoDelegate(ILog logger, string tablePrefix, string instanceId, bool useProperties)
        {
            this.logger = logger;
            this.tablePrefix = tablePrefix;
            this.instanceId = instanceId;
            this.useProperties = useProperties;
        }

        /*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Interface.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

        protected internal virtual bool CanUseProperties
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
        public virtual int UpdateTriggerStatesFromOtherStates(IDbConnection conn, string newState, string oldState1,
                                                              string oldState2)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_TRIGGER_STATES_FROM_OTHER_STATES)))
            {
                AddCommandParameter(cmd, 1, newState);
                AddCommandParameter(cmd, 2, oldState1);
                AddCommandParameter(cmd, 3, oldState2);
                return cmd.ExecuteNonQuery();
            }
        }


        /// <summary>
        /// Get the names of all of the triggers that have misfired.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="ts">The ts.</param>
        /// <returns>an array of <code>Key</code> objects</returns>
        public virtual Key[] SelectMisfiredTriggers(IDbConnection conn, long ts)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_MISFIRED_TRIGGERS)))
            {
                AddCommandParameter(cmd, 1, ts);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    ArrayList list = new ArrayList();
                    while (rs.Read())
                    {
                        string triggerName = Convert.ToString(rs[AdoConstants.COL_TRIGGER_NAME]);
                        string groupName = Convert.ToString(rs[AdoConstants.COL_TRIGGER_GROUP]);
                        list.Add(new Key(triggerName, groupName));
                    }
                    object[] oArr = list.ToArray();
                    Key[] kArr = new Key[oArr.Length];
                    Array.Copy(oArr, 0, kArr, 0, oArr.Length);
                    return kArr;
                }
            }
        }

        /// <summary> 
        /// Select all of the triggers in a given state.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="state">The state the triggers must be in</param>
        /// <returns> an array of trigger <code>Key</code>s </returns>
        public virtual Key[] SelectTriggersInState(IDbConnection conn, string state)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_TRIGGERS_IN_STATE)))
            {
                AddCommandParameter(cmd, 1, state);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    ArrayList list = new ArrayList();
                    while (rs.Read())
                    {
                        list.Add(new Key(Convert.ToString(rs[1 - 1]), Convert.ToString(rs[2 - 1])));
                    }

                    Key[] sArr = (Key[]) list.ToArray(typeof (Key));
                    return sArr;
                }
            }
        }


        public virtual Key[] SelectMisfiredTriggersInState(IDbConnection conn, string state, long ts)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_MISFIRED_TRIGGERS_IN_STATE)))
            {
                AddCommandParameter(cmd, 1, ts);
                AddCommandParameter(cmd, 2, state);

                using (IDataReader rs = cmd.ExecuteReader())
                {
                    ArrayList list = new ArrayList();
                    while (rs.Read())
                    {
                        string triggerName = Convert.ToString(rs[AdoConstants.COL_TRIGGER_NAME]);
                        string groupName = Convert.ToString(rs[AdoConstants.COL_TRIGGER_GROUP]);
                        list.Add(new Key(triggerName, groupName));
                    }
                    object[] oArr = list.ToArray();
                    Key[] kArr = new Key[oArr.Length];
                    Array.Copy(oArr, 0, kArr, 0, oArr.Length);
                    return kArr;
                }
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
        /// <returns>an array of <code>Key</code> objects</returns>
        public virtual Key[] SelectMisfiredTriggersInGroupInState(IDbConnection conn, string groupName, string state,
                                                                  long ts)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_MISFIRED_TRIGGERS_IN_GROUP_IN_STATE))
                )
            {
                AddCommandParameter(cmd, 1, Convert.ToDecimal(ts));
                AddCommandParameter(cmd, 2, groupName);
                AddCommandParameter(cmd, 3, state);

                using (IDataReader rs = cmd.ExecuteReader())
                {
                    ArrayList list = new ArrayList();
                    while (rs.Read())
                    {
                        string triggerName = Convert.ToString(rs[AdoConstants.COL_TRIGGER_NAME]);
                        list.Add(new Key(triggerName, groupName));
                    }
                    object[] oArr = list.ToArray();
                    Key[] kArr = new Key[oArr.Length];
                    Array.Copy(oArr, 0, kArr, 0, oArr.Length);
                    return kArr;
                }
            }
        }

        /// <summary> <p>
        /// Select all of the triggers for jobs that are requesting recovery. The
        /// returned trigger objects will have unique "recoverXXX" trigger names and
        /// will be in the <code>{@link
        /// org.quartz.Scheduler}.DEFAULT_RECOVERY_GROUP</code>
        /// trigger group.
        /// </p>
        /// 
        /// <p>
        /// In order to preserve the ordering of the triggers, the fire time will be
        /// set from the <code>COL_FIRED_TIME</code> column in the <code>TABLE_FIRED_TRIGGERS</code>
        /// table. The caller is responsible for calling <code>computeFirstFireTime</code>
        /// on each returned trigger. It is also up to the caller to insert the
        /// returned triggers to ensure that they are fired.
        /// </p>
        /// 
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <returns> an array of <code>Trigger</code> objects</returns>
        public virtual Trigger[] SelectTriggersForRecoveringJobs(IDbConnection conn)
        {
            using (
                IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_INSTANCES_RECOVERABLE_FIRED_TRIGGERS)))
            {
                AddCommandParameter(cmd, 1, instanceId);
                AddCommandParameter(cmd, 2, true);

                using (IDataReader rs = cmd.ExecuteReader())
                {
                    // TODO
                    long dumId = (DateTime.Now.Ticks - 621355968000000000)/10000;
                    ArrayList list = new ArrayList();
                    while (rs.Read())
                    {
                        string jobName = Convert.ToString(rs[AdoConstants.COL_JOB_NAME]);
                        string jobGroup = Convert.ToString(rs[AdoConstants.COL_JOB_GROUP]);
                        string trigName = Convert.ToString(rs[AdoConstants.COL_TRIGGER_NAME]);
                        string trigGroup = Convert.ToString(rs[AdoConstants.COL_TRIGGER_GROUP]);
                        long firedTime = Convert.ToInt64(rs[AdoConstants.COL_FIRED_TIME]);
                        DateTime tempAux = new DateTime(firedTime);
                        SimpleTrigger rcvryTrig =
                            new SimpleTrigger("recover_" + instanceId + "_" + Convert.ToString(dumId++),
                                              Scheduler_Fields.DEFAULT_RECOVERY_GROUP, tempAux);
                        rcvryTrig.JobName = jobName;
                        rcvryTrig.JobGroup = jobGroup;
                        rcvryTrig.MisfireInstruction = SimpleTrigger.MISFIRE_INSTRUCTION_FIRE_NOW;

                        JobDataMap jd = SelectTriggerJobDataMap(conn, trigName, trigGroup);
                        jd.Put("QRTZ_FAILED_JOB_ORIG_TRIGGER_NAME", trigName);
                        jd.Put("QRTZ_FAILED_JOB_ORIG_TRIGGER_GROUP", trigGroup);
                        jd.Put("QRTZ_FAILED_JOB_ORIG_TRIGGER_FIRETIME_IN_MILLISECONDS_AS_STRING",
                               Convert.ToString(firedTime));
                        rcvryTrig.JobDataMap = jd;

                        list.Add(rcvryTrig);
                    }
                    object[] oArr = list.ToArray();
                    Trigger[] tArr = new Trigger[oArr.Length];
                    Array.Copy(oArr, 0, tArr, 0, oArr.Length);
                    return tArr;
                }
            }
        }

        /// <summary>
        /// Delete all fired triggers.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <returns>The number of rows deleted.</returns>
        public virtual int DeleteFiredTriggers(IDbConnection conn)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(DELETE_FIRED_TRIGGERS)))
            {
                return cmd.ExecuteNonQuery();
            }
        }


        /// <summary>
        /// Delete all fired triggers of the given instance.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="instanceId">The instance id.</param>
        /// <returns>The number of rows deleted</returns>
        public virtual int DeleteFiredTriggers(IDbConnection conn, string instanceId)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(DELETE_INSTANCES_FIRED_TRIGGERS)))
            {
                AddCommandParameter(cmd, 1, instanceId);
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
        public virtual int InsertJobDetail(IDbConnection conn, JobDetail job)
        {
            byte[] baos = SerializeJobData(job.JobDataMap);

            int insertResult;

            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(INSERT_JOB_DETAIL)))
            {
                AddCommandParameter(cmd, 1, job.Name);
                AddCommandParameter(cmd, 2, job.Group);
                AddCommandParameter(cmd, 3, job.Description);
                AddCommandParameter(cmd, 4, job.JobType.FullName);
                AddCommandParameter(cmd, 5, job.Durable);
                AddCommandParameter(cmd, 6, job.Volatile);
                AddCommandParameter(cmd, 7, job.Stateful);
                AddCommandParameter(cmd, 8, job.RequestsRecovery);
                AddCommandParameter(cmd, 9, baos);

                insertResult = cmd.ExecuteNonQuery();

                if (insertResult > 0)
                {
                    string[] jobListeners = job.JobListenerNames;
                    for (int i = 0; jobListeners != null && i < jobListeners.Length; i++)
                    {
                        InsertJobListener(conn, job, jobListeners[i]);
                    }
                }
            }


            return insertResult;
        }

        /// <summary>
        /// Update the job detail record.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <param name="job">The job to update.</param>
        /// <returns>Number of rows updated.</returns>
        public virtual int UpdateJobDetail(IDbConnection conn, JobDetail job)
        {
            byte[] baos = SerializeJobData(job.JobDataMap);

            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_JOB_DETAIL)))
            {
                AddCommandParameter(cmd, 1, job.Description);
                AddCommandParameter(cmd, 2, job.JobType.FullName);
                AddCommandParameter(cmd, 3, job.Durable);
                AddCommandParameter(cmd, 4, job.Volatile);
                AddCommandParameter(cmd, 5, job.Stateful);
                AddCommandParameter(cmd, 6, job.RequestsRecovery);
                AddCommandParameter(cmd, 7, baos);
                AddCommandParameter(cmd, 8, job.Name);
                AddCommandParameter(cmd, 9, job.Group);

                int insertResult = cmd.ExecuteNonQuery();

                if (insertResult > 0)
                {
                    DeleteJobListeners(conn, job.Name, job.Group);

                    String[] jobListeners = job.JobListenerNames;
                    for (int i = 0; jobListeners != null && i < jobListeners.Length; i++)
                    {
                        InsertJobListener(conn, job, jobListeners[i]);
                    }
                }

                return insertResult;
            }
        }

        /// <summary>
        /// Get the names of all of the triggers associated with the given job.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <param name="jobName">The name of the job.</param>
        /// <param name="groupName">The group containing the job.</param>
        /// <returns>An array of <code>Key</code> objects</returns>
        public virtual Key[] SelectTriggerNamesForJob(IDbConnection conn, string jobName, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_TRIGGERS_FOR_JOB)))
            {
                AddCommandParameter(cmd, 1, jobName);
                AddCommandParameter(cmd, 2, groupName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    ArrayList list = new ArrayList(10);
                    while (rs.Read())
                    {
                        string trigName = Convert.ToString(rs[AdoConstants.COL_TRIGGER_NAME]);
                        string trigGroup = Convert.ToString(rs[AdoConstants.COL_TRIGGER_GROUP]);
                        list.Add(new Key(trigName, trigGroup));
                    }
                    object[] oArr = list.ToArray();
                    Key[] kArr = new Key[oArr.Length];
                    Array.Copy(oArr, 0, kArr, 0, oArr.Length);
                    return kArr;
                }
            }
        }

        /// <summary>
        /// Delete all job listeners for the given job.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <param name="jobName">The name of the job.</param>
        /// <param name="groupName">The group containing the job.</param>
        /// <returns>The number of rows deleted.</returns>
        public virtual int DeleteJobListeners(IDbConnection conn, string jobName, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(DELETE_JOB_LISTENERS)))
            {
                AddCommandParameter(cmd, 1, jobName);
                AddCommandParameter(cmd, 2, groupName);
                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Delete the job detail record for the given job.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="jobName">the name of the job</param>
        /// <param name="groupName">the group containing the job</param>
        /// <returns>the number of rows deleted</returns>
        public virtual int DeleteJobDetail(IDbConnection conn, string jobName, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(DELETE_JOB_DETAIL)))
            {
                logger.Debug("Deleting job: " + groupName + "." + jobName);
                AddCommandParameter(cmd, 1, jobName);
                AddCommandParameter(cmd, 2, groupName);
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
        public virtual bool IsJobStateful(IDbConnection conn, string jobName, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_JOB_STATEFUL)))
            {
                AddCommandParameter(cmd, 1, jobName);
                AddCommandParameter(cmd, 2, groupName);

                object o = cmd.ExecuteScalar();
                if (o != null)
                {
                    return (bool) o;
                }
                else
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Check whether or not the given job exists.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="jobName">the name of the job</param>
        /// <param name="groupName">the group containing the job</param>
        /// <returns>true if the job exists, false otherwise</returns>
        public virtual bool JobExists(IDbConnection conn, string jobName, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_JOB_EXISTENCE)))
            {
                AddCommandParameter(cmd, 1, jobName);
                AddCommandParameter(cmd, 2, groupName);
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
        /// Update the job data map for the given job.
        /// </summary>
        /// <param name="conn">The conn.</param>
        /// <param name="job">the job to update</param>
        /// <returns>the number of rows updated</returns>
        public virtual int UpdateJobData(IDbConnection conn, JobDetail job)
        {
            byte[] baos = SerializeJobData(job.JobDataMap);

            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_JOB_DATA)))
            {
                AddCommandParameter(cmd, 1, baos);
                AddCommandParameter(cmd, 2, job.Name);
                AddCommandParameter(cmd, 3, job.Group);

                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Associate a listener with a job.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <param name="job">The job to associate with the listener.</param>
        /// <param name="listener">The listener to insert.</param>
        /// <returns>The number of rows inserted.</returns>
        public virtual int InsertJobListener(IDbConnection conn, JobDetail job, string listener)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(INSERT_JOB_LISTENER)))
            {
                AddCommandParameter(cmd, 1, job.Name);
                AddCommandParameter(cmd, 2, job.Group);
                AddCommandParameter(cmd, 3, listener);

                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Get all of the listeners for a given job.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <param name="jobName">The job name whose listeners are wanted.</param>
        /// <param name="groupName">The group containing the job.</param>
        /// <returns>Array of <code>String</code> listener names.</returns>
        public virtual string[] SelectJobListeners(IDbConnection conn, string jobName, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_JOB_LISTENERS)))
            {
                ArrayList list = new ArrayList();

                AddCommandParameter(cmd, 1, jobName);
                AddCommandParameter(cmd, 2, groupName);

                using (IDataReader dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        list.Add(Convert.ToString(dr[1 - 1]));
                    }

                    return (string[]) list.ToArray(typeof (string));
                }
            }
        }

        /// <summary>
        /// Select the JobDetail object for a given job name / group name.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <param name="jobName">The job name whose listeners are wanted.</param>
        /// <param name="groupName">The group containing the job.</param>
        /// <returns>The populated JobDetail object.</returns>
        public virtual JobDetail SelectJobDetail(IDbConnection conn, string jobName, string groupName,
                                                 IClassLoadHelper loadHelper)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_JOB_DETAIL)))
            {
                AddCommandParameter(cmd, 1, jobName);
                AddCommandParameter(cmd, 2, groupName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    JobDetail job = null;

                    if (rs.Read())
                    {
                        job = new JobDetail();

                        job.Name = Convert.ToString(rs[AdoConstants.COL_JOB_NAME]);
                        job.Group = Convert.ToString(rs[AdoConstants.COL_JOB_GROUP]);
                        job.Description = Convert.ToString(rs[AdoConstants.COL_DESCRIPTION]);
                        job.JobType = loadHelper.LoadType(Convert.ToString(rs[AdoConstants.COL_JOB_CLASS]));
                        job.Durability = Convert.ToBoolean(rs[AdoConstants.COL_IS_DURABLE]);
                        job.Volatility = Convert.ToBoolean(rs[AdoConstants.COL_IS_VOLATILE]);
                        job.RequestsRecovery = Convert.ToBoolean(rs[AdoConstants.COL_REQUESTS_RECOVERY]);

                        IDictionary map;
                        if (CanUseProperties)
                        {
                            map = GetMapFromProperties(rs, 9);
                        }
                        else
                        {
                            map = (IDictionary) GetObjectFromBlob(rs, 9);
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
            Stream stream = (Stream) GetJobDetailFromBlob(rs, idx);
            if (stream == null)
            {
                return null;
            }
            NameValueCollection properties = new NameValueCollection(ConfigurationSettings.AppSettings);
            map = ConvertFromProperty(properties);
            return map;
        }

        /// <summary>
        /// Select the total number of jobs stored.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <returns>The total number of jobs stored.</returns>
        public virtual int SelectNumJobs(IDbConnection conn)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_NUM_JOBS)))
            {
                return (int) cmd.ExecuteScalar();
            }
        }

        /// <summary>
        /// Select all of the job group names that are stored.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <returns>An array of <code>String</code> group names.</returns>
        public virtual string[] SelectJobGroups(IDbConnection conn)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_JOB_GROUPS)))
            {
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    ArrayList list = new ArrayList();
                    while (rs.Read())
                    {
                        list.Add(Convert.ToString(rs[1 - 1]));
                    }

                    object[] oArr = list.ToArray();
                    string[] sArr = new string[oArr.Length];
                    Array.Copy(oArr, 0, sArr, 0, oArr.Length);
                    return sArr;
                }
            }
        }

        /// <summary>
        /// Select all of the jobs contained in a given group.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <param name="groupName">The group containing the jobs.</param>
        /// <returns>An array of <code>String</code> job names.</returns>
        public virtual String[] SelectJobsInGroup(IDbConnection conn, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_JOBS_IN_GROUP)))
            {
                AddCommandParameter(cmd, 1, groupName);

                using (IDataReader rs = cmd.ExecuteReader())
                {
                    ArrayList list = new ArrayList();
                    while (rs.Read())
                    {
                        list.Add(Convert.ToString(rs[1 - 1]));
                    }

                    object[] oArr = list.ToArray();
                    string[] sArr = new string[oArr.Length];
                    Array.Copy(oArr, 0, sArr, 0, oArr.Length);
                    return sArr;
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
        public virtual int InsertTrigger(IDbConnection conn, Trigger trigger, string state, JobDetail jobDetail)
        {
            byte[] baos = null;
            if (trigger.JobDataMap.Count > 0)
            {
                baos = SerializeJobData(trigger.JobDataMap);
            }

            int insertResult;

            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(INSERT_TRIGGER)))
            {
                AddCommandParameter(cmd, 1, trigger.Name);
                AddCommandParameter(cmd, 2, trigger.Group);
                AddCommandParameter(cmd, 3, trigger.JobName);
                AddCommandParameter(cmd, 4, trigger.JobGroup);
                AddCommandParameter(cmd, 5, trigger.Volatile);
                AddCommandParameter(cmd, 6, trigger.Description);
                AddCommandParameter(cmd, 7,
                                    Decimal.Parse(
                                        Convert.ToString(trigger.GetNextFireTime().Value.Ticks),
                                        NumberStyles.Any));
                long prevFireTime = - 1;
                if (trigger.GetPreviousFireTime().HasValue)
                {
                    prevFireTime = trigger.GetPreviousFireTime().Value.Ticks;
                }
                AddCommandParameter(cmd, 8,
                                    Decimal.Parse(Convert.ToString(prevFireTime), NumberStyles.Any));
                AddCommandParameter(cmd, 9, state);
                if (trigger is SimpleTrigger)
                {
                    AddCommandParameter(cmd, 10, AdoConstants.TTYPE_SIMPLE);
                }
                else if (trigger is CronTrigger)
                {
                    AddCommandParameter(cmd, 10, AdoConstants.TTYPE_CRON);
                }
                else
                {
                    // (trigger instanceof BlobTrigger)
                    AddCommandParameter(cmd, 10, AdoConstants.TTYPE_BLOB);
                }
                AddCommandParameter(cmd, 11,
                                    Decimal.Parse(Convert.ToString(trigger.StartTime.Ticks),
                                                  NumberStyles.Any));
                long endTime = 0;
                if (trigger.EndTime.HasValue)
                {
                    endTime = trigger.EndTime.Value.Ticks;
                }
                AddCommandParameter(cmd, 12, Decimal.Parse(Convert.ToString(endTime), NumberStyles.Any));
                AddCommandParameter(cmd, 13, trigger.CalendarName);
                AddCommandParameter(cmd, 14, trigger.MisfireInstruction);
                if (baos != null)
                {
                    AddCommandParameter(cmd, 15, baos);
                }
                else
                {
                    AddCommandParameter(cmd, 15, null);
                }

                insertResult = cmd.ExecuteNonQuery();
                if (insertResult > 0)
                {
                    String[] trigListeners = trigger.TriggerListenerNames;
                    for (int i = 0; trigListeners != null && i < trigListeners.Length; i++)
                    {
                        InsertTriggerListener(conn, trigger, trigListeners[i]);
                    }
                }

                return insertResult;
            }
        }

        /// <summary>
        /// Insert the simple trigger data.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <param name="trigger">The trigger to insert.</param>
        /// <returns>The number of rows inserted.</returns>
        public virtual int InsertSimpleTrigger(IDbConnection conn, SimpleTrigger trigger)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(INSERT_SIMPLE_TRIGGER)))
            {
                AddCommandParameter(cmd, 1, trigger.Name);
                AddCommandParameter(cmd, 2, trigger.Group);
                AddCommandParameter(cmd, 3, trigger.RepeatCount);
                AddCommandParameter(cmd, 4, trigger.RepeatInterval);
                AddCommandParameter(cmd, 5, trigger.TimesTriggered);

                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Insert the cron trigger data.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="trigger">the trigger to insert</param>
        /// <returns>the number of rows inserted</returns>
        public virtual int InsertCronTrigger(IDbConnection conn, CronTrigger trigger)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(INSERT_CRON_TRIGGER)))
            {
                AddCommandParameter(cmd, 1, trigger.Name);
                AddCommandParameter(cmd, 2, trigger.Group);
                AddCommandParameter(cmd, 3, trigger.CronExpressionString);
                AddCommandParameter(cmd, 4, trigger.TimeZone.StandardName);

                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Insert the blob trigger data.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <param name="trigger">The trigger to insert.</param>
        /// <returns>The number of rows inserted.</returns>
        public virtual int InsertBlobTrigger(IDbConnection conn, Trigger trigger)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(INSERT_BLOB_TRIGGER)))
            {
                // update the blob
                byte[] buf = SerializeObject(trigger);
                AddCommandParameter(cmd, 1, trigger.Name);
                AddCommandParameter(cmd, 2, trigger.Group);
                AddCommandParameter(cmd, 3, buf);

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
        public virtual int UpdateTrigger(IDbConnection conn, Trigger trigger, string state, JobDetail jobDetail)
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
                cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_TRIGGER));
            }
            else
            {
                cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_TRIGGER_SKIP_DATA));
            }

            AddCommandParameter(cmd, 1, trigger.JobName);
            AddCommandParameter(cmd, 2, trigger.JobGroup);
            AddCommandParameter(cmd, 3, trigger.Volatile);
            AddCommandParameter(cmd, 4, trigger.Description);
            long nextFireTime = - 1;

            if (trigger.GetNextFireTime().HasValue)
            {
                nextFireTime = trigger.GetNextFireTime().Value.Ticks;
            }
            AddCommandParameter(cmd, 5, Decimal.Parse(Convert.ToString(nextFireTime), NumberStyles.Any));
            long prevFireTime = - 1;

            if (trigger.GetPreviousFireTime().HasValue)
            {
                prevFireTime = trigger.GetPreviousFireTime().Value.Ticks;
            }
            AddCommandParameter(cmd, 6, Decimal.Parse(Convert.ToString(prevFireTime), NumberStyles.Any));
            AddCommandParameter(cmd, 7, state);
            if (trigger is SimpleTrigger)
            {
                // UpdateSimpleTrigger(conn, (SimpleTrigger)trigger);
                AddCommandParameter(cmd, 8, AdoConstants.TTYPE_SIMPLE);
            }
            else if (trigger is CronTrigger)
            {
                // UpdateCronTrigger(conn, (CronTrigger)trigger);
                AddCommandParameter(cmd, 8, AdoConstants.TTYPE_CRON);
            }
            else
            {
                // UpdateBlobTrigger(conn, trigger);
                AddCommandParameter(cmd, 8, AdoConstants.TTYPE_BLOB);
            }
            //UPGRADE_TODO: Method 'java.util.Date.getTime' was converted to 'DateTime.Ticks' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilDategetTime_3"'
            AddCommandParameter(cmd, 9,
                                Decimal.Parse(Convert.ToString(trigger.StartTime.Ticks),
                                              NumberStyles.Any));
            long endTime = 0;
            if (trigger.EndTime.HasValue)
            {
                endTime = trigger.EndTime.Value.Ticks;
            }
            AddCommandParameter(cmd, 10, Decimal.Parse(Convert.ToString(endTime), NumberStyles.Any));
            AddCommandParameter(cmd, 11, trigger.CalendarName);
            AddCommandParameter(cmd, 12, trigger.MisfireInstruction);
            if (updateJobData)
            {
                if (baos != null)
                {
                    AddCommandParameter(cmd, 13, baos);
                }
                else
                {
                    AddCommandParameter(cmd, 13, null);
                }
                AddCommandParameter(cmd, 14, trigger.Name);
                AddCommandParameter(cmd, 15, trigger.Group);
            }
            else
            {
                AddCommandParameter(cmd, 13, trigger.Name);
                AddCommandParameter(cmd, 14, trigger.Group);
            }

            insertResult = cmd.ExecuteNonQuery();

            if (insertResult > 0)
            {
                DeleteTriggerListeners(conn, trigger.Name, trigger.Group);

                String[] trigListeners = trigger.TriggerListenerNames;
                for (int i = 0; trigListeners != null && i < trigListeners.Length; i++)
                {
                    InsertTriggerListener(conn, trigger, trigListeners[i]);
                }
            }

            return insertResult;
        }

        /// <summary>
        /// Update the simple trigger data.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <param name="trigger">The trigger to insert.</param>
        /// <returns>The number of rows updated.</returns>
        public virtual int UpdateSimpleTrigger(IDbConnection conn, SimpleTrigger trigger)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_SIMPLE_TRIGGER)))
            {
                AddCommandParameter(cmd, 1, trigger.RepeatCount);
                AddCommandParameter(cmd, 2, trigger.RepeatInterval);
                AddCommandParameter(cmd, 3, trigger.TimesTriggered);
                AddCommandParameter(cmd, 4, trigger.Name);
                AddCommandParameter(cmd, 5, trigger.Group);

                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Update the cron trigger data.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <param name="trigger">The trigger to insert.</param>
        /// <returns>The number of rows updated.</returns>
        public virtual int UpdateCronTrigger(IDbConnection conn, CronTrigger trigger)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_CRON_TRIGGER)))
            {
                AddCommandParameter(cmd, 1, trigger.CronExpressionString);
                AddCommandParameter(cmd, 2, trigger.Name);
                AddCommandParameter(cmd, 3, trigger.Group);

                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Update the blob trigger data.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <param name="trigger">The trigger to insert.</param>
        /// <returns>The number of rows updated.</returns>
        public virtual int UpdateBlobTrigger(IDbConnection conn, Trigger trigger)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_BLOB_TRIGGER)))
            {
                // update the blob
                byte[] os = SerializeObject(trigger);

                AddCommandParameter(cmd, 1, os);
                AddCommandParameter(cmd, 2, trigger.Name);
                AddCommandParameter(cmd, 3, trigger.Group);

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
        public virtual bool TriggerExists(IDbConnection conn, string triggerName, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_TRIGGER_EXISTENCE)))
            {
                AddCommandParameter(cmd, 1, triggerName);
                AddCommandParameter(cmd, 2, groupName);

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
        public virtual int UpdateTriggerState(IDbConnection conn, string triggerName, string groupName, string state)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_TRIGGER_STATE)))
            {
                AddCommandParameter(cmd, 1, state);
                AddCommandParameter(cmd, 2, triggerName);
                AddCommandParameter(cmd, 3, groupName);

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
        public virtual int UpdateTriggerStateFromOtherStates(IDbConnection conn, string triggerName, string groupName,
                                                             string newState, string oldState1, string oldState2,
                                                             string oldState3)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_TRIGGER_STATE_FROM_STATES)))
            {
                AddCommandParameter(cmd, 1, newState);
                AddCommandParameter(cmd, 2, triggerName);
                AddCommandParameter(cmd, 3, groupName);
                AddCommandParameter(cmd, 4, oldState1);
                AddCommandParameter(cmd, 5, oldState2);
                AddCommandParameter(cmd, 6, oldState3);

                return cmd.ExecuteNonQuery();
            }
        }


        public virtual int UpdateTriggerStateFromOtherStatesBeforeTime(IDbConnection conn, string newState,
                                                                       string oldState1,
                                                                       string oldState2, long time)
        {
            using (
                IDbCommand cmd =
                    PrepareCommand(conn, ReplaceTablePrefix(UPDATE_TRIGGER_STATE_FROM_OTHER_STATES_BEFORE_TIME)))
            {
                AddCommandParameter(cmd, 1, newState);
                AddCommandParameter(cmd, 2, oldState1);
                AddCommandParameter(cmd, 3, oldState2);
                AddCommandParameter(cmd, 4, time);

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
        public virtual int UpdateTriggerGroupStateFromOtherStates(IDbConnection conn, string groupName, string newState,
                                                                  string oldState1, string oldState2, string oldState3)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_TRIGGER_GROUP_STATE_FROM_STATES)))
            {
                AddCommandParameter(cmd, 1, newState);
                AddCommandParameter(cmd, 2, groupName);
                AddCommandParameter(cmd, 3, oldState1);
                AddCommandParameter(cmd, 4, oldState2);
                AddCommandParameter(cmd, 5, oldState3);

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
        public virtual int UpdateTriggerStateFromOtherState(IDbConnection conn, string triggerName, string groupName,
                                                            string newState, string oldState)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_TRIGGER_STATE_FROM_STATE)))
            {
                AddCommandParameter(cmd, 1, newState);
                AddCommandParameter(cmd, 2, triggerName);
                AddCommandParameter(cmd, 3, groupName);
                AddCommandParameter(cmd, 4, oldState);

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
        public virtual int UpdateTriggerGroupStateFromOtherState(IDbConnection conn, string groupName, string newState,
                                                                 string oldState)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_TRIGGER_GROUP_STATE_FROM_STATE)))
            {
                AddCommandParameter(cmd, 1, newState);
                AddCommandParameter(cmd, 2, groupName);
                AddCommandParameter(cmd, 3, oldState);

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
        public virtual int UpdateTriggerStatesForJob(IDbConnection conn, string jobName, string groupName, string state)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_JOB_TRIGGER_STATES)))
            {
                AddCommandParameter(cmd, 1, state);
                AddCommandParameter(cmd, 2, jobName);
                AddCommandParameter(cmd, 3, groupName);

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
        public virtual int UpdateTriggerStatesForJobFromOtherState(IDbConnection conn, string jobName, string groupName,
                                                                   string state, string oldState)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_JOB_TRIGGER_STATES_FROM_OTHER_STATE))
                )
            {
                AddCommandParameter(cmd, 1, state);
                AddCommandParameter(cmd, 2, jobName);
                AddCommandParameter(cmd, 3, groupName);
                AddCommandParameter(cmd, 4, oldState);

                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Delete all of the listeners associated with a given trigger.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="triggerName">the name of the trigger whose listeners will be deleted</param>
        /// <param name="groupName">the name of the group containing the trigger</param>
        /// <returns>the number of rows deleted</returns>
        public virtual int DeleteTriggerListeners(IDbConnection conn, string triggerName, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(DELETE_TRIGGER_LISTENERS)))
            {
                AddCommandParameter(cmd, 1, triggerName);
                AddCommandParameter(cmd, 2, groupName);
                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Associate a listener with the given trigger.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="trigger">the trigger</param>
        /// <param name="listener">the name of the listener to associate with the trigger</param>
        /// <returns>the number of rows inserted</returns>
        public virtual int InsertTriggerListener(IDbConnection conn, Trigger trigger, string listener)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(INSERT_TRIGGER_LISTENER)))
            {
                AddCommandParameter(cmd, 1, trigger.Name);
                AddCommandParameter(cmd, 2, trigger.Group);
                AddCommandParameter(cmd, 3, listener);

                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Select the listeners associated with a given trigger.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="triggerName">the name of the trigger</param>
        /// <param name="groupName">the group containing the trigger</param>
        /// <returns>
        /// array of <code>String</code> trigger listener names
        /// </returns>
        public virtual String[] SelectTriggerListeners(IDbConnection conn, string triggerName, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_TRIGGER_LISTENERS)))
            {
                AddCommandParameter(cmd, 1, triggerName);
                AddCommandParameter(cmd, 2, groupName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    ArrayList list = new ArrayList();
                    while (rs.Read())
                    {
                        list.Add(Convert.ToString(rs[1 - 1]));
                    }
                    return (string[]) list.ToArray(typeof (string));
                }
            }
        }

        /// <summary>
        /// Delete the simple trigger data for a trigger.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="triggerName">the name of the trigger</param>
        /// <param name="groupName">the group containing the trigger</param>
        /// <returns>the number of rows deleted</returns>
        public virtual int DeleteSimpleTrigger(IDbConnection conn, string triggerName, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(DELETE_SIMPLE_TRIGGER)))
            {
                AddCommandParameter(cmd, 1, triggerName);
                AddCommandParameter(cmd, 2, groupName);

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
        public virtual int DeleteCronTrigger(IDbConnection conn, string triggerName, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(DELETE_CRON_TRIGGER)))
            {
                AddCommandParameter(cmd, 1, triggerName);
                AddCommandParameter(cmd, 2, groupName);

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
        public virtual int DeleteBlobTrigger(IDbConnection conn, string triggerName, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(DELETE_BLOB_TRIGGER)))
            {
                AddCommandParameter(cmd, 1, triggerName);
                AddCommandParameter(cmd, 2, groupName);

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
        public virtual int DeleteTrigger(IDbConnection conn, string triggerName, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(DELETE_TRIGGER)))
            {
                AddCommandParameter(cmd, 1, triggerName);
                AddCommandParameter(cmd, 2, groupName);

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
        public virtual int SelectNumTriggersForJob(IDbConnection conn, string jobName, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_NUM_TRIGGERS_FOR_JOB)))
            {
                AddCommandParameter(cmd, 1, jobName);
                AddCommandParameter(cmd, 2, groupName);

                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        return rs.GetInt32(0);
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
        /// <returns>
        /// the <code>{@link org.quartz.JobDetail}</code> object
        /// associated with the given trigger
        /// </returns>
        public virtual JobDetail SelectJobForTrigger(IDbConnection conn, string triggerName, string groupName,
                                                     IClassLoadHelper loadHelper)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_JOB_FOR_TRIGGER)))
            {
                AddCommandParameter(cmd, 1, triggerName);
                AddCommandParameter(cmd, 2, groupName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        JobDetail job = new JobDetail();
                        job.Name = Convert.ToString(rs[1 - 1]);
                        job.Group = Convert.ToString(rs[2 - 1]);
                        job.Durability = rs.GetBoolean(3 - 1);
                        job.JobType = loadHelper.LoadType(Convert.ToString(rs[4 - 1]));
                        job.RequestsRecovery = rs.GetBoolean(5 - 1);

                        return job;
                    }
                    else
                    {
                        logger.Debug("No job for trigger '" + groupName + "." + triggerName + "'.");
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
        /// an array of <code>(@link org.quartz.Trigger)</code> objects
        /// associated with a given job.
        /// </returns>
        public virtual Trigger[] SelectTriggersForJob(IDbConnection conn, string jobName, string groupName)
        {
            ArrayList trigList = new ArrayList();

            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_TRIGGERS_FOR_JOB)))
            {
                AddCommandParameter(cmd, 1, jobName);
                AddCommandParameter(cmd, 2, groupName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    while (rs.Read())
                    {
                        Trigger t = SelectTrigger(conn,
                                                  rs.GetString(0),
                                                  rs.GetString(1));
                        if (t != null)
                            trigList.Add(t);
                    }
                }
            }


            return (Trigger[]) trigList.ToArray(typeof (Trigger));
        }


        public virtual Trigger[] SelectTriggersForCalendar(IDbConnection conn, string calName)
        {
            ArrayList trigList = new ArrayList();
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_TRIGGERS_FOR_CALENDAR)))
            {
                AddCommandParameter(cmd, 1, calName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    while (rs.Read())
                    {
                        trigList.Add(
                            SelectTrigger(conn, Convert.ToString(rs[AdoConstants.COL_TRIGGER_NAME]),
                                          Convert.ToString(rs[AdoConstants.COL_TRIGGER_GROUP])));
                    }
                }
            }


            return (Trigger[]) trigList.ToArray(typeof (Trigger));
        }


        /// <summary>
        /// Selects the stateful jobs of trigger group.
        /// </summary>
        /// <param name="conn">The database connection.</param>
        /// <param name="groupName">Name of the group.</param>
        /// <returns></returns>
        public virtual IList SelectStatefulJobsOfTriggerGroup(IDbConnection conn, string groupName)
        {
            ArrayList jobList = new ArrayList();
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_STATEFUL_JOBS_OF_TRIGGER_GROUP)))
            {
                AddCommandParameter(cmd, 1, groupName);
                AddCommandParameter(cmd, 2, true);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    while (rs.Read())
                    {
                        jobList.Add(
                            new Key(Convert.ToString(rs[AdoConstants.COL_JOB_NAME]),
                                    Convert.ToString(rs[AdoConstants.COL_JOB_GROUP])));
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
        /// <returns>
        /// the <code>{@link org.quartz.Trigger}</code> object
        /// </returns>
        public virtual Trigger SelectTrigger(IDbConnection conn, string triggerName, string groupName)
        {
            IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_TRIGGER));
            Trigger trigger = null;

            AddCommandParameter(cmd, 1, triggerName);
            AddCommandParameter(cmd, 2, groupName);
            IDataReader rs = cmd.ExecuteReader();


            if (rs.Read())
            {
                string jobName = Convert.ToString(rs[AdoConstants.COL_JOB_NAME]);
                string jobGroup = Convert.ToString(rs[AdoConstants.COL_JOB_GROUP]);
                bool volatility = Convert.ToBoolean(rs[AdoConstants.COL_IS_VOLATILE]);
                string description = Convert.ToString(rs[AdoConstants.COL_DESCRIPTION]);
                long nextFireTime = Convert.ToInt64(rs[AdoConstants.COL_NEXT_FIRE_TIME]);
                long prevFireTime = Convert.ToInt64(rs[AdoConstants.COL_PREV_FIRE_TIME]);
                string triggerType = Convert.ToString(rs[AdoConstants.COL_TRIGGER_TYPE]);
                long startTime = Convert.ToInt64(rs[AdoConstants.COL_START_TIME]);
                long endTime = Convert.ToInt64(rs[AdoConstants.COL_END_TIME]);
                string calendarName = Convert.ToString(rs[AdoConstants.COL_CALENDAR_NAME]);
                int misFireInstr = Convert.ToInt32(rs[AdoConstants.COL_MISFIRE_INSTRUCTION]);

                IDictionary map;
                if (CanUseProperties)
                {
                    map = GetMapFromProperties(rs, 16);
                }
                else
                {
                    map = (IDictionary) GetObjectFromBlob(rs, 16);
                }

                NullableDateTime nft = null;
                if (nextFireTime > 0)
                {
                    nft = new DateTime(nextFireTime);
                }
                NullableDateTime pft = null;
                if (prevFireTime > 0)
                {
                    pft = new DateTime(prevFireTime);
                }
                DateTime startTimeD = new DateTime(startTime);
                NullableDateTime endTimeD = null;
                if (endTime > 0)
                {
                    endTimeD = new DateTime(endTime);
                }

                rs.Close();

                if (triggerType.Equals(AdoConstants.TTYPE_SIMPLE))
                {
                    cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_SIMPLE_TRIGGER));
                    AddCommandParameter(cmd, 1, triggerName);
                    AddCommandParameter(cmd, 2, groupName);
                    rs = cmd.ExecuteReader();

                    if (rs.Read())
                    {
                        int repeatCount = Convert.ToInt32(rs[AdoConstants.COL_REPEAT_COUNT]);
                        long repeatInterval = Convert.ToInt64(rs[AdoConstants.COL_REPEAT_INTERVAL]);
                        int timesTriggered = Convert.ToInt32(rs[AdoConstants.COL_TIMES_TRIGGERED]);

                        //UPGRADE_NOTE: ref keyword was added to struct-type parameters. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1303_3"'
                        SimpleTrigger st =
                            new SimpleTrigger(triggerName, groupName, jobName, jobGroup, startTimeD, endTimeD,
                                              repeatCount,
                                              repeatInterval);
                        st.CalendarName = calendarName;
                        st.MisfireInstruction = misFireInstr;
                        st.TimesTriggered = timesTriggered;
                        st.Volatility = volatility;
                        //UPGRADE_NOTE: ref keyword was added to struct-type parameters. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1303_3"'
                        st.SetNextFireTime(nft);
                        //UPGRADE_NOTE: ref keyword was added to struct-type parameters. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1303_3"'
                        st.SetPreviousFireTime(pft);
                        st.Description = description;
                        if (null != map)
                        {
                            st.JobDataMap = new JobDataMap(map);
                        }
                        trigger = st;
                    }
                }
                else if (triggerType.Equals(AdoConstants.TTYPE_CRON))
                {
                    cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_CRON_TRIGGER));
                    AddCommandParameter(cmd, 1, triggerName);
                    AddCommandParameter(cmd, 2, groupName);
                    rs = cmd.ExecuteReader();

                    if (rs.Read())
                    {
                        string cronExpr = Convert.ToString(rs[AdoConstants.COL_CRON_EXPRESSION]);
                        string timeZoneId = Convert.ToString(rs[AdoConstants.COL_TIME_ZONE_ID]);

                        CronTrigger ct = null;
                        try
                        {
                            TimeZone timeZone = null;
                            if (timeZoneId != null)
                            {
                                // TODO should we do something actually here
                            }
                            ct =
                                new CronTrigger(triggerName, groupName, jobName, jobGroup, startTimeD, endTimeD,
                                                cronExpr, timeZone);
                        }
                        catch (Exception)
                        {
                            // expr must be valid, or it never would have
                            // gotten to the store...
                        }
                        if (null != ct)
                        {
                            ct.CalendarName = calendarName;
                            ct.MisfireInstruction = misFireInstr;
                            ct.Volatility = volatility;
                            ct.SetNextFireTime(nft);
                            ct.SetPreviousFireTime(pft);
                            ct.Description = description;
                            if (null != map)
                            {
                                ct.JobDataMap = new JobDataMap(map);
                            }
                            trigger = ct;
                        }
                    }
                }
                else if (triggerType.Equals(AdoConstants.TTYPE_BLOB))
                {
                    cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_BLOB_TRIGGER));
                    AddCommandParameter(cmd, 1, triggerName);
                    AddCommandParameter(cmd, 2, groupName);
                    rs = cmd.ExecuteReader();

                    if (rs.Read())
                    {
                        trigger = (Trigger) GetObjectFromBlob(rs, 3);
                    }
                }
                else
                {
                    throw new Exception("Class for trigger type '" + triggerType + "' not found.");
                }
            }


            return trigger;
        }

        /// <summary>
        /// Select a trigger's JobDataMap.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="triggerName">the name of the trigger</param>
        /// <param name="groupName">the group containing the trigger</param>
        /// <returns>
        /// the <code>{@link org.quartz.JobDataMap}</code> of the Trigger,
        /// never null, but possibly empty.
        /// </returns>
        public virtual JobDataMap SelectTriggerJobDataMap(IDbConnection conn, string triggerName, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_TRIGGER_DATA)))
            {
                AddCommandParameter(cmd, 1, triggerName);
                AddCommandParameter(cmd, 2, groupName);

                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        IDictionary map;
                        if (CanUseProperties)
                        {
                            map = GetMapFromProperties(rs, 1);
                        }
                        else
                        {
                            map = (IDictionary) GetObjectFromBlob(rs, 1);
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
        /// <returns>
        /// the <code>{@link org.quartz.Trigger}</code> object
        /// </returns>
        public virtual string SelectTriggerState(IDbConnection conn, string triggerName, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_TRIGGER_STATE)))
            {
                string state;

                AddCommandParameter(cmd, 1, triggerName);
                AddCommandParameter(cmd, 2, groupName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        state = Convert.ToString(rs[AdoConstants.COL_TRIGGER_STATE]);
                    }
                    else
                    {
                        state = AdoConstants.STATE_DELETED;
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
        /// a <code>TriggerStatus</code> object, or null
        /// </returns>
        public virtual TriggerStatus SelectTriggerStatus(IDbConnection conn, string triggerName, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_TRIGGER_STATUS)))
            {
                TriggerStatus status = null;

                AddCommandParameter(cmd, 1, triggerName);
                AddCommandParameter(cmd, 2, groupName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        string state = Convert.ToString(rs[AdoConstants.COL_TRIGGER_STATE]);
                        long nextFireTime = Convert.ToInt64(rs[AdoConstants.COL_NEXT_FIRE_TIME]);
                        string jobName = Convert.ToString(rs[AdoConstants.COL_JOB_NAME]);
                        string jobGroup = Convert.ToString(rs[AdoConstants.COL_JOB_GROUP]);

                        NullableDateTime nft = null;
                        if (nextFireTime > 0)
                        {
                            nft = new DateTime(nextFireTime);
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
        public virtual int SelectNumTriggers(IDbConnection conn)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_NUM_TRIGGERS)))
            {
                int count = 0;

                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        count = rs.GetInt32(1 - 1);
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
        /// an array of <code>String</code> group names
        /// </returns>
        public virtual string[] SelectTriggerGroups(IDbConnection conn)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_TRIGGER_GROUPS)))
            {
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    ArrayList list = new ArrayList();
                    while (rs.Read())
                    {
                        list.Add(Convert.ToString(rs[1 - 1]));
                    }

                    return (string[]) list.ToArray(typeof (string));
                }
            }
        }

        /// <summary>
        /// Select all of the triggers contained in a given group.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="groupName">the group containing the triggers</param>
        /// <returns>
        /// an array of <code>String</code> trigger names
        /// </returns>
        public virtual string[] SelectTriggersInGroup(IDbConnection conn, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_TRIGGERS_IN_GROUP)))
            {
                AddCommandParameter(cmd, 1, groupName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    ArrayList list = new ArrayList();
                    while (rs.Read())
                    {
                        list.Add(Convert.ToString(rs[1 - 1]));
                    }

                    return (string[]) list.ToArray(typeof (string));
                }
            }
        }


        public virtual int InsertPausedTriggerGroup(IDbConnection conn, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(INSERT_PAUSED_TRIGGER_GROUP)))
            {
                AddCommandParameter(cmd, 1, groupName);
                int rows = cmd.ExecuteNonQuery();

                return rows;
            }
        }


        public virtual int DeletePausedTriggerGroup(IDbConnection conn, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(DELETE_PAUSED_TRIGGER_GROUP)))
            {
                AddCommandParameter(cmd, 1, groupName);
                int rows = cmd.ExecuteNonQuery();

                return rows;
            }
        }


        /// <summary>
        /// Deletes all paused trigger groups.
        /// </summary>
        /// <param name="conn">The conn.</param>
        /// <returns></returns>
        public virtual int DeleteAllPausedTriggerGroups(IDbConnection conn)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(DELETE_PAUSED_TRIGGER_GROUPS)))
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
        public virtual bool IsTriggerGroupPaused(IDbConnection conn, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_PAUSED_TRIGGER_GROUP)))
            {
                AddCommandParameter(cmd, 1, groupName);
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
        public virtual bool IsExistingTriggerGroup(IDbConnection conn, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_NUM_TRIGGERS_IN_GROUP)))
            {
                AddCommandParameter(cmd, 1, groupName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (!rs.Read())
                    {
                        return false;
                    }

                    return (rs.GetInt32(1 - 1) > 0);
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
        public virtual int InsertCalendar(IDbConnection conn, string calendarName, ICalendar calendar)
        {
            byte[] baos = SerializeObject(calendar);

            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(INSERT_CALENDAR)))
            {
                AddCommandParameter(cmd, 1, calendarName);
                AddCommandParameter(cmd, 2, baos);

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
        public virtual int UpdateCalendar(IDbConnection conn, string calendarName, ICalendar calendar)
        {
            byte[] baos = SerializeObject(calendar);

            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_CALENDAR)))
            {
                AddCommandParameter(cmd, 1, baos);
                AddCommandParameter(cmd, 2, calendarName);

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
        public virtual bool CalendarExists(IDbConnection conn, string calendarName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_CALENDAR_EXISTENCE)))
            {
                AddCommandParameter(cmd, 1, calendarName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
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
        /// Select a calendar.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="calendarName">The name of the calendar.</param>
        /// <returns>the Calendar</returns>
        /// <throws>  ClassNotFoundException </throws>
        /// <throws>  IOException </throws>
        public virtual ICalendar SelectCalendar(IDbConnection conn, string calendarName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_CALENDAR)))
            {
                AddCommandParameter(cmd, 1, calendarName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    ICalendar cal = null;
                    if (rs.Read())
                    {
                        cal = (ICalendar) GetObjectFromBlob(rs, 2);
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
        public virtual bool CalendarIsReferenced(IDbConnection conn, string calendarName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_REFERENCED_CALENDAR)))
            {
                AddCommandParameter(cmd, 1, calendarName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
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
        /// Delete a calendar.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="calendarName">The name of the trigger.</param>
        /// <returns>the number of rows deleted</returns>
        public virtual int DeleteCalendar(IDbConnection conn, string calendarName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(DELETE_CALENDAR)))
            {
                AddCommandParameter(cmd, 1, calendarName);
                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Select the total number of calendars stored.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <returns>the total number of calendars stored</returns>
        public virtual int SelectNumCalendars(IDbConnection conn)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_NUM_CALENDARS)))
            {
                int count = 0;
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        count = rs.GetInt32(1 - 1);
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
        /// an array of <code>String</code> calendar names
        /// </returns>
        public virtual String[] SelectCalendars(IDbConnection conn)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_CALENDARS)))
            {
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    ArrayList list = new ArrayList();
                    while (rs.Read())
                    {
                        list.Add(Convert.ToString(rs[1 - 1]));
                    }
                    return (string[]) list.ToArray(typeof (string));
                }
            }
        }

        //---------------------------------------------------------------------------
        // trigger firing
        //---------------------------------------------------------------------------

        /// <summary>
        /// Select the next time that a trigger will be fired.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <returns>
        /// the next fire time, or 0 if no trigger will be fired
        /// </returns>
        public virtual NullableDateTime SelectNextFireTime(IDbConnection conn)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_NEXT_FIRE_TIME)))
            {
                AddCommandParameter(cmd, 1, AdoConstants.STATE_WAITING);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        return new DateTime(Convert.ToInt64(rs[AdoConstants.ALIAS_COL_NEXT_FIRE_TIME]));
                    }
                    else
                    {
                        return null;
                    }
                }
            }
        }

        /// <summary>
        /// Select the trigger that will be fired at the given fire time.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="fireTime">the time that the trigger will be fired</param>
        /// <returns>
        /// a <code>{@link org.quartz.utils.Key}</code> representing the
        /// trigger that will be fired at the given fire time, or null if no
        /// trigger will be fired at that time
        /// </returns>
        public virtual Key SelectTriggerForFireTime(IDbConnection conn, DateTime fireTime)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_TRIGGER_FOR_FIRE_TIME)))
            {
                AddCommandParameter(cmd, 1, AdoConstants.STATE_WAITING);
                AddCommandParameter(cmd, 2, Decimal.Parse(Convert.ToString(fireTime), NumberStyles.Any));

                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        return
                            new Key(Convert.ToString(rs[AdoConstants.COL_TRIGGER_NAME]),
                                    Convert.ToString(rs[AdoConstants.COL_TRIGGER_GROUP]));
                    }
                    else
                    {
                        return null;
                    }
                }
            }
        }

        /// <summary>
        /// Insert a fired trigger.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="trigger">the trigger</param>
        /// <param name="state">the state that the trigger should be stored in</param>
        /// <param name="job">The job.</param>
        /// <returns>the number of rows inserted</returns>
        public virtual int InsertFiredTrigger(IDbConnection conn, Trigger trigger, string state, JobDetail job)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(INSERT_FIRED_TRIGGER)))
            {
                AddCommandParameter(cmd, 1, trigger.FireInstanceId);
                AddCommandParameter(cmd, 2, trigger.Name);
                AddCommandParameter(cmd, 3, trigger.Group);
                AddCommandParameter(cmd, 4, trigger.Volatile);
                AddCommandParameter(cmd, 5, instanceId);
                AddCommandParameter(cmd, 6,
                                    Decimal.Parse(Convert.ToString(trigger.GetNextFireTime().Value.Ticks),
                                                  NumberStyles.Any));
                AddCommandParameter(cmd, 7, state);
                if (job != null)
                {
                    AddCommandParameter(cmd, 8, trigger.JobName);
                    AddCommandParameter(cmd, 9, trigger.JobGroup);
                    AddCommandParameter(cmd, 10, job.Stateful);
                    AddCommandParameter(cmd, 11, job.RequestsRecovery);
                }
                else
                {
                    AddCommandParameter(cmd, 8, null);
                    AddCommandParameter(cmd, 9, null);
                    AddCommandParameter(cmd, 10, false);
                    AddCommandParameter(cmd, 11, false);
                }

                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Select the states of all fired-trigger records for a given trigger, or
        /// trigger group if trigger name is <code>null</code>.
        /// </summary>
        /// <param name="conn">The DB connection.</param>
        /// <param name="triggerName">Name of the trigger.</param>
        /// <param name="groupName">Name of the group.</param>
        /// <returns>a List of FiredTriggerRecord objects.</returns>
        public virtual IList SelectFiredTriggerRecords(IDbConnection conn, string triggerName, string groupName)
        {
            IDbCommand cmd;

            IList lst = new ArrayList();

            if (triggerName != null)
            {
                cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_FIRED_TRIGGER));
                AddCommandParameter(cmd, 1, triggerName);
                AddCommandParameter(cmd, 2, groupName);
            }
            else
            {
                cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_FIRED_TRIGGER_GROUP));
                AddCommandParameter(cmd, 1, groupName);
            }

            using (IDataReader rs = cmd.ExecuteReader())
            {
                while (rs.Read())
                {
                    FiredTriggerRecord rec = new FiredTriggerRecord();

                    rec.FireInstanceId = Convert.ToString(rs[AdoConstants.COL_ENTRY_ID]);
                    rec.FireInstanceState = Convert.ToString(rs[AdoConstants.COL_ENTRY_STATE]);
                    rec.FireTimestamp = Convert.ToInt64(rs[AdoConstants.COL_FIRED_TIME]);
                    rec.SchedulerInstanceId = Convert.ToString(rs[AdoConstants.COL_INSTANCE_NAME]);
                    rec.TriggerIsVolatile = Convert.ToBoolean(rs[AdoConstants.COL_IS_VOLATILE]);
                    rec.TriggerKey =
                        new Key(Convert.ToString(rs[AdoConstants.COL_TRIGGER_NAME]),
                                Convert.ToString(rs[AdoConstants.COL_TRIGGER_GROUP]));
                    if (!rec.FireInstanceState.Equals(AdoConstants.STATE_ACQUIRED))
                    {
                        rec.JobIsStateful = Convert.ToBoolean(rs[AdoConstants.COL_IS_STATEFUL]);
                        rec.JobRequestsRecovery = Convert.ToBoolean(rs[AdoConstants.COL_REQUESTS_RECOVERY]);
                        rec.JobKey =
                            new Key(Convert.ToString(rs[AdoConstants.COL_JOB_NAME]),
                                    Convert.ToString(rs[AdoConstants.COL_JOB_GROUP]));
                    }
                    lst.Add(rec);
                }
            }
            return lst;
        }

        /// <summary>
        /// Select the states of all fired-trigger records for a given job, or job
        /// group if job name is <code>null</code>.
        /// </summary>
        /// <param name="conn">The DB connection.</param>
        /// <param name="jobName">Name of the job.</param>
        /// <param name="groupName">Name of the group.</param>
        /// <returns>a List of FiredTriggerRecord objects.</returns>
        public virtual IList SelectFiredTriggerRecordsByJob(IDbConnection conn, string jobName, string groupName)
        {
            IList lst = new ArrayList();

            IDbCommand cmd;
            if (jobName != null)
            {
                cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_FIRED_TRIGGERS_OF_JOB));
                AddCommandParameter(cmd, 1, jobName);
                AddCommandParameter(cmd, 2, groupName);
            }
            else
            {
                cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_FIRED_TRIGGERS_OF_JOB_GROUP));
                AddCommandParameter(cmd, 1, groupName);
            }

            using (IDataReader rs = cmd.ExecuteReader())
            {
                while (rs.Read())
                {
                    FiredTriggerRecord rec = new FiredTriggerRecord();

                    rec.FireInstanceId = Convert.ToString(rs[AdoConstants.COL_ENTRY_ID]);
                    rec.FireInstanceState = Convert.ToString(rs[AdoConstants.COL_ENTRY_STATE]);
                    rec.FireTimestamp = Convert.ToInt64(rs[AdoConstants.COL_FIRED_TIME]);
                    rec.SchedulerInstanceId = Convert.ToString(rs[AdoConstants.COL_INSTANCE_NAME]);
                    rec.TriggerIsVolatile = Convert.ToBoolean(rs[AdoConstants.COL_IS_VOLATILE]);
                    rec.TriggerKey =
                        new Key(Convert.ToString(rs[AdoConstants.COL_TRIGGER_NAME]),
                                Convert.ToString(rs[AdoConstants.COL_TRIGGER_GROUP]));
                    if (!rec.FireInstanceState.Equals(AdoConstants.STATE_ACQUIRED))
                    {
                        rec.JobIsStateful = Convert.ToBoolean(rs[AdoConstants.COL_IS_STATEFUL]);
                        rec.JobRequestsRecovery = Convert.ToBoolean(rs[AdoConstants.COL_REQUESTS_RECOVERY]);
                        rec.JobKey =
                            new Key(Convert.ToString(rs[AdoConstants.COL_JOB_NAME]),
                                    Convert.ToString(rs[AdoConstants.COL_JOB_GROUP]));
                    }
                    lst.Add(rec);
                }
            }
            return lst;
        }


        public virtual IList SelectInstancesFiredTriggerRecords(IDbConnection conn, string instanceName)
        {
            IList lst = new ArrayList();

            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_INSTANCES_FIRED_TRIGGERS)))
            {
                AddCommandParameter(cmd, 1, instanceName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    while (rs.Read())
                    {
                        FiredTriggerRecord rec = new FiredTriggerRecord();

                        rec.FireInstanceId = Convert.ToString(rs[AdoConstants.COL_ENTRY_ID]);
                        rec.FireInstanceState = Convert.ToString(rs[AdoConstants.COL_ENTRY_STATE]);
                        rec.FireTimestamp = Convert.ToInt64(rs[AdoConstants.COL_FIRED_TIME]);
                        rec.SchedulerInstanceId = Convert.ToString(rs[AdoConstants.COL_INSTANCE_NAME]);
                        rec.TriggerIsVolatile = Convert.ToBoolean(rs[AdoConstants.COL_IS_VOLATILE]);
                        rec.TriggerKey =
                            new Key(Convert.ToString(rs[AdoConstants.COL_TRIGGER_NAME]),
                                    Convert.ToString(rs[AdoConstants.COL_TRIGGER_GROUP]));
                        if (!rec.FireInstanceState.Equals(AdoConstants.STATE_ACQUIRED))
                        {
                            rec.JobIsStateful = Convert.ToBoolean(rs[AdoConstants.COL_IS_STATEFUL]);
                            rec.JobRequestsRecovery = Convert.ToBoolean(rs[AdoConstants.COL_REQUESTS_RECOVERY]);
                            rec.JobKey =
                                new Key(Convert.ToString(rs[AdoConstants.COL_JOB_NAME]),
                                        Convert.ToString(rs[AdoConstants.COL_JOB_GROUP]));
                        }
                        lst.Add(rec);
                    }
                }

                return lst;
            }
        }

        /// <summary>
        /// Delete a fired trigger.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="entryId">the fired trigger entry to delete</param>
        /// <returns>the number of rows deleted</returns>
        public virtual int DeleteFiredTrigger(IDbConnection conn, string entryId)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(DELETE_FIRED_TRIGGER)))
            {
                AddCommandParameter(cmd, 1, entryId);
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
        public virtual int SelectJobExecutionCount(IDbConnection conn, string jobName, string jobGroup)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_JOB_EXECUTION_COUNT)))
            {
                AddCommandParameter(cmd, 1, jobName);
                AddCommandParameter(cmd, 2, jobGroup);

                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        return rs.GetInt32(1 - 1);
                    }
                    else
                    {
                        return 0;
                    }
                }
            }
        }


        /// <summary>
        /// Delete all volatile fired triggers.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <returns>The number of rows deleted</returns>
        public virtual int DeleteVolatileFiredTriggers(IDbConnection conn)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(DELETE_VOLATILE_FIRED_TRIGGERS)))
            {
                AddCommandParameter(cmd, 1, true);
                return cmd.ExecuteNonQuery();
            }
        }


        /// <summary>
        /// Inserts the state of the scheduler.
        /// </summary>
        /// <param name="conn">The conn.</param>
        /// <param name="instanceId">The instance id.</param>
        /// <param name="checkInTime">The check in time.</param>
        /// <param name="interval">The interval.</param>
        /// <param name="recoverer">The recoverer.</param>
        /// <returns></returns>
        public virtual int InsertSchedulerState(IDbConnection conn, string instanceId, long checkInTime, long interval,
                                                string recoverer)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(INSERT_SCHEDULER_STATE)))
            {
                AddCommandParameter(cmd, 1, instanceId);
                AddCommandParameter(cmd, 2, checkInTime);
                AddCommandParameter(cmd, 3, interval);
                AddCommandParameter(cmd, 4, recoverer);

                return cmd.ExecuteNonQuery();
            }
        }


        /// <summary>
        /// Deletes the state of the scheduler.
        /// </summary>
        /// <param name="conn">The database connection.</param>
        /// <param name="instanceId">The instance id.</param>
        /// <returns></returns>
        public virtual int DeleteSchedulerState(IDbConnection conn, string instanceId)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(DELETE_SCHEDULER_STATE)))
            {
                AddCommandParameter(cmd, 1, instanceId);

                return cmd.ExecuteNonQuery();
            }
        }


        /// <summary>
        /// Updates the state of the scheduler.
        /// </summary>
        /// <param name="conn">The database connection.</param>
        /// <param name="instanceId">The instance id.</param>
        /// <param name="checkInTime">The check in time.</param>
        /// <param name="recoverer">The recoverer.</param>
        /// <returns></returns>
        public virtual int UpdateSchedulerState(IDbConnection conn, string instanceId, long checkInTime,
                                                string recoverer)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_SCHEDULER_STATE)))
            {
                AddCommandParameter(cmd, 1, checkInTime);
                AddCommandParameter(cmd, 2, recoverer);
                AddCommandParameter(cmd, 3, instanceId);

                return cmd.ExecuteNonQuery();
            }
        }


        public virtual IList SelectSchedulerStateRecords(IDbConnection conn, string instanceId)
        {
            IDbCommand cmd;

            ArrayList list = new ArrayList();

            if (instanceId != null)
            {
                cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_SCHEDULER_STATE));
                AddCommandParameter(cmd, 1, instanceId);
            }
            else
            {
                cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_SCHEDULER_STATES));
            }
            using (IDataReader rs = cmd.ExecuteReader())
            {
                while (rs.Read())
                {
                    SchedulerStateRecord rec = new SchedulerStateRecord();

                    rec.SchedulerInstanceId = Convert.ToString(rs[AdoConstants.COL_INSTANCE_NAME]);
                    rec.CheckinTimestamp = Convert.ToInt64(rs[AdoConstants.COL_LAST_CHECKIN_TIME]);
                    rec.CheckinInterval = Convert.ToInt64(rs[AdoConstants.COL_CHECKIN_INTERVAL]);
                    rec.Recoverer = Convert.ToString(rs[AdoConstants.COL_RECOVERER]);

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
        protected internal string ReplaceTablePrefix(String query)
        {
            return Util.ReplaceTablePrefix(query, tablePrefix);
        }

        /// <summary>
        /// Create a serialized <code>java.util.ByteArrayOutputStream</code>
        /// version of an Object.
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
        /// Remove the transient data from and then create a serialized <code>MemoryStream</code>
        /// version of a <code>JobDataMap</code> and returns the underlying bytes.
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
                    GetKeyOfNonSerializableValue(data) +
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
        protected internal virtual IDictionary ConvertFromProperty(NameValueCollection properties)
        {
            IDictionary data = new Hashtable();
            HashSet keys = new HashSet(properties);
            IEnumerator it = keys.GetEnumerator();
            while (it.MoveNext())
            {
                object key = it.Current;
                object val = properties[(string) key];
                data[key] = val;
            }

            return data;
        }

        /// <summary>
        /// Convert the JobDataMap into a list of properties.
        /// </summary>
        protected internal virtual NameValueCollection ConvertToProperty(IDictionary data)
        {
            NameValueCollection properties = new NameValueCollection();
            foreach (DictionaryEntry entry in data)
            {
                object key = entry.Key;
                object val = entry.Value;

                if (!(key is String))
                {
                    throw new IOException("JobDataMap keys/values must be Strings " +
                                          "when the 'useProperties' property is set. " +
                                          " offending Key: " + key);
                }
                if (!(val is String))
                {
                    throw new IOException("JobDataMap values must be Strings " +
                                          "when the 'useProperties' property is set. " +
                                          " Key of offending value: " + key);
                }
                if (val == null)
                {
                    val = "";
                }
                properties[(string) key] = (string) val;
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
            if (data != null)
            {
                MemoryStream ms = new MemoryStream(data);
                BinaryFormatter bf = new BinaryFormatter();
                obj = bf.Deserialize(ms);
            }
            return obj;
        }

        protected static byte[] ReadBytesFromBlob(IDataReader dr, int colIndex)
        {
            if (dr.IsDBNull(colIndex))
            {
                return null;
            }

            int bufferSize = 1024;
            MemoryStream stream = new MemoryStream();

            // can read the data
            byte[] outbyte = new byte[bufferSize];

            // Reset the starting byte for the new BLOB.
            int startIndex;
            startIndex = 0;

            // Read the bytes into outbyte[] and retain the number of bytes returned.

            int retval; // The bytes returned from GetBytes.
            retval = (int) dr.GetBytes(colIndex, startIndex, outbyte, 0, bufferSize);

            // Continue reading and writing while there are bytes beyond the size of the buffer.
            while (retval == bufferSize)
            {
                stream.Write(outbyte, 0, retval);

                // Reposition the start index to the end of the last buffer and fill the buffer.
                startIndex += bufferSize;
                retval = (int)dr.GetBytes(colIndex, startIndex, outbyte, 0, bufferSize);
            }

            // Write the remaining buffer.
            stream.Write(outbyte, 0, retval);

            return stream.GetBuffer();
        }


        public virtual Key[] SelectVolatileTriggers(IDbConnection conn)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_VOLATILE_TRIGGERS)))
            {
                AddCommandParameter(cmd, 1, true);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    ArrayList list = new ArrayList();
                    while (rs.Read())
                    {
                        string triggerName = Convert.ToString(rs[AdoConstants.COL_TRIGGER_NAME]);
                        string groupName = Convert.ToString(rs[AdoConstants.COL_TRIGGER_GROUP]);
                        list.Add(new Key(triggerName, groupName));
                    }
                    object[] oArr = list.ToArray();
                    Key[] kArr = new Key[oArr.Length];
                    Array.Copy(oArr, 0, kArr, 0, oArr.Length);
                    return kArr;
                }
            }
        }


        public virtual Key[] SelectVolatileJobs(IDbConnection conn)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_VOLATILE_JOBS)))
            {
                AddCommandParameter(cmd, 1, true);
                using (IDataReader dr = cmd.ExecuteReader())
                {
                    ArrayList list = new ArrayList();
                    while (dr.Read())
                    {
                        string triggerName = Convert.ToString(dr[AdoConstants.COL_JOB_NAME]);
                        string groupName = Convert.ToString(dr[AdoConstants.COL_JOB_GROUP]);
                        list.Add(new Key(triggerName, groupName));
                    }
                    Object[] oArr = list.ToArray();
                    Key[] kArr = new Key[oArr.Length];
                    Array.Copy(oArr, 0, kArr, 0, oArr.Length);
                    return kArr;
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
        protected internal virtual object GetJobDetailFromBlob(IDataReader rs, int colIndex)
        {
            if (CanUseProperties)
            {
                if (!rs.IsDBNull(colIndex))
                {
                    // TODO

                    return null;
                }
                else
                {
                    return null;
                }
            }

            return GetObjectFromBlob(rs, colIndex);
        }


        /// <summary>
        /// Selects the paused trigger groups.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <returns></returns>
        public virtual ISet SelectPausedTriggerGroups(IDbConnection conn)
        {
            HashSet retValue = new HashSet();


            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_PAUSED_TRIGGER_GROUPS)))
            {
                using (IDataReader dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        string groupName = Convert.ToString(dr[AdoConstants.COL_TRIGGER_GROUP]);
                        retValue.Add(groupName);
                    }
                }
                return retValue;
            }
        }

        protected IDbCommand PrepareCommand(IDbConnection connection, string commandText)
        {
            IDbCommand cmd = dbProvider.CreateCommand();
            cmd.CommandText = commandText;
            cmd.Connection = connection;
            return cmd;
        }

        protected void AddCommandParameter(IDbCommand cmd, int i, object paramValue)
        {
            IDbDataParameter param = cmd.CreateParameter();
            param.Value = paramValue;
            cmd.Parameters.Add(param);
        }
    }


    // EOF
}