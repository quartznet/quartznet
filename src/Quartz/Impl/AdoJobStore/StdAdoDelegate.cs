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
        protected ILog logger = null;
        protected string tablePrefix = DEFAULT_TABLE_PREFIX;
        protected string instanceId;
        protected bool useProperties;
        protected IDbProvider dbProvider;
        protected AdoUtil adoUtil;

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
        public virtual int UpdateTriggerStatesFromOtherStates(ConnectionAndTransactionHolder conn, string newState,
                                                              string oldState1,
                                                              string oldState2)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_TRIGGER_STATES_FROM_OTHER_STATES)))
            {
                AddCommandParameter(cmd, 1, "newState", newState);
                AddCommandParameter(cmd, 2, "oldState1", oldState1);
                AddCommandParameter(cmd, 3, "oldState2", oldState2);
                return cmd.ExecuteNonQuery();
            }
        }


        /// <summary>
        /// Get the names of all of the triggers that have misfired.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="ts">The ts.</param>
        /// <returns>an array of <code>Key</code> objects</returns>
        public virtual Key[] SelectMisfiredTriggers(ConnectionAndTransactionHolder conn, long ts)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_MISFIRED_TRIGGERS)))
            {
                AddCommandParameter(cmd, 1, "timestamp", ts);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    ArrayList list = new ArrayList();
                    while (rs.Read())
                    {
                        string triggerName = GetString(rs[COL_TRIGGER_NAME]);
                        string groupName = GetString(rs[COL_TRIGGER_GROUP]);
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
        public virtual Key[] SelectTriggersInState(ConnectionAndTransactionHolder conn, string state)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_TRIGGERS_IN_STATE)))
            {
                AddCommandParameter(cmd, 1, "state", state);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    ArrayList list = new ArrayList();
                    while (rs.Read())
                    {
                        list.Add(new Key(GetString(rs[0]), GetString(rs[1])));
                    }

                    Key[] sArr = (Key[]) list.ToArray(typeof (Key));
                    return sArr;
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
        /// <returns>An array of <code>Key</code> objects</returns>
        public virtual Key[] SelectMisfiredTriggersInState(ConnectionAndTransactionHolder conn, string state, long ts)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_MISFIRED_TRIGGERS_IN_STATE)))
            {
                AddCommandParameter(cmd, 1, "timestamp", ts);
                AddCommandParameter(cmd, 2, "state", state);

                using (IDataReader rs = cmd.ExecuteReader())
                {
                    ArrayList list = new ArrayList();
                    while (rs.Read())
                    {
                        string triggerName = GetString(rs[COL_TRIGGER_NAME]);
                        string groupName = GetString(rs[COL_TRIGGER_GROUP]);
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
        /// Get the names of all of the triggers in the given states that have
        /// misfired - according to the given timestamp.  No more than count will
        /// be returned.
        /// </summary>
        /// <param name="conn">The conn.</param>
        /// <param name="state1">The state1.</param>
        /// <param name="state2">The state2.</param>
        /// <param name="ts">The ts.</param>
        /// <param name="count">The most misfired triggers to return, negative for all</param>
        /// <param name="resultList">
        /// Output parameter.  A List of <see cref="Key" /> objects.  Must not be null
        /// </param>
        /// <returns>Whether there are more misfired triggers left to find beyond the given count.</returns>
        public virtual bool SelectMisfiredTriggersInStates(ConnectionAndTransactionHolder conn, string state1, string state2, DateTime ts, int count, IList resultList)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_MISFIRED_TRIGGERS_IN_STATES)))
            {
                AddCommandParameter(cmd, 1, "nextFireTime", Convert.ToDecimal(ts.Ticks));
                AddCommandParameter(cmd, 2, "state1", state1);
                AddCommandParameter(cmd, 3, "state2", state2);

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
                            string triggerName = GetString(rs[COL_TRIGGER_NAME]);
                            string groupName = GetString(rs[COL_TRIGGER_GROUP]);
                            resultList.Add(new Key(triggerName, groupName));
                        }
                    }
                    return hasReachedLimit;
                }
            }
        }

        /// <summary>
        /// Get the number of triggers in the given states that have
        /// misfired - according to the given timestamp.
        /// </summary>
        /// <param name="conn"></param>
        /// <param name="state1"></param>
        /// <param name="state2"></param>
        /// <param name="ts"></param>
        /// <returns></returns>
        public int CountMisfiredTriggersInStates(ConnectionAndTransactionHolder conn, string state1, string state2, DateTime ts)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(COUNT_MISFIRED_TRIGGERS_IN_STATES)))
            {
                AddCommandParameter(cmd, 1, "nextFireTime", Convert.ToDecimal(ts.Ticks));
                AddCommandParameter(cmd, 2, "state1", state1);
                AddCommandParameter(cmd, 3, "state2", state2);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        return Convert.ToInt32(rs.GetValue(0));
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
        /// <returns>an array of <code>Key</code> objects</returns>
        public virtual Key[] SelectMisfiredTriggersInGroupInState(ConnectionAndTransactionHolder conn, string groupName,
                                                                  string state,
                                                                  long ts)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_MISFIRED_TRIGGERS_IN_GROUP_IN_STATE))
                )
            {
                AddCommandParameter(cmd, 1, "timestamp", Convert.ToDecimal(ts));
                AddCommandParameter(cmd, 2, "triggerGroup", groupName);
                AddCommandParameter(cmd, 3, "state", state);

                using (IDataReader rs = cmd.ExecuteReader())
                {
                    ArrayList list = new ArrayList();
                    while (rs.Read())
                    {
                        string triggerName = GetString(rs[COL_TRIGGER_NAME]);
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
        public virtual Trigger[] SelectTriggersForRecoveringJobs(ConnectionAndTransactionHolder conn)
        {
            ArrayList list = new ArrayList();

            using (
                IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_INSTANCES_RECOVERABLE_FIRED_TRIGGERS)))
            {
                AddCommandParameter(cmd, 1, "instanceName", instanceId);
                AddCommandParameter(cmd, 2, "requestsRecovery", GetDbBooleanValue(true));

                using (IDataReader rs = cmd.ExecuteReader())
                {
                    long dumId = DateTime.Now.Ticks;
                    
                    while (rs.Read())
                    {
                        string jobName = GetString(rs[COL_JOB_NAME]);
                        string jobGroup = GetString(rs[COL_JOB_GROUP]);
                        // string trigName = GetString(rs[COL_TRIGGER_NAME]);
                        // string trigGroup = GetString(rs[COL_TRIGGER_GROUP]);
                        long firedTimeInTicks = Convert.ToInt64(rs[COL_FIRED_TIME]);
                        int priority = Convert.ToInt32(rs[COL_PRIORITY]);
                        DateTime firedTime = new DateTime(firedTimeInTicks);
                        SimpleTrigger rcvryTrig =
                            new SimpleTrigger("recover_" + instanceId + "_" + Convert.ToString(dumId++),
                                              SchedulerConstants.DEFAULT_RECOVERY_GROUP, firedTime);
                        rcvryTrig.JobName = jobName;
                        rcvryTrig.JobGroup = jobGroup;
                        rcvryTrig.Priority = priority;
                        rcvryTrig.MisfireInstruction = SimpleTrigger.MISFIRE_INSTRUCTION_FIRE_NOW;

                        list.Add(rcvryTrig);
                    }
                }
            }

            // read JobDataMaps with different reader..
            foreach (SimpleTrigger trigger in list)
            {
                JobDataMap jd = SelectTriggerJobDataMap(conn, trigger.Name, trigger.Group);
                jd.Put(SchedulerConstants.FAILED_JOB_ORIGINAL_TRIGGER_NAME, trigger.Name);
                jd.Put(SchedulerConstants.FAILED_JOB_ORIGINAL_TRIGGER_GROUP, trigger.Group);
                jd.Put(SchedulerConstants.FAILED_JOB_ORIGINAL_TRIGGER_FIRETIME_IN_MILLISECONDS,
                       Convert.ToString(trigger.StartTime));

                trigger.JobDataMap = jd;
            }

            object[] oArr = list.ToArray();
            Trigger[] tArr = new Trigger[oArr.Length];
            Array.Copy(oArr, 0, tArr, 0, oArr.Length);
            return tArr;
               
           
        }

        /// <summary>
        /// Delete all fired triggers.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <returns>The number of rows deleted.</returns>
        public virtual int DeleteFiredTriggers(ConnectionAndTransactionHolder conn)
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
        /// <param name="instanceName">The instance id.</param>
        /// <returns>The number of rows deleted</returns>
        public virtual int DeleteFiredTriggers(ConnectionAndTransactionHolder conn, string instanceName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(DELETE_INSTANCES_FIRED_TRIGGERS)))
            {
                AddCommandParameter(cmd, 1, "instanceName", instanceName);
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

            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(INSERT_JOB_DETAIL)))
            {
                AddCommandParameter(cmd, 1, "jobName", job.Name);
                AddCommandParameter(cmd, 2, "jobGroup", job.Group);
                AddCommandParameter(cmd, 3, "jobDescription", job.Description);
                AddCommandParameter(cmd, 4, "jobType", GetStorableJobTypeName(job.JobType));
                AddCommandParameter(cmd, 5, "jobDurable", GetDbBooleanValue(job.Durable));
                AddCommandParameter(cmd, 6, "jobVolatile", GetDbBooleanValue(job.Volatile));
                AddCommandParameter(cmd, 7, "jobStateful", GetDbBooleanValue(job.Stateful));
                AddCommandParameter(cmd, 8, "jobRequestsRecovery", GetDbBooleanValue(job.RequestsRecovery));
                AddCommandParameter(cmd, 9, "jobDataMap", baos, dbProvider.Metadata.DbBinaryType);

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
        /// Gets the db presentation for boolean value. Subclasses can overwrite this behaviour.
        /// </summary>
        /// <param name="booleanValue">Value to map to database.</param>
        /// <returns></returns>
        protected virtual object GetDbBooleanValue(bool booleanValue)
        {
            return booleanValue;
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

            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_JOB_DETAIL)))
            {
                AddCommandParameter(cmd, 1, "jobDescription", job.Description);
                AddCommandParameter(cmd, 2, "jobType", GetStorableJobTypeName(job.JobType));
                AddCommandParameter(cmd, 3, "jobDurable", GetDbBooleanValue(job.Durable));
                AddCommandParameter(cmd, 4, "jobVolatile", GetDbBooleanValue(job.Volatile));
                AddCommandParameter(cmd, 5, "jobStateful", GetDbBooleanValue(job.Stateful));
                AddCommandParameter(cmd, 6, "jobRequestsRecovery", job.RequestsRecovery);
                AddCommandParameter(cmd, 7, "jobDataMap", baos, dbProvider.Metadata.DbBinaryType);
                AddCommandParameter(cmd, 8, "jobName", job.Name);
                AddCommandParameter(cmd, 9, "jobGroup", job.Group);

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
        public virtual Key[] SelectTriggerNamesForJob(ConnectionAndTransactionHolder conn, string jobName,
                                                      string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_TRIGGERS_FOR_JOB)))
            {
                AddCommandParameter(cmd, 1, "jobName", jobName);
                AddCommandParameter(cmd, 2, "jobGroup", groupName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    ArrayList list = new ArrayList(10);
                    while (rs.Read())
                    {
                        string trigName = GetString(rs[COL_TRIGGER_NAME]);
                        string trigGroup = GetString(rs[COL_TRIGGER_GROUP]);
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
        public virtual int DeleteJobListeners(ConnectionAndTransactionHolder conn, string jobName, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(DELETE_JOB_LISTENERS)))
            {
                AddCommandParameter(cmd, 1, "jobName", jobName);
                AddCommandParameter(cmd, 2, "jobGroup", groupName);
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
        public virtual int DeleteJobDetail(ConnectionAndTransactionHolder conn, string jobName, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(DELETE_JOB_DETAIL)))
            {
                if (logger.IsDebugEnabled)
                {
                    logger.Debug("Deleting job: " + groupName + "." + jobName);
                }
                AddCommandParameter(cmd, 1, "jobName", jobName);
                AddCommandParameter(cmd, 2, "jobGroup", groupName);
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
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_JOB_STATEFUL)))
            {
                AddCommandParameter(cmd, 1, "jobName", jobName);
                AddCommandParameter(cmd, 2, "jobGroup", groupName);

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
        public virtual bool JobExists(ConnectionAndTransactionHolder conn, string jobName, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_JOB_EXISTENCE)))
            {
                AddCommandParameter(cmd, 0, "jobName", jobName);
                AddCommandParameter(cmd, 1, "jobGroup", groupName);
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
        public virtual int UpdateJobData(ConnectionAndTransactionHolder conn, JobDetail job)
        {
            byte[] baos = SerializeJobData(job.JobDataMap);

            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_JOB_DATA)))
            {
                AddCommandParameter(cmd, 1, "jobDataMap", baos, dbProvider.Metadata.DbBinaryType);
                AddCommandParameter(cmd, 2, "jobName", job.Name);
                AddCommandParameter(cmd, 3, "jobGroup", job.Group);

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
        public virtual int InsertJobListener(ConnectionAndTransactionHolder conn, JobDetail job, string listener)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(INSERT_JOB_LISTENER)))
            {
                AddCommandParameter(cmd, 1, "jobName", job.Name);
                AddCommandParameter(cmd, 2, "jobGroup", job.Group);
                AddCommandParameter(cmd, 3, "listener", listener);

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
        public virtual string[] SelectJobListeners(ConnectionAndTransactionHolder conn, string jobName, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_JOB_LISTENERS)))
            {
                ArrayList list = new ArrayList();

                AddCommandParameter(cmd, 1, "jobName", jobName);
                AddCommandParameter(cmd, 2, "jobGroup", groupName);

                using (IDataReader dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        list.Add(dr[0]);
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
        /// <param name="loadHelper">The load helper.</param>
        /// <returns>The populated JobDetail object.</returns>
        public virtual JobDetail SelectJobDetail(ConnectionAndTransactionHolder conn, string jobName, string groupName,
                                                 IClassLoadHelper loadHelper)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_JOB_DETAIL)))
            {
                AddCommandParameter(cmd, 1, "jobName", jobName);
                AddCommandParameter(cmd, 2, "jobGroup", groupName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    JobDetail job = null;

                    if (rs.Read())
                    {
                        job = new JobDetail();

                        job.Name = GetString(rs[COL_JOB_NAME]);
                        job.Group = GetString(rs[COL_JOB_GROUP]);
                        job.Description = GetString(rs[COL_DESCRIPTION]);
                        job.JobType = loadHelper.LoadType(GetString(rs[COL_JOB_CLASS]));
                        job.Durable = GetBoolean(rs[COL_IS_DURABLE]);
                        job.Volatile = GetBoolean(rs[COL_IS_VOLATILE]);
                        job.RequestsRecovery = GetBoolean(rs[COL_REQUESTS_RECOVERY]);

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
            Stream stream = (Stream) GetJobDetailFromBlob(rs, idx);
            if (stream == null)
            {
                return null;
            }
#if NET_20
            NameValueCollection properties = new NameValueCollection(ConfigurationManager.AppSettings);
#else
            NameValueCollection properties = new NameValueCollection(ConfigurationSettings.AppSettings);
#endif
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
        public virtual string[] SelectJobGroups(ConnectionAndTransactionHolder conn)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_JOB_GROUPS)))
            {
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    ArrayList list = new ArrayList();
                    while (rs.Read())
                    {
                        list.Add(rs[0]);
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
        public virtual String[] SelectJobsInGroup(ConnectionAndTransactionHolder conn, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_JOBS_IN_GROUP)))
            {
                AddCommandParameter(cmd, 1, "jobGroup", groupName);

                using (IDataReader rs = cmd.ExecuteReader())
                {
                    ArrayList list = new ArrayList();
                    while (rs.Read())
                    {
                        list.Add(GetString(rs[0]));
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
        public virtual int InsertTrigger(ConnectionAndTransactionHolder conn, Trigger trigger, string state,
                                         JobDetail jobDetail)
        {
            byte[] baos = null;
            if (trigger.JobDataMap.Count > 0)
            {
                baos = SerializeJobData(trigger.JobDataMap);
            }

            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(INSERT_TRIGGER)))
            {
                AddCommandParameter(cmd, 1, "triggerName", trigger.Name);
                AddCommandParameter(cmd, 2, "triggerGroup", trigger.Group);
                AddCommandParameter(cmd, 3, "triggerJobName", trigger.JobName);
                AddCommandParameter(cmd, 4, "triggerJobGroup", trigger.JobGroup);
                AddCommandParameter(cmd, 5, "triggerVolatile", GetDbBooleanValue(trigger.Volatile));
                AddCommandParameter(cmd, 6, "triggerDescription", trigger.Description);
                AddCommandParameter(cmd, 7, "triggerNextFireTime",
                                    Convert.ToDecimal(trigger.GetNextFireTime().Value.Ticks));
                long prevFireTime = - 1;
                if (trigger.GetPreviousFireTime().HasValue)
                {
                    prevFireTime = trigger.GetPreviousFireTime().Value.Ticks;
                }
                AddCommandParameter(cmd, 8, "triggerPreviousFireTime", Convert.ToDecimal(prevFireTime));
                AddCommandParameter(cmd, 9, "triggerState", state);
                string paramName = "triggerType";
                if (trigger.GetType() == typeof (SimpleTrigger))
                {
                    AddCommandParameter(cmd, 10, paramName, TTYPE_SIMPLE);
                }
                else if (trigger.GetType() == typeof (CronTrigger))
                {
                    AddCommandParameter(cmd, 10, paramName, TTYPE_CRON);
                }
                else
                {
                    // (trigger instanceof BlobTrigger)
                    AddCommandParameter(cmd, 10, paramName, TTYPE_BLOB);
                }
                AddCommandParameter(cmd, 11, "triggerStartTime", Convert.ToDecimal(trigger.StartTime.Ticks));
                long endTime = 0;
                if (trigger.EndTime.HasValue)
                {
                    endTime = trigger.EndTime.Value.Ticks;
                }
                AddCommandParameter(cmd, 12, "triggerEndTime", Convert.ToDecimal(endTime));
                AddCommandParameter(cmd, 13, "triggerCalendarName", trigger.CalendarName);
                AddCommandParameter(cmd, 14, "triggerMisfireInstruction", trigger.MisfireInstruction);

                paramName = "triggerJobJobDataMap";
                if (baos != null)
                {
                    AddCommandParameter(cmd, 15, paramName, baos, dbProvider.Metadata.DbBinaryType);
                }
                else
                {
                    AddCommandParameter(cmd, 15, paramName, null, dbProvider.Metadata.DbBinaryType);
                }
                AddCommandParameter(cmd, 16, "triggerPriority", trigger.Priority);

                int insertResult = cmd.ExecuteNonQuery();
                if (insertResult > 0)
                {
                    string[] trigListeners = trigger.TriggerListenerNames;
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
        public virtual int InsertSimpleTrigger(ConnectionAndTransactionHolder conn, SimpleTrigger trigger)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(INSERT_SIMPLE_TRIGGER)))
            {
                AddCommandParameter(cmd, 1, "triggerName", trigger.Name);
                AddCommandParameter(cmd, 2, "triggerGroup", trigger.Group);
                AddCommandParameter(cmd, 3, "triggerRepeatCount", trigger.RepeatCount);
                AddCommandParameter(cmd, 4, "triggerRepeatInterval", trigger.RepeatInterval);
                AddCommandParameter(cmd, 5, "triggerTimesTriggered", trigger.TimesTriggered);

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
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(INSERT_CRON_TRIGGER)))
            {
                AddCommandParameter(cmd, 1, "triggerName", trigger.Name);
                AddCommandParameter(cmd, 2, "triggerGroup", trigger.Group);
                AddCommandParameter(cmd, 3, "triggerCronExpression", trigger.CronExpressionString);
                AddCommandParameter(cmd, 4, "triggerTimeZone", trigger.TimeZone.StandardName);

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
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(INSERT_BLOB_TRIGGER)))
            {
                // update the blob
                byte[] buf = SerializeObject(trigger);
                AddCommandParameter(cmd, 1, "triggerName", trigger.Name);
                AddCommandParameter(cmd, 2, "triggerGroup", trigger.Group);
                AddCommandParameter(cmd, 3, "blob", buf, dbProvider.Metadata.DbBinaryType);

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
                cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_TRIGGER));
            }
            else
            {
                cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_TRIGGER_SKIP_DATA));
            }

            AddCommandParameter(cmd, 1, "triggerJobName", trigger.JobName);
            AddCommandParameter(cmd, 2, "triggerJobGroup", trigger.JobGroup);
            AddCommandParameter(cmd, 3, "triggerVolatile", GetDbBooleanValue(trigger.Volatile));
            AddCommandParameter(cmd, 4, "triggerDescription", trigger.Description);
            long nextFireTime = - 1;

            if (trigger.GetNextFireTime().HasValue)
            {
                nextFireTime = trigger.GetNextFireTime().Value.Ticks;
            }
            AddCommandParameter(cmd, 5, "triggerNextFireTime", Convert.ToDecimal(nextFireTime));
            long prevFireTime = - 1;

            if (trigger.GetPreviousFireTime().HasValue)
            {
                prevFireTime = trigger.GetPreviousFireTime().Value.Ticks;
            }
            AddCommandParameter(cmd, 6, "triggerPreviousFireTime", Convert.ToDecimal(prevFireTime));
            AddCommandParameter(cmd, 7, "triggerState", state);
            string paramName = "triggerType";
            if (trigger.GetType() == typeof (SimpleTrigger))
            {
                // UpdateSimpleTrigger(conn, (SimpleTrigger)trigger);
                AddCommandParameter(cmd, 8, paramName, TTYPE_SIMPLE);
            }
            else if (trigger.GetType() == typeof (CronTrigger))
            {
                // UpdateCronTrigger(conn, (CronTrigger)trigger);
                AddCommandParameter(cmd, 8, paramName, TTYPE_CRON);
            }
            else
            {
                // UpdateBlobTrigger(conn, trigger);
                AddCommandParameter(cmd, 8, paramName, TTYPE_BLOB);
            }

            AddCommandParameter(cmd, 9, "triggerStartTime", Convert.ToDecimal(trigger.StartTime.Ticks));
            long endTime = 0;
            if (trigger.EndTime.HasValue)
            {
                endTime = trigger.EndTime.Value.Ticks;
            }
            AddCommandParameter(cmd, 10, "triggerEndTime", Convert.ToDecimal(endTime));
            AddCommandParameter(cmd, 11, "triggerCalendarName", trigger.CalendarName);
            AddCommandParameter(cmd, 12, "triggerMisfireInstruction", trigger.MisfireInstruction);
            AddCommandParameter(cmd, 13, "triggerPriority", trigger.Priority);
            paramName = "triggerJobJobDataMap";
            if (updateJobData)
            {
                if (baos != null)
                {
                    AddCommandParameter(cmd, 14, paramName, baos, dbProvider.Metadata.DbBinaryType);
                }
                else
                {
                    AddCommandParameter(cmd, 14, paramName, null, dbProvider.Metadata.DbBinaryType);
                }
                AddCommandParameter(cmd, 15, "triggerName", trigger.Name);
                AddCommandParameter(cmd, 16, "triggerGroup", trigger.Group);
            }
            else
            {
                AddCommandParameter(cmd, 14, "triggerName", trigger.Name);
                AddCommandParameter(cmd, 15, "triggerGroup", trigger.Group);
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
        public virtual int UpdateSimpleTrigger(ConnectionAndTransactionHolder conn, SimpleTrigger trigger)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_SIMPLE_TRIGGER)))
            {
                AddCommandParameter(cmd, 1, "triggerRepeatCount", trigger.RepeatCount);
                AddCommandParameter(cmd, 2, "triggerRepeatInterval", trigger.RepeatInterval);
                AddCommandParameter(cmd, 3, "triggerTimesTriggered", trigger.TimesTriggered);
                AddCommandParameter(cmd, 4, "triggerName", trigger.Name);
                AddCommandParameter(cmd, 5, "triggerGroup", trigger.Group);

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
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_CRON_TRIGGER)))
            {
                AddCommandParameter(cmd, 1, "triggerCronExpression", trigger.CronExpressionString);
                AddCommandParameter(cmd, 2, "triggerName", trigger.Name);
                AddCommandParameter(cmd, 3, "triggerGroup", trigger.Group);

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
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_BLOB_TRIGGER)))
            {
                // update the blob
                byte[] os = SerializeObject(trigger);

                AddCommandParameter(cmd, 1, "blob", os, dbProvider.Metadata.DbBinaryType);
                AddCommandParameter(cmd, 2, "triggerName", trigger.Name);
                AddCommandParameter(cmd, 3, "triggerGroup", trigger.Group);

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
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_TRIGGER_EXISTENCE)))
            {
                AddCommandParameter(cmd, 1, "triggerName", triggerName);
                AddCommandParameter(cmd, 2, "triggerGroup", groupName);

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
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_TRIGGER_STATE)))
            {
                AddCommandParameter(cmd, 1, "state", state);
                AddCommandParameter(cmd, 2, "triggerName", triggerName);
                AddCommandParameter(cmd, 3, "triggerGroup", groupName);

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
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_TRIGGER_STATE_FROM_STATES)))
            {
                AddCommandParameter(cmd, 1, "newState", newState);
                AddCommandParameter(cmd, 2, "triggerName", triggerName);
                AddCommandParameter(cmd, 3, "triggerGroup", groupName);
                AddCommandParameter(cmd, 4, "oldState1", oldState1);
                AddCommandParameter(cmd, 5, "oldState2", oldState2);
                AddCommandParameter(cmd, 6, "oldState3", oldState3);

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
                    PrepareCommand(conn, ReplaceTablePrefix(UPDATE_TRIGGER_STATE_FROM_OTHER_STATES_BEFORE_TIME)))
            {
                AddCommandParameter(cmd, 1, "newState", newState);
                AddCommandParameter(cmd, 2, "oldState1", oldState1);
                AddCommandParameter(cmd, 3, "oldState2", oldState2);
                AddCommandParameter(cmd, 4, "time", time);

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
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_TRIGGER_GROUP_STATE_FROM_STATES)))
            {
                AddCommandParameter(cmd, 1, "newState", newState);
                AddCommandParameter(cmd, 2, "groupName", groupName);
                AddCommandParameter(cmd, 3, "oldState1", oldState1);
                AddCommandParameter(cmd, 4, "oldState2", oldState2);
                AddCommandParameter(cmd, 5, "oldState3", oldState3);

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
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_TRIGGER_STATE_FROM_STATE)))
            {
                AddCommandParameter(cmd, 1, "newState", newState);
                AddCommandParameter(cmd, 2, "triggerName", triggerName);
                AddCommandParameter(cmd, 3, "triggerGroup", groupName);
                AddCommandParameter(cmd, 4, "oldState", oldState);

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
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_TRIGGER_GROUP_STATE_FROM_STATE)))
            {
                AddCommandParameter(cmd, 1, "newState", newState);
                AddCommandParameter(cmd, 2, "triggerGroup", groupName);
                AddCommandParameter(cmd, 3, "oldState", oldState);

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
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_JOB_TRIGGER_STATES)))
            {
                AddCommandParameter(cmd, 1, "state", state);
                AddCommandParameter(cmd, 2, "jobName", jobName);
                AddCommandParameter(cmd, 3, "jobGroup", groupName);

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
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_JOB_TRIGGER_STATES_FROM_OTHER_STATE))
                )
            {
                AddCommandParameter(cmd, 1, "state", state);
                AddCommandParameter(cmd, 2, "jobName", jobName);
                AddCommandParameter(cmd, 3, "jobGroup", groupName);
                AddCommandParameter(cmd, 4, "oldState", oldState);

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
        public virtual int DeleteTriggerListeners(ConnectionAndTransactionHolder conn, string triggerName,
                                                  string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(DELETE_TRIGGER_LISTENERS)))
            {
                AddCommandParameter(cmd, 1, "triggerName", triggerName);
                AddCommandParameter(cmd, 2, "triggerGroup", groupName);
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
        public virtual int InsertTriggerListener(ConnectionAndTransactionHolder conn, Trigger trigger, string listener)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(INSERT_TRIGGER_LISTENER)))
            {
                AddCommandParameter(cmd, 1, "triggerName", trigger.Name);
                AddCommandParameter(cmd, 2, "triggerGroup", trigger.Group);
                AddCommandParameter(cmd, 3, "listener", listener);

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
        public virtual String[] SelectTriggerListeners(ConnectionAndTransactionHolder conn, string triggerName,
                                                       string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_TRIGGER_LISTENERS)))
            {
                AddCommandParameter(cmd, 1, "triggerName", triggerName);
                AddCommandParameter(cmd, 2, "triggerGroup", groupName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    ArrayList list = new ArrayList();
                    while (rs.Read())
                    {
                        list.Add(rs[0]);
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
        public virtual int DeleteSimpleTrigger(ConnectionAndTransactionHolder conn, string triggerName, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(DELETE_SIMPLE_TRIGGER)))
            {
                AddCommandParameter(cmd, 1, "triggerName", triggerName);
                AddCommandParameter(cmd, 2, "triggerGroup", groupName);

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
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(DELETE_CRON_TRIGGER)))
            {
                AddCommandParameter(cmd, 1, "triggerName", triggerName);
                AddCommandParameter(cmd, 2, "triggerGroup", groupName);

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
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(DELETE_BLOB_TRIGGER)))
            {
                AddCommandParameter(cmd, 1, "triggerName", triggerName);
                AddCommandParameter(cmd, 2, "triggerGroup", groupName);

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
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(DELETE_TRIGGER)))
            {
                AddCommandParameter(cmd, 1, "triggerName", triggerName);
                AddCommandParameter(cmd, 2, "triggerGroup", groupName);

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
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_NUM_TRIGGERS_FOR_JOB)))
            {
                AddCommandParameter(cmd, 1, "jobName", jobName);
                AddCommandParameter(cmd, 2, "jobGroup", groupName);

                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        return Convert.ToInt32(rs.GetValue(0));
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
        public virtual JobDetail SelectJobForTrigger(ConnectionAndTransactionHolder conn, string triggerName,
                                                     string groupName,
                                                     IClassLoadHelper loadHelper)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_JOB_FOR_TRIGGER)))
            {
                AddCommandParameter(cmd, 1, "triggerName", triggerName);
                AddCommandParameter(cmd, 2, "triggerGroup", groupName);
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
        /// an array of <code>(@link org.quartz.Trigger)</code> objects
        /// associated with a given job.
        /// </returns>
        public virtual Trigger[] SelectTriggersForJob(ConnectionAndTransactionHolder conn, string jobName,
                                                      string groupName)
        {
            ArrayList triggerIdentifiers = new ArrayList();
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_TRIGGERS_FOR_JOB)))
            {
                AddCommandParameter(cmd, 1, "jobName", jobName);
                AddCommandParameter(cmd, 2, "jobGroup", groupName);
                
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    while (rs.Read())
                    {
                        Pair p = new Pair();
                        p.First = rs.GetString(0);
                        p.Second = rs.GetString(1);
                        triggerIdentifiers.Add(p);
                    }
                }
            }

            ArrayList trigList = new ArrayList();
            foreach (Pair p in triggerIdentifiers)
            {
                Trigger t = SelectTrigger(conn, (string) p.First, (string) p.Second);
                if (t != null)
                {
                    trigList.Add(t);
                }
            }

            return (Trigger[]) trigList.ToArray(typeof (Trigger));
        }


        /// <summary>
        /// Select the triggers for a calendar
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <param name="calName">Name of the calendar.</param>
        /// <returns>
        /// An array of <code>Trigger</code> objects associated with a given job.
        /// </returns>
        public virtual Trigger[] SelectTriggersForCalendar(ConnectionAndTransactionHolder conn, string calName)
        {
            ArrayList trigList = new ArrayList();
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_TRIGGERS_FOR_CALENDAR)))
            {
                AddCommandParameter(cmd, 1, "calendarName", calName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    while (rs.Read())
                    {
                        trigList.Add(
                            SelectTrigger(conn, GetString(rs[COL_TRIGGER_NAME]), GetString(rs[COL_TRIGGER_GROUP])));
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
        public virtual IList SelectStatefulJobsOfTriggerGroup(ConnectionAndTransactionHolder conn, string groupName)
        {
            ArrayList jobList = new ArrayList();
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_STATEFUL_JOBS_OF_TRIGGER_GROUP)))
            {
                AddCommandParameter(cmd, 1, "jobGroup", groupName);
                AddCommandParameter(cmd, 2, "isStateful", GetDbBooleanValue(true));
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    while (rs.Read())
                    {
                        jobList.Add(
                            new Key(GetString(rs[COL_JOB_NAME]), GetString(rs[COL_JOB_GROUP])));
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
            NullableDateTime pft = null;
            NullableDateTime endTimeD = null;
            NullableDateTime nft = null;
            DateTime startTimeD = DateTime.MinValue;

            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_TRIGGER)))
            {
                AddCommandParameter(cmd, 1, "triggerName", triggerName);
                AddCommandParameter(cmd, 2, "triggerGroup", groupName);

                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        jobName = GetString(rs[COL_JOB_NAME]);
                        jobGroup = GetString(rs[COL_JOB_GROUP]);
                        volatility = GetBoolean(rs[COL_IS_VOLATILE]);
                        description = GetString(rs[COL_DESCRIPTION]);
                        long nextFireTime = Convert.ToInt64(rs[COL_NEXT_FIRE_TIME]);
                        long prevFireTime = Convert.ToInt64(rs[COL_PREV_FIRE_TIME]);
                        triggerType = GetString(rs[COL_TRIGGER_TYPE]);
                        long startTime = Convert.ToInt64(rs[COL_START_TIME]);
                        long endTime = Convert.ToInt64(rs[COL_END_TIME]);
                        calendarName = GetString(rs[COL_CALENDAR_NAME]);
                        misFireInstr = Convert.ToInt32(rs[COL_MISFIRE_INSTRUCTION]);
                        priority = Convert.ToInt32(rs[COL_PRIORITY]);
                        
                        if (CanUseProperties)
                        {
                            map = GetMapFromProperties(rs, 15);
                        }
                        else
                        {
                            map = (IDictionary) GetObjectFromBlob(rs, 15);
                        }

                        
                        if (nextFireTime > 0)
                        {
                            nft = new DateTime(nextFireTime);
                        }
                        
                        if (prevFireTime > 0)
                        {
                            pft = new DateTime(prevFireTime);
                        }

                        startTimeD = new DateTime(startTime);

                        if (endTime > 0)
                        {
                            endTimeD = new DateTime(endTime);
                        }
                    }
                }
            }

            if (triggerType.Equals(TTYPE_SIMPLE))
            {
                    using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_SIMPLE_TRIGGER)))
                    {
                        AddCommandParameter(cmd, 1, "triggerName", triggerName);
                        AddCommandParameter(cmd, 2, "triggerGroup", groupName);
                        using (IDataReader rs = cmd.ExecuteReader())
                        {
                            if (rs.Read())
                            {
                                int repeatCount = Convert.ToInt32(rs[COL_REPEAT_COUNT]);
                                long repeatInterval = Convert.ToInt64(rs[COL_REPEAT_INTERVAL]);
                                int timesTriggered = Convert.ToInt32(rs[COL_TIMES_TRIGGERED]);

                                SimpleTrigger st =
                                    new SimpleTrigger(triggerName, groupName, jobName, jobGroup, startTimeD, endTimeD,
                                                      repeatCount,
                                                      repeatInterval);
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
                else if (triggerType.Equals(TTYPE_CRON))
                {
                    using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_CRON_TRIGGER)))
                    {
                        AddCommandParameter(cmd, 1, "triggerName", triggerName);
                        AddCommandParameter(cmd, 2, "triggerGroup", groupName);
                        using (IDataReader rs = cmd.ExecuteReader())
                        {
                            if (rs.Read())
                            {
                                string cronExpr = GetString(rs[COL_CRON_EXPRESSION]);
                                string timeZoneId = GetString(rs[COL_TIME_ZONE_ID]);

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
                else if (triggerType.Equals(TTYPE_BLOB))
                {
                    using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_BLOB_TRIGGER)))
                    {
                        AddCommandParameter(cmd, 1, "triggerName", triggerName);
                        AddCommandParameter(cmd, 2, "triggerGroup", groupName);
                        using (IDataReader rs = cmd.ExecuteReader())
                        {
                            if (rs.Read())
                            {
                                trigger = (Trigger) GetObjectFromBlob(rs, 2);
                            }
                        }
                    }
                }
                else
                {
                    throw new Exception("Class for trigger type '" + triggerType + "' not found.");
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
                return (string) columnValue;
            }
        }

        protected virtual bool GetBoolean(object columnValue)
        {
            // default to treat values as ints
            if (columnValue != null)
            {
                return Convert.ToInt32(columnValue) == 1;
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
        /// <returns>
        /// the <code>{@link org.quartz.JobDataMap}</code> of the Trigger,
        /// never null, but possibly empty.
        /// </returns>
        public virtual JobDataMap SelectTriggerJobDataMap(ConnectionAndTransactionHolder conn, string triggerName,
                                                          string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_TRIGGER_DATA)))
            {
                AddCommandParameter(cmd, 1, "triggerName", triggerName);
                AddCommandParameter(cmd, 2, "triggerGroup", groupName);

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
        /// <returns>
        /// the <code>{@link org.quartz.Trigger}</code> object
        /// </returns>
        public virtual string SelectTriggerState(ConnectionAndTransactionHolder conn, string triggerName,
                                                 string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_TRIGGER_STATE)))
            {
                string state;

                AddCommandParameter(cmd, 1, "triggerName", triggerName);
                AddCommandParameter(cmd, 2, "triggerGroup", groupName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        state = GetString(rs[COL_TRIGGER_STATE]);
                    }
                    else
                    {
                        state = STATE_DELETED;
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
        public virtual TriggerStatus SelectTriggerStatus(ConnectionAndTransactionHolder conn, string triggerName,
                                                         string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_TRIGGER_STATUS)))
            {
                TriggerStatus status = null;

                AddCommandParameter(cmd, 1, "triggerName", triggerName);
                AddCommandParameter(cmd, 2, "triggerGroup", groupName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        string state = GetString(rs[COL_TRIGGER_STATE]);
                        long nextFireTime = Convert.ToInt64(rs[COL_NEXT_FIRE_TIME]);
                        string jobName = GetString(rs[COL_JOB_NAME]);
                        string jobGroup = GetString(rs[COL_JOB_GROUP]);

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
        public virtual int SelectNumTriggers(ConnectionAndTransactionHolder conn)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_NUM_TRIGGERS)))
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
        /// an array of <code>String</code> group names
        /// </returns>
        public virtual string[] SelectTriggerGroups(ConnectionAndTransactionHolder conn)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_TRIGGER_GROUPS)))
            {
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    ArrayList list = new ArrayList();
                    while (rs.Read())
                    {
                        list.Add(rs[0]);
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
        public virtual string[] SelectTriggersInGroup(ConnectionAndTransactionHolder conn, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_TRIGGERS_IN_GROUP)))
            {
                AddCommandParameter(cmd, 1, "triggerGroup", groupName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    ArrayList list = new ArrayList();
                    while (rs.Read())
                    {
                        list.Add(rs[0]);
                    }

                    return (string[]) list.ToArray(typeof (string));
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
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(INSERT_PAUSED_TRIGGER_GROUP)))
            {
                AddCommandParameter(cmd, 1, "triggerGroup", groupName);
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
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(DELETE_PAUSED_TRIGGER_GROUP)))
            {
                AddCommandParameter(cmd, 1, "triggerGroup", groupName);
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
        public virtual bool IsTriggerGroupPaused(ConnectionAndTransactionHolder conn, string groupName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_PAUSED_TRIGGER_GROUP)))
            {
                AddCommandParameter(cmd, 1, "triggerGroup", groupName);
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
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_NUM_TRIGGERS_IN_GROUP)))
            {
                AddCommandParameter(cmd, 1, "triggerGroup", groupName);
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

            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(INSERT_CALENDAR)))
            {
                AddCommandParameter(cmd, 1, "calendarName", calendarName);
                AddCommandParameter(cmd, 2, "calendar", baos, dbProvider.Metadata.DbBinaryType);

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

            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_CALENDAR)))
            {
                AddCommandParameter(cmd, 1, "calendar", baos, dbProvider.Metadata.DbBinaryType);
                AddCommandParameter(cmd, 2, "calendarName", calendarName);

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
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_CALENDAR_EXISTENCE)))
            {
                AddCommandParameter(cmd, 1, "calendarName", calendarName);
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
        public virtual ICalendar SelectCalendar(ConnectionAndTransactionHolder conn, string calendarName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_CALENDAR)))
            {
                AddCommandParameter(cmd, 1, "calendarName", calendarName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    ICalendar cal = null;
                    if (rs.Read())
                    {
                        cal = (ICalendar) GetObjectFromBlob(rs, 1);
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
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_REFERENCED_CALENDAR)))
            {
                AddCommandParameter(cmd, 1, "calendarName", calendarName);
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
        public virtual int DeleteCalendar(ConnectionAndTransactionHolder conn, string calendarName)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(DELETE_CALENDAR)))
            {
                AddCommandParameter(cmd, 1, "calendarName", calendarName);
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
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_NUM_CALENDARS)))
            {
                int count = 0;
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        count = Convert.ToInt32(rs.GetValue(0));
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
        public virtual String[] SelectCalendars(ConnectionAndTransactionHolder conn)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_CALENDARS)))
            {
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    ArrayList list = new ArrayList();
                    while (rs.Read())
                    {
                        list.Add(rs[0]);
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
        public virtual NullableDateTime SelectNextFireTimeDEPRECATED(ConnectionAndTransactionHolder conn)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_NEXT_FIRE_TIME)))
            {
                AddCommandParameter(cmd, 1, "state", STATE_WAITING);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        return new DateTime(Convert.ToInt64(rs[ALIAS_COL_NEXT_FIRE_TIME]));
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
        public virtual Key SelectTriggerForFireTime(ConnectionAndTransactionHolder conn, DateTime fireTime)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_TRIGGER_FOR_FIRE_TIME)))
            {
                AddCommandParameter(cmd, 1, "state", STATE_WAITING);
                AddCommandParameter(cmd, 2, "fireTime", Convert.ToDecimal(fireTime.Ticks));

                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        return
                            new Key(GetString(rs[COL_TRIGGER_NAME]), GetString(rs[COL_TRIGGER_GROUP]));
                    }
                    else
                    {
                        return null;
                    }
                }
            }
        }

        /// <summary>
        /// Select the next trigger which will fire to fire between the two given timestamps
        /// in ascending order of fire time, and then descending by priority.
        /// </summary>
        /// <param name="conn">The conn.</param>
        /// <param name="noLaterThan">highest value of <see cref="Trigger.GetNextFireTime"/> of the triggers (exclusive)</param>
        /// <param name="noEarlierThan">highest value of <see cref="Trigger.GetNextFireTime"/> of the triggers (inclusive)</param>
        /// <returns></returns>
        public virtual Key SelectTriggerToAcquire(ConnectionAndTransactionHolder conn, DateTime noLaterThan, DateTime noEarlierThan)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_NEXT_TRIGGER_TO_ACQUIRE)))
            {
                // Try to give jdbc driver a hint to hopefully not pull over 
                // more than the one row we actually need.
                // TODO ps.setFetchSize(1);
                // TODO ps.setMaxRows(1);

                AddCommandParameter(cmd, 1, "state", STATE_WAITING);
                AddCommandParameter(cmd, 2, "noLaterThan", Convert.ToDecimal(noLaterThan.Ticks));
                AddCommandParameter(cmd, 3, "noEarlierThan", Convert.ToDecimal(noEarlierThan.Ticks));
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        return new Key(GetString(rs[COL_TRIGGER_NAME]), GetString(rs[COL_TRIGGER_GROUP]));
                    }
                }
                return null;
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
        public virtual int InsertFiredTrigger(ConnectionAndTransactionHolder conn, Trigger trigger, string state,
                                              JobDetail job)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(INSERT_FIRED_TRIGGER)))
            {
                AddCommandParameter(cmd, 1, "triggerEntryId", trigger.FireInstanceId);
                AddCommandParameter(cmd, 2, "triggerName", trigger.Name);
                AddCommandParameter(cmd, 3, "triggerGroup", trigger.Group);
                AddCommandParameter(cmd, 4, "triggerVolatile", GetDbBooleanValue(trigger.Volatile));
                AddCommandParameter(cmd, 5, "triggerInstanceName", instanceId);
                AddCommandParameter(cmd, 6, "triggerFireTime",
                                    Convert.ToDecimal(trigger.GetNextFireTime().Value.Ticks));
                AddCommandParameter(cmd, 7, "triggerState", state);
                if (job != null)
                {
                    AddCommandParameter(cmd, 8, "triggerJobName", trigger.JobName);
                    AddCommandParameter(cmd, 9, "triggerJobGroup", trigger.JobGroup);
                    AddCommandParameter(cmd, 10, "triggerJobStateful", GetDbBooleanValue(job.Stateful));
                    AddCommandParameter(cmd, 11, "triggerJobRequestsRecovery", GetDbBooleanValue(job.RequestsRecovery));
                }
                else
                {
                    AddCommandParameter(cmd, 8, "triggerJobName", null);
                    AddCommandParameter(cmd, 9, "triggerJobGroup", null);
                    AddCommandParameter(cmd, 10, "triggerJobStateful", GetDbBooleanValue(false));
                    AddCommandParameter(cmd, 11, "triggerJobRequestsRecovery", GetDbBooleanValue(false));
                }

                AddCommandParameter(cmd, 12, "triggerPriority", trigger.Priority);

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
        public virtual IList SelectFiredTriggerRecords(ConnectionAndTransactionHolder conn, string triggerName,
                                                       string groupName)
        {
            IDbCommand cmd;

            IList lst = new ArrayList();

            if (triggerName != null)
            {
                cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_FIRED_TRIGGER));
                AddCommandParameter(cmd, 1, "triggerName", triggerName);
                AddCommandParameter(cmd, 2, "triggerGroup", groupName);
            }
            else
            {
                cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_FIRED_TRIGGER_GROUP));
                AddCommandParameter(cmd, 1, "triggerGroup", groupName);
            }

            using (IDataReader rs = cmd.ExecuteReader())
            {
                while (rs.Read())
                {
                    FiredTriggerRecord rec = new FiredTriggerRecord();

                    rec.FireInstanceId = GetString(rs[COL_ENTRY_ID]);
                    rec.FireInstanceState = GetString(rs[COL_ENTRY_STATE]);
                    rec.FireTimestamp = Convert.ToInt64(rs[COL_FIRED_TIME]);
                    rec.Priority = Convert.ToInt32(rs[COL_PRIORITY]);
                    rec.SchedulerInstanceId = GetString(rs[COL_INSTANCE_NAME]);
                    rec.TriggerIsVolatile = GetBoolean(rs[COL_IS_VOLATILE]);
                    rec.TriggerKey = new Key(GetString(rs[COL_TRIGGER_NAME]), GetString(rs[COL_TRIGGER_GROUP]));
                    if (!rec.FireInstanceState.Equals(STATE_ACQUIRED))
                    {
                        rec.JobIsStateful = GetBoolean(rs[COL_IS_STATEFUL]);
                        rec.JobRequestsRecovery = GetBoolean(rs[COL_REQUESTS_RECOVERY]);
                        rec.JobKey =
                            new Key(GetString(rs[COL_JOB_NAME]), GetString(rs[COL_JOB_GROUP]));
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
        public virtual IList SelectFiredTriggerRecordsByJob(ConnectionAndTransactionHolder conn, string jobName,
                                                            string groupName)
        {
            IList lst = new ArrayList();

            IDbCommand cmd;
            if (jobName != null)
            {
                cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_FIRED_TRIGGERS_OF_JOB));
                AddCommandParameter(cmd, 1, "jobName", jobName);
                AddCommandParameter(cmd, 2, "jobGroup", groupName);
            }
            else
            {
                cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_FIRED_TRIGGERS_OF_JOB_GROUP));
                AddCommandParameter(cmd, 1, "jobGroup", groupName);
            }

            using (IDataReader rs = cmd.ExecuteReader())
            {
                while (rs.Read())
                {
                    FiredTriggerRecord rec = new FiredTriggerRecord();

                    rec.FireInstanceId = GetString(rs[COL_ENTRY_ID]);
                    rec.FireInstanceState = GetString(rs[COL_ENTRY_STATE]);
                    rec.FireTimestamp = Convert.ToInt64(rs[COL_FIRED_TIME]);
                    rec.Priority = Convert.ToInt32(rs[COL_PRIORITY]);
                    rec.SchedulerInstanceId = GetString(rs[COL_INSTANCE_NAME]);
                    rec.TriggerIsVolatile = GetBoolean(rs[COL_IS_VOLATILE]);
                    rec.TriggerKey = new Key(GetString(rs[COL_TRIGGER_NAME]), GetString(rs[COL_TRIGGER_GROUP]));
                    if (!rec.FireInstanceState.Equals(STATE_ACQUIRED))
                    {
                        rec.JobIsStateful = GetBoolean(rs[COL_IS_STATEFUL]);
                        rec.JobRequestsRecovery = GetBoolean(rs[COL_REQUESTS_RECOVERY]);
                        rec.JobKey = new Key(GetString(rs[COL_JOB_NAME]), GetString(rs[COL_JOB_GROUP]));
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
        public virtual IList SelectInstancesFiredTriggerRecords(ConnectionAndTransactionHolder conn, string instanceName)
        {
            IList lst = new ArrayList();

            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_INSTANCES_FIRED_TRIGGERS)))
            {
                AddCommandParameter(cmd, 1, "instanceName", instanceName);
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    while (rs.Read())
                    {
                        FiredTriggerRecord rec = new FiredTriggerRecord();

                        rec.FireInstanceId = GetString(rs[COL_ENTRY_ID]);
                        rec.FireInstanceState = GetString(rs[COL_ENTRY_STATE]);
                        rec.FireTimestamp = Convert.ToInt64(rs[COL_FIRED_TIME]);
                        rec.SchedulerInstanceId = GetString(rs[COL_INSTANCE_NAME]);
                        rec.TriggerIsVolatile = GetBoolean(rs[COL_IS_VOLATILE]);
                        rec.TriggerKey = new Key(GetString(rs[COL_TRIGGER_NAME]), GetString(rs[COL_TRIGGER_GROUP]));
                        if (!rec.FireInstanceState.Equals(STATE_ACQUIRED))
                        {
                            rec.JobIsStateful = GetBoolean(rs[COL_IS_STATEFUL]);
                            rec.JobRequestsRecovery = GetBoolean(rs[COL_REQUESTS_RECOVERY]);
                            rec.JobKey = new Key(GetString(rs[COL_JOB_NAME]), GetString(rs[COL_JOB_GROUP]));
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
        public ISet SelectFiredTriggerInstanceNames(ConnectionAndTransactionHolder conn)
        {
            ISet instanceNames = new HashSet();
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_FIRED_TRIGGER_INSTANCE_NAMES)))
            {
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    while (rs.Read())
                    {
                        instanceNames.Add(rs[COL_INSTANCE_NAME]);
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
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(DELETE_FIRED_TRIGGER)))
            {
                AddCommandParameter(cmd, 1, "triggerEntryId", entryId);
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
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_JOB_EXECUTION_COUNT)))
            {
                AddCommandParameter(cmd, 1, "jobName", jobName);
                AddCommandParameter(cmd, 2, "jobGroup", jobGroup);

                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        return Convert.ToInt32(rs.GetValue(0));
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
        public virtual int DeleteVolatileFiredTriggers(ConnectionAndTransactionHolder conn)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(DELETE_VOLATILE_FIRED_TRIGGERS)))
            {
                AddCommandParameter(cmd, 1, "volatile", true);
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
        public virtual int InsertSchedulerState(ConnectionAndTransactionHolder conn, string instanceName, DateTime checkInTime, long interval)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(INSERT_SCHEDULER_STATE)))
            {
                AddCommandParameter(cmd, 1, "instanceName", instanceName);
                AddCommandParameter(cmd, 2, "lastCheckinTime", checkInTime.Ticks);
                AddCommandParameter(cmd, 3, "checkinInterval", interval);

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
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(DELETE_SCHEDULER_STATE)))
            {
                AddCommandParameter(cmd, 1, "instanceName", instanceName);

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
        public virtual int UpdateSchedulerState(ConnectionAndTransactionHolder conn, string instanceName, DateTime checkInTime)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_SCHEDULER_STATE)))
            {
                AddCommandParameter(cmd, 1, "lastCheckinTime", checkInTime.Ticks);
                AddCommandParameter(cmd, 2, "instanceName", instanceName);

                return cmd.ExecuteNonQuery();
            }
        }


        /// <summary>
        /// A List of all current <code>SchedulerStateRecords</code>.
        /// <p>
        /// If instanceId is not null, then only the record for the identified
        /// instance will be returned.
        /// </p>
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="instanceName">The instance id.</param>
        /// <returns></returns>
        public virtual IList SelectSchedulerStateRecords(ConnectionAndTransactionHolder conn, string instanceName)
        {
            IDbCommand cmd;

            ArrayList list = new ArrayList();

            if (instanceName != null)
            {
                cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_SCHEDULER_STATE));
                AddCommandParameter(cmd, 1, "instanceName", instanceName);
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
                    rec.SchedulerInstanceId = GetString(rs[COL_INSTANCE_NAME]);
                    rec.CheckinTimestamp = new DateTime(Convert.ToInt64(rs[COL_LAST_CHECKIN_TIME]));
                    rec.CheckinInterval = Convert.ToInt64(rs[COL_CHECKIN_INTERVAL]);
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
            // TODO return new Hashtable(properties);
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
                object val = entry.Value == null ? string.Empty : entry.Value;

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

            // TODO CHECK WITH QUARTZ 1.6   
            byte[] data = ReadBytesFromBlob(rs, colIndex);
            if (data != null)
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
                retval = (int) dr.GetBytes(colIndex, startIndex, outbyte, 0, bufferSize);
            }

            // Write the remaining buffer.
            stream.Write(outbyte, 0, retval);

            return stream.GetBuffer();
        }


        /// <summary>
        /// Get the names of all of the triggers that are volatile.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <returns>An array of <code>Key</code> objects.</returns>
        public virtual Key[] SelectVolatileTriggers(ConnectionAndTransactionHolder conn)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_VOLATILE_TRIGGERS)))
            {
                AddCommandParameter(cmd, 1, "isVolatile", GetDbBooleanValue(true));
                using (IDataReader rs = cmd.ExecuteReader())
                {
                    ArrayList list = new ArrayList();
                    while (rs.Read())
                    {
                        string triggerName = GetString(rs[COL_TRIGGER_NAME]);
                        string groupName = GetString(rs[COL_TRIGGER_GROUP]);
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
        /// Get the names of all of the jobs that are volatile.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <returns>An array of <code>Key</code> objects.</returns>
        public virtual Key[] SelectVolatileJobs(ConnectionAndTransactionHolder conn)
        {
            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_VOLATILE_JOBS)))
            {
                AddCommandParameter(cmd, 1, "isVolatile", GetDbBooleanValue(true));
                using (IDataReader dr = cmd.ExecuteReader())
                {
                    ArrayList list = new ArrayList();
                    while (dr.Read())
                    {
                        string triggerName = (string) dr[COL_JOB_NAME];
                        string groupName = (string) dr[COL_JOB_GROUP];
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
        public virtual ISet SelectPausedTriggerGroups(ConnectionAndTransactionHolder conn)
        {
            HashSet retValue = new HashSet();


            using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_PAUSED_TRIGGER_GROUPS)))
            {
                using (IDataReader dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        string groupName = (string) dr[COL_TRIGGER_GROUP];
                        retValue.Add(groupName);
                    }
                }
                return retValue;
            }
        }

        protected IDbCommand PrepareCommand(ConnectionAndTransactionHolder cth, string commandText)
        {
            return adoUtil.PrepareCommand(cth, commandText);
        }


        protected void AddCommandParameter(IDbCommand cmd, int parameterIndex, string paramName, object paramValue)
        {
            AddCommandParameter(cmd, parameterIndex, paramName, paramValue, null);
        }

        protected void AddCommandParameter(IDbCommand cmd, int parameterIndex, string paramName, object paramValue,
                                           Enum dataType)
        {
            adoUtil.AddCommandParameter(cmd, parameterIndex, paramName, paramValue, dataType);
        }
    }


    // EOF
}