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
using System.Data.OleDb;
using System.Globalization;
using System.IO;
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

		/// <summary> <p>
		/// Create new StdAdoDelegate instance.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">logger
		/// the logger to use during execution
		/// </param>
		/// <param name="">tablePrefix
		/// the prefix of all table names
		/// </param>
		public StdAdoDelegate(ILog logger, string tablePrefix, string instanceId)
		{
			this.logger = logger;
			this.tablePrefix = tablePrefix;
			this.instanceId = instanceId;
		}

		/// <summary> <p>
		/// Create new StdAdoDelegate instance.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">logger
		/// the logger to use during execution
		/// </param>
		/// <param name="">tablePrefix
		/// the prefix of all table names
		/// </param>
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

		protected internal virtual bool CanUseProperties()
		{
			return useProperties;
		}

		//---------------------------------------------------------------------------
		// startup / recovery
		//---------------------------------------------------------------------------

		/// <summary> <p>
		/// Insert the job detail record.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <param name="">newState
		/// the new state for the triggers
		/// </param>
		/// <param name="">oldState1
		/// the first old state to update
		/// </param>
		/// <param name="">oldState2
		/// the second old state to update
		/// </param>
		/// <returns> number of rows updated
		/// </returns>
		public virtual int UpdateTriggerStatesFromOtherStates(IDbConnection legacy, string newState, string oldState1,
		                                                      string oldState2)
		{
			using (IDbConnection con = dbProvider.CreateConnection())
			{
				using (IDbCommand cmd = PrepareCommand(con, ReplaceTablePrefix(UPDATE_TRIGGER_STATES_FROM_OTHER_STATES)))
				{
					AddCommandParameter(cmd, 1, newState);
					AddCommandParameter(cmd, 2, oldState1);
					AddCommandParameter(cmd, 3, oldState2);
					return cmd.ExecuteNonQuery();
				}
			}
		}


		/// <summary> <p>
		/// Get the names of all of the triggers that have misfired.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <returns> an array of <code>{@link
		/// org.quartz.utils.Key}</code> objects
		/// </returns>
		public virtual Key[] SelectMisfiredTriggers(IDbConnection legacy, long ts)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
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
		}

		/// <summary> 
		/// Select all of the triggers in a given state.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <param name="state">The state the triggers must be in</param>
		/// <returns> an array of trigger <code>Key</code>s </returns>
		public virtual Key[] SelectTriggersInState(IDbConnection legacy, string state)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
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
		}


		public virtual Key[] SelectMisfiredTriggersInState(IDbConnection legacy, string state, long ts)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
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
		}

		/// <summary>
		/// Get the names of all of the triggers in the given group and state that
		/// have misfired.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <returns> an array of <code>Key</code> objects</returns>
		public virtual Key[] SelectMisfiredTriggersInGroupInState(IDbConnection legacy, string groupName, string state,
		                                                          long ts)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
			{
				using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_MISFIRED_TRIGGERS_IN_GROUP_IN_STATE)))
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
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <returns> an array of <code>{@link org.quartz.Trigger}</code> objects
		/// </returns>
		public virtual Trigger[] SelectTriggersForRecoveringJobs(IDbConnection legacy)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
			{
				using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_INSTANCES_RECOVERABLE_FIRED_TRIGGERS)))
				{
					AddCommandParameter(cmd, 1, instanceId);
					AddCommandParameter(cmd, 2, true);

					using (IDataReader rs = cmd.ExecuteReader())
					{
						// TODO
						long dumId = (DateTime.Now.Ticks - 621355968000000000) / 10000;
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
							jd.Put("QRTZ_FAILED_JOB_ORIG_TRIGGER_FIRETIME_IN_MILLISECONDS_AS_STRING", Convert.ToString(firedTime));
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
		}

		/// <summary>
		/// Delete all fired triggers.
		/// </summary>
		/// <param name="legacy">The DB Connection.</param>
		/// <returns>The number of rows deleted.</returns>
		public virtual int DeleteFiredTriggers(IDbConnection legacy)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
			{
				using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(DELETE_FIRED_TRIGGERS)))
				{
					return cmd.ExecuteNonQuery();
				}
			}
		}


		public virtual int DeleteFiredTriggers(IDbConnection legacy, string instanceId)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
			{
				using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(DELETE_INSTANCES_FIRED_TRIGGERS)))
				{
					AddCommandParameter(cmd, 1, instanceId);
					return cmd.ExecuteNonQuery();
				}
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
		public virtual int InsertJobDetail(IDbConnection legacy, JobDetail job)
		{
			MemoryStream baos = SerializeJobData(job.JobDataMap);

			int insertResult = 0;
			using (IDbConnection conn = dbProvider.CreateConnection())
			{
				using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(INSERT_JOB_DETAIL)))
				{
					AddCommandParameter(cmd, 1, job.Name);
					AddCommandParameter(cmd, 2, job.Group);
					AddCommandParameter(cmd, 3, job.Description);
					//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Class.getName' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
					AddCommandParameter(cmd, 4, job.JobClass.FullName);
					AddCommandParameter(cmd, 5, job.Durable);
					AddCommandParameter(cmd, 6, job.Volatile);
					AddCommandParameter(cmd, 7, job.Stateful);
					AddCommandParameter(cmd, 8, job.requestsRecovery());
					AddCommandParameter(cmd, 9, SupportClass.ToSByteArray(baos.ToArray()));

					insertResult = cmd.ExecuteNonQuery();

					if (insertResult > 0)
					{
						String[] jobListeners = job.JobListenerNames;
						for (int i = 0; jobListeners != null && i < jobListeners.Length; i++)
						{
							InsertJobListener(conn, job, jobListeners[i]);
						}
					}
				}
			}


			return insertResult;
		}

		/// <summary>
		/// Update the job detail record.
		/// </summary>
		/// <param name="legacy">The DB Connection.</param>
		/// <param name="job">The job to update.</param>
		/// <returns>Number of rows updated.</returns>
		public virtual int UpdateJobDetail(IDbConnection legacy, JobDetail job)
		{
			MemoryStream baos = SerializeJobData(job.JobDataMap);

			using (IDbConnection conn = dbProvider.CreateConnection())
			{
				using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_JOB_DETAIL)))
				{
					AddCommandParameter(cmd, 1, job.Description);
					AddCommandParameter(cmd, 2, job.JobClass.FullName);
					AddCommandParameter(cmd, 3, job.Durable);
					AddCommandParameter(cmd, 4, job.Volatile);
					AddCommandParameter(cmd, 5, job.Stateful);
					AddCommandParameter(cmd, 6, job.requestsRecovery());
					AddCommandParameter(cmd, 7, SupportClass.ToSByteArray(baos.ToArray()));
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
			
		}

		/// <summary>
		/// Get the names of all of the triggers associated with the given job.
		/// </summary>
		/// <param name="legacy">The DB Connection.</param>
		/// <param name="jobName">The name of the job.</param>
		/// <param name="groupName">The group containing the job.</param>
		/// <returns>An array of <code>Key</code> objects</returns>
		public virtual Key[] SelectTriggerNamesForJob(IDbConnection legacy, string jobName, string groupName)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
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
		}

		/// <summary>
		/// Delete all job listeners for the given job.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <param name="jobName">The name of the job.</param>
		/// <param name="groupName">The group containing the job.</param>
		/// <returns>The number of rows deleted.</returns>
		public virtual int DeleteJobListeners(IDbConnection legacy, string jobName, string groupName)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
			{
				using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(DELETE_JOB_LISTENERS)))
				{
					AddCommandParameter(cmd, 1, jobName);
					AddCommandParameter(cmd, 2, groupName);
					return cmd.ExecuteNonQuery();
				}
			}
		}

		/// <summary> <p>
		/// Delete the job detail record for the given job.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <param name="">jobName
		/// the name of the job
		/// </param>
		/// <param name="">groupName
		/// the group containing the job
		/// </param>
		/// <returns> the number of rows deleted
		/// </returns>
		public virtual int DeleteJobDetail(IDbConnection legacy, string jobName, string groupName)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
			{
				using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(DELETE_JOB_DETAIL)))
				{
					logger.Debug("Deleting job: " + groupName + "." + jobName);
					AddCommandParameter(cmd, 1, jobName);
					AddCommandParameter(cmd, 2, groupName);
					return cmd.ExecuteNonQuery();
				}
			}
		}

		/// <summary> <p>
		/// Check whether or not the given job is stateful.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <param name="">jobName
		/// the name of the job
		/// </param>
		/// <param name="">groupName
		/// the group containing the job
		/// </param>
		/// <returns> true if the job exists and is stateful, false otherwise
		/// </returns>
		public virtual bool IsJobStateful(IDbConnection legacy, string jobName, string groupName)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
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
		}

		/// <summary> <p>
		/// Check whether or not the given job exists.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <param name="">jobName
		/// the name of the job
		/// </param>
		/// <param name="">groupName
		/// the group containing the job
		/// </param>
		/// <returns> true if the job exists, false otherwise
		/// </returns>
		public virtual bool JobExists(IDbConnection legacy, string jobName, string groupName)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
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
		}

		/// <summary> <p>
		/// Update the job data map for the given job.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <param name="">job
		/// the job to update
		/// </param>
		/// <returns> the number of rows updated
		/// </returns>
		public virtual int UpdateJobData(IDbConnection legacy, JobDetail job)
		{
			MemoryStream baos = SerializeJobData(job.JobDataMap);
			using (IDbConnection conn = dbProvider.CreateConnection())
			{
				using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_JOB_DATA)))
				{
					AddCommandParameter(cmd, 1, SupportClass.ToSByteArray(baos.ToArray()));
					AddCommandParameter(cmd, 2, job.Name);
					AddCommandParameter(cmd, 3, job.Group);

					return cmd.ExecuteNonQuery();
				}
			}
		}

		/// <summary>
		/// Associate a listener with a job.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <param name="job">The job to associate with the listener.</param>
		/// <param name="listener">The listener to insert.</param>
		/// <returns>The number of rows inserted.</returns>
		public virtual int InsertJobListener(IDbConnection legacy, JobDetail job, string listener)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
			{
				using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(INSERT_JOB_LISTENER)))
				{
					AddCommandParameter(cmd, 1, job.Name);
					AddCommandParameter(cmd, 2, job.Group);
					AddCommandParameter(cmd, 3, listener);

					return cmd.ExecuteNonQuery();
				}
			}
		}

		/// <summary>
		/// Get all of the listeners for a given job.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <param name="jobName">The job name whose listeners are wanted.</param>
		/// <param name="groupName">The group containing the job.</param>
		/// <returns>Array of <code>String</code> listener names.</returns>
		public virtual string[] SelectJobListeners(IDbConnection legacy, string jobName, string groupName)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
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
		}

		/// <summary>
		/// Select the JobDetail object for a given job name / group name.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <param name="jobName">The job name whose listeners are wanted.</param>
		/// <param name="groupName">The group containing the job.</param>
		/// <returns>The populated JobDetail object.</returns>
		public virtual JobDetail SelectJobDetail(IDbConnection legacy, string jobName, string groupName,
		                                         IClassLoadHelper loadHelper)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
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
							job.JobClass = loadHelper.LoadClass(Convert.ToString(rs[AdoConstants.COL_JOB_CLASS]));
							job.Durability = Convert.ToBoolean(rs[AdoConstants.COL_IS_DURABLE]);
							job.Volatility = Convert.ToBoolean(rs[AdoConstants.COL_IS_VOLATILE]);
							job.RequestsRecovery = Convert.ToBoolean(rs[AdoConstants.COL_REQUESTS_RECOVERY]);

							IDictionary map = null;
							if (CanUseProperties())
							{
								map = GetMapFromProperties(rs);
							}
							else
							{
								map = (IDictionary) GetObjectFromBlob(rs, AdoConstants.COL_JOB_DATAMAP);
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
		}

		/// <summary> build Map from java.util.Properties encoding.</summary>
		private IDictionary GetMapFromProperties(OleDbDataReader rs)
		{
			IDictionary map;
			Stream is_Renamed = (Stream) GetJobDetailFromBlob(rs, AdoConstants.COL_JOB_DATAMAP);
			if (is_Renamed == null)
			{
				return null;
			}
			NameValueCollection properties = new NameValueCollection();
			if (is_Renamed != null)
			{
				//UPGRADE_TODO: Method 'java.util.Properties.load' was converted to 'System.Collections.Specialized.NameValueCollection' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilPropertiesload_javaioInputStream_3"'
				properties = new NameValueCollection(ConfigurationSettings.AppSettings);
			}
			map = ConvertFromProperty(properties);
			return map;
		}

		/// <summary>
		/// Select the total number of jobs stored.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <returns>The total number of jobs stored.</returns>
		public virtual int SelectNumJobs(IDbConnection legacy)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
			{
				using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SELECT_NUM_JOBS)))
				{

					return (int) cmd.ExecuteScalar();
				}
			}
		}

		/// <summary>
		/// Select all of the job group names that are stored.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <returns>An array of <code>String</code> group names.</returns>
		public virtual string[] SelectJobGroups(IDbConnection legacy)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
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
		}

		/// <summary>
		/// Select all of the jobs contained in a given group.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <param name="groupName">The group containing the jobs.</param>
		/// <returns>An array of <code>String</code> job names.</returns>
		public virtual String[] SelectJobsInGroup(IDbConnection legacy, string groupName)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
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
		}

		//---------------------------------------------------------------------------
		// triggers
		//---------------------------------------------------------------------------

		/// <summary> <p>
		/// Insert the base trigger data.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <param name="">trigger
		/// the trigger to insert
		/// </param>
		/// <param name="">state
		/// the state that the trigger should be stored in
		/// </param>
		/// <returns> the number of rows inserted
		/// </returns>
		public virtual int InsertTrigger(IDbConnection legacy, Trigger trigger, string state, JobDetail jobDetail)
		{
			MemoryStream baos = null;
			if (trigger.JobDataMap.Count > 0)
			{
				baos = SerializeJobData(trigger.JobDataMap);
			}

			OleDbCommand ps = null;

			int insertResult = 0;
			using (IDbConnection conn = dbProvider.CreateConnection())
			{
				using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(INSERT_TRIGGER)))
				{

					AddCommandParameter(cmd, 1, trigger.Name);
					AddCommandParameter(cmd, 2, trigger.Group);
					AddCommandParameter(cmd, 3, trigger.JobName);
					AddCommandParameter(cmd, 4, trigger.JobGroup);
					AddCommandParameter(cmd, 5, trigger.Volatile);
					AddCommandParameter(cmd, 6, trigger.Description);
					//UPGRADE_TODO: Method 'java.util.Date.getTime' was converted to 'DateTime.Ticks' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilDategetTime_3"'
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
					//UPGRADE_TODO: Method 'java.util.Date.getTime' was converted to 'DateTime.Ticks' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilDategetTime_3"'
					AddCommandParameter(cmd, 11,
					                    Decimal.Parse(Convert.ToString(trigger.StartTime.Ticks),
					                                  NumberStyles.Any));
					long endTime = 0;
					//UPGRADE_TODO: The 'DateTime' structure does not have an equivalent to NULL. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1291_3"'
					if (trigger.EndTime.HasValue)
					{
						//UPGRADE_TODO: Method 'java.util.Date.getTime' was converted to 'DateTime.Ticks' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilDategetTime_3"'
						endTime = trigger.EndTime.Value.Ticks;
					}
					AddCommandParameter(cmd, 12, Decimal.Parse(Convert.ToString(endTime), NumberStyles.Any));
					AddCommandParameter(cmd, 13, trigger.CalendarName);
					AddCommandParameter(cmd, 14, trigger.MisfireInstruction);
					if (baos != null)
					{
						AddCommandParameter(cmd, 15, SupportClass.ToSByteArray(baos.ToArray()));
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
		}

		/// <summary>
		/// Insert the simple trigger data.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <param name="trigger">The trigger to insert.</param>
		/// <returns>The number of rows inserted.</returns>
		public virtual int InsertSimpleTrigger(IDbConnection legacy, SimpleTrigger trigger)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
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
		}

		/// <summary> <p>
		/// Insert the cron trigger data.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <param name="">trigger
		/// the trigger to insert
		/// </param>
		/// <returns> the number of rows inserted
		/// </returns>
		public virtual int InsertCronTrigger(IDbConnection legacy, CronTrigger trigger)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
			{
				using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(INSERT_CRON_TRIGGER)))
				{
					AddCommandParameter(cmd, 1, trigger.Name);
					AddCommandParameter(cmd, 2, trigger.Group);
					AddCommandParameter(cmd, 3, trigger.CronExpression);
					AddCommandParameter(cmd, 4, trigger.TimeZone.StandardName);
					
					return cmd.ExecuteNonQuery();
				}
				
			}
		}

		/// <summary>
		/// Insert the blob trigger data.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <param name="trigger">The trigger to insert.</param>
		/// <returns>The number of rows inserted.</returns>
		public virtual int InsertBlobTrigger(IDbConnection legacy, Trigger trigger)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
			{
				using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(INSERT_CRON_TRIGGER)))
				{
					// update the blob
					os = new MemoryStream();
					//UPGRADE_TODO: Class 'java.io.ObjectOutputStream' was converted to 'System.IO.BinaryWriter' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javaioObjectOutputStream_3"'
					BinaryWriter oos = new BinaryWriter(os);
					//UPGRADE_TODO: Method 'java.io.ObjectOutputStream.writeObject' was converted to 'SupportClass.Serialize' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javaioObjectOutputStreamwriteObject_javalangObject_3"'
					SupportClass.Serialize(oos, trigger);
					oos.Close();

					sbyte[] buf = SupportClass.ToSByteArray(os.ToArray());
					MemoryStream is_Renamed = new MemoryStream(SupportClass.ToByteArray(buf));

					cmd =
						SupportClass.TransactionManager.manager.PrepareStatement(conn, ReplaceTablePrefix(INSERT_BLOB_TRIGGER));
					AddCommandParameter(cmd, 1, trigger.Name);
					AddCommandParameter(cmd, 2, trigger.Group);
					AddCommandParameter(cmd, 3, is_Renamed);

					return SupportClass.TransactionManager.manager.ExecuteUpdate(cmd);
				}
			}
		}

		/// <summary>
		/// Update the base trigger data.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <param name="trigger">The trigger to insert.</param>
		/// <param name="state">The state that the trigger should be stored in.</param>
		/// <returns>The number of rows updated.</returns>
		public virtual int UpdateTrigger(IDbConnection legacy, Trigger trigger, string state, JobDetail jobDetail)
		{
			// save some clock cycles by unnecessarily writing job data blob ...
			bool updateJobData = trigger.JobDataMap.Dirty;
			MemoryStream baos = null;
			if (updateJobData && trigger.JobDataMap.Count > 0)
			{
				baos = SerializeJobData(trigger.JobDataMap);
			}

			OleDbCommand ps = null;

			int insertResult = 0;


			try
			{
				if (updateJobData)
				{
					ps = SupportClass.TransactionManager.manager.PrepareStatement(conn, ReplaceTablePrefix(UPDATE_TRIGGER));
				}
				else
				{
					ps =
						SupportClass.TransactionManager.manager.PrepareStatement(conn,
						                                                         ReplaceTablePrefix(UPDATE_TRIGGER_SKIP_DATA));
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
				AddCommandParameter(cmd, 5,
				                                                 Decimal.Parse(Convert.ToString(nextFireTime), NumberStyles.Any));
				long prevFireTime = - 1;

				if (trigger.GetPreviousFireTime().HasValue)
				{
					prevFireTime = trigger.GetPreviousFireTime().Value.Ticks;
				}
				AddCommandParameter(cmd, 6,
				                                                 Decimal.Parse(Convert.ToString(prevFireTime), NumberStyles.Any));
				AddCommandParameter(cmd, 7, state);
				if (trigger is SimpleTrigger)
				{
					//                UpdateSimpleTrigger(conn, (SimpleTrigger)trigger);
					AddCommandParameter(cmd, 8, AdoConstants.TTYPE_SIMPLE);
				}
				else if (trigger is CronTrigger)
				{
					//                UpdateCronTrigger(conn, (CronTrigger)trigger);
					AddCommandParameter(cmd, 8, AdoConstants.TTYPE_CRON);
				}
				else
				{
					//                UpdateBlobTrigger(conn, trigger);
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
					AddCommandParameter(cmd, 13, SupportClass.ToSByteArray(baos.ToArray()));

					AddCommandParameter(cmd, 14, trigger.Name);
					AddCommandParameter(cmd, 15, trigger.Group);
				}
				else
				{
					AddCommandParameter(cmd, 13, trigger.Name);
					AddCommandParameter(cmd, 14, trigger.Group);
				}

				insertResult = cmd.ExecuteNonQuery();
			}
			finally
			{
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}

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
		public virtual int UpdateSimpleTrigger(IDbConnection legacy, SimpleTrigger trigger)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
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
		}

		/// <summary>
		/// Update the cron trigger data.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <param name="trigger">The trigger to insert.</param>
		/// <returns>The number of rows updated.</returns>
		public virtual int UpdateCronTrigger(IDbConnection legacy, CronTrigger trigger)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
			{
				using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_CRON_TRIGGER)))
				{
					AddCommandParameter(cmd, 1, trigger.CronExpression);
					AddCommandParameter(cmd, 2, trigger.Name);
					AddCommandParameter(cmd, 3, trigger.Group);

					return cmd.ExecuteNonQuery();
				}
			}
		}

		/// <summary>
		/// Update the blob trigger data.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <param name="trigger">The trigger to insert.</param>
		/// <returns>The number of rows updated.</returns>
		public virtual int UpdateBlobTrigger(IDbConnection legacy, Trigger trigger)
		{
			OleDbCommand ps = null;
			MemoryStream os = null;

			try
			{
				// update the blob
				os = new MemoryStream();
				//UPGRADE_TODO: Class 'java.io.ObjectOutputStream' was converted to 'System.IO.BinaryWriter' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javaioObjectOutputStream_3"'
				BinaryWriter oos = new BinaryWriter(os);
				//UPGRADE_TODO: Method 'java.io.ObjectOutputStream.writeObject' was converted to 'SupportClass.Serialize' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javaioObjectOutputStreamwriteObject_javalangObject_3"'
				SupportClass.Serialize(oos, trigger);
				oos.Close();

				sbyte[] buf = SupportClass.ToSByteArray(os.ToArray());
				MemoryStream is_Renamed = new MemoryStream(SupportClass.ToByteArray(buf));

				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, ReplaceTablePrefix(UPDATE_BLOB_TRIGGER));
				AddCommandParameter(cmd, 1, is_Renamed);
				AddCommandParameter(cmd, 2, trigger.Name);
				AddCommandParameter(cmd, 3, trigger.Group);

				return cmd.ExecuteNonQuery();
			}
			finally
			{
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
				if (os != null)
				{
					os.Close();
				}
			}
		}

		/// <summary>
		/// Check whether or not a trigger exists.
		/// </summary>
		/// <param name="legacy">The DB Connection.</param>
		/// <param name="triggerName">The name of the trigger.</param>
		/// <param name="groupName">The group containing the trigger.</param>
		/// <returns>true if the trigger exists, false otherwise</returns>
		public virtual bool TriggerExists(IDbConnection legacy, string triggerName, string groupName)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
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
		}

		/// <summary>
		/// Update the state for a given trigger.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <param name="triggerName">The name of the trigger.</param>
		/// <param name="groupName">The group containing the trigger.</param>
		/// <param name="state">The new state for the trigger.</param>
		/// <returns>The number of rows updated.</returns>
		public virtual int UpdateTriggerState(IDbConnection legacy, string triggerName, string groupName, string state)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
			{
				using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_TRIGGER_STATE)))
				{
					AddCommandParameter(cmd, 1, state);
					AddCommandParameter(cmd, 2, triggerName);
					AddCommandParameter(cmd, 3, groupName);

					return cmd.ExecuteNonQuery();
				}
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
		public virtual int UpdateTriggerStateFromOtherStates(IDbConnection legacy, string triggerName, string groupName,
		                                                     string newState, string oldState1, string oldState2,
		                                                     string oldState3)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
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
		}


		public virtual int UpdateTriggerStateFromOtherStatesBeforeTime(IDbConnection legacy, string newState, string oldState1,
		                                                               string oldState2, long time)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
			{
				using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_TRIGGER_STATE_FROM_OTHER_STATES_BEFORE_TIME)))
				{

					AddCommandParameter(cmd, 1, newState);
					AddCommandParameter(cmd, 2, oldState1);
					AddCommandParameter(cmd, 3, oldState2);
					AddCommandParameter(cmd, 4, time);

					return cmd.ExecuteNonQuery();
				}
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
		public virtual int UpdateTriggerGroupStateFromOtherStates(IDbConnection legacy, string groupName, string newState,
		                                                          string oldState1, string oldState2, string oldState3)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
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
		}

		/// <summary>
		/// Update the given trigger to the given new state, if it is in the given
		/// old state.
		/// </summary>
		/// <param name="conn">
		/// the DB connection
		/// </param>
		/// <param name="">triggerName
		/// the name of the trigger
		/// </param>
		/// <param name="">groupName
		/// the group containing the trigger
		/// </param>
		/// <param name="">newState
		/// the new state for the trigger
		/// </param>
		/// <param name="">oldState
		/// the old state the trigger must be in
		/// </param>
		/// <returns> int the number of rows updated
		/// </returns>
		/// <throws>  SQLException </throws>
		public virtual int UpdateTriggerStateFromOtherState(IDbConnection legacy, string triggerName, string groupName,
		                                                    string newState, string oldState)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
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
			
		}

		/// <summary> <p>
		/// Update all of the triggers of the given group to the given new state, if
		/// they are in the given old state.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB connection
		/// </param>
		/// <param name="">groupName
		/// the group containing the triggers
		/// </param>
		/// <param name="">newState
		/// the new state for the trigger group
		/// </param>
		/// <param name="">oldState
		/// the old state the triggers must be in
		/// </param>
		/// <returns> int the number of rows updated
		/// </returns>
		/// <throws>  SQLException </throws>
		public virtual int UpdateTriggerGroupStateFromOtherState(IDbConnection legacy, string groupName, string newState,
		                                                         string oldState)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
			{
				using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_TRIGGER_GROUP_STATE_FROM_STATE)))
				{
					AddCommandParameter(cmd, 1, newState);
					AddCommandParameter(cmd, 2, groupName);
					AddCommandParameter(cmd, 3, oldState);

					return cmd.ExecuteNonQuery();
				}
			}
		}

		/// <summary> <p>
		/// Update the states of all triggers associated with the given job.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <param name="">jobName
		/// the name of the job
		/// </param>
		/// <param name="">groupName
		/// the group containing the job
		/// </param>
		/// <param name="">state
		/// the new state for the triggers
		/// </param>
		/// <returns> the number of rows updated
		/// </returns>
		public virtual int UpdateTriggerStatesForJob(IDbConnection legacy, string jobName, string groupName, string state)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
			{
				using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_JOB_TRIGGER_STATES)))
				{
					AddCommandParameter(cmd, 1, state);
					AddCommandParameter(cmd, 2, jobName);
					AddCommandParameter(cmd, 3, groupName);

					return cmd.ExecuteNonQuery();
				}
			}
		}


		public virtual int UpdateTriggerStatesForJobFromOtherState(IDbConnection legacy, string jobName, string groupName,
		                                                           string state, string oldState)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
			{
				using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(UPDATE_JOB_TRIGGER_STATES_FROM_OTHER_STATE)))
				{
					AddCommandParameter(cmd, 1, state);
					AddCommandParameter(cmd, 2, jobName);
					AddCommandParameter(cmd, 3, groupName);
					AddCommandParameter(cmd, 4, oldState);

					return cmd.ExecuteNonQuery();
				}
			}
		}

		/// <summary> <p>
		/// Delete all of the listeners associated with a given trigger.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <param name="">triggerName
		/// the name of the trigger whose listeners will be deleted
		/// </param>
		/// <param name="">groupName
		/// the name of the group containing the trigger
		/// </param>
		/// <returns> the number of rows deleted
		/// </returns>
		public virtual int DeleteTriggerListeners(IDbConnection legacy, string triggerName, string groupName)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
			{
				using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(DELETE_TRIGGER_LISTENERS)))
				{
					
					AddCommandParameter(cmd, 1, triggerName);
					AddCommandParameter(cmd, 2, groupName);
					return cmd.ExecuteNonQuery();
				}
			}
		}

		/// <summary> <p>
		/// Associate a listener with the given trigger.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <param name="">trigger
		/// the trigger
		/// </param>
		/// <param name="">listener
		/// the name of the listener to associate with the trigger
		/// </param>
		/// <returns> the number of rows inserted
		/// </returns>
		public virtual int InsertTriggerListener(IDbConnection legacy, Trigger trigger, string listener)
		{
			OleDbCommand ps = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, ReplaceTablePrefix(INSERT_TRIGGER_LISTENER));
				AddCommandParameter(cmd, 1, trigger.Name);
				AddCommandParameter(cmd, 2, trigger.Group);
				AddCommandParameter(cmd, 3, listener);

				return cmd.ExecuteNonQuery();
			}
			finally
			{
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}

		/// <summary> <p>
		/// Select the listeners associated with a given trigger.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <param name="">triggerName
		/// the name of the trigger
		/// </param>
		/// <param name="">groupName
		/// the group containing the trigger
		/// </param>
		/// <returns> array of <code>String</code> trigger listener names
		/// </returns>
		public virtual String[] SelectTriggerListeners(IDbConnection legacy, string triggerName, string groupName)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         ReplaceTablePrefix(SELECT_TRIGGER_LISTENERS));
				AddCommandParameter(cmd, 1, triggerName);
				AddCommandParameter(cmd, 2, groupName);
				rs = ps.ExecuteReader();

				ArrayList list = new ArrayList();
				while (rs.Read())
				{
					list.Add(Convert.ToString(rs[1 - 1]));
				}
				Object[] oArr = list.ToArray();
				String[] sArr = new String[oArr.Length];
				Array.Copy(oArr, 0, sArr, 0, oArr.Length);
				return sArr;
			}
			finally
			{
				if (null != rs)
				{
					try
					{
						rs.Close();
					}
					catch (OleDbException ignore)
					{
					}
				}
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}

		/// <summary> <p>
		/// Delete the simple trigger data for a trigger.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <param name="">triggerName
		/// the name of the trigger
		/// </param>
		/// <param name="">groupName
		/// the group containing the trigger
		/// </param>
		/// <returns> the number of rows deleted
		/// </returns>
		public virtual int DeleteSimpleTrigger(IDbConnection legacy, string triggerName, string groupName)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
			{
				using (IDbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(DELETE_SIMPLE_TRIGGER)))
				{
					AddCommandParameter(cmd, 1, triggerName);
					AddCommandParameter(cmd, 2, groupName);

					return cmd.ExecuteNonQuery();
				}
			}
		}

		/// <summary> <p>
		/// Delete the cron trigger data for a trigger.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <param name="">triggerName
		/// the name of the trigger
		/// </param>
		/// <param name="">groupName
		/// the group containing the trigger
		/// </param>
		/// <returns> the number of rows deleted
		/// </returns>
		public virtual int DeleteCronTrigger(IDbConnection legacy, string triggerName, string groupName)
		{
			OleDbCommand ps = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, ReplaceTablePrefix(DELETE_CRON_TRIGGER));
				AddCommandParameter(cmd, 1, triggerName);
				AddCommandParameter(cmd, 2, groupName);

				return cmd.ExecuteNonQuery();
			}
			finally
			{
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}

		/// <summary> <p>
		/// Delete the cron trigger data for a trigger.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <param name="">triggerName
		/// the name of the trigger
		/// </param>
		/// <param name="">groupName
		/// the group containing the trigger
		/// </param>
		/// <returns> the number of rows deleted
		/// </returns>
		public virtual int DeleteBlobTrigger(IDbConnection legacy, string triggerName, string groupName)
		{
			OleDbCommand ps = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, ReplaceTablePrefix(DELETE_BLOB_TRIGGER));
				AddCommandParameter(cmd, 1, triggerName);
				AddCommandParameter(cmd, 2, groupName);

				return cmd.ExecuteNonQuery();
			}
			finally
			{
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}

		/// <summary> <p>
		/// Delete the base trigger data for a trigger.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <param name="">triggerName
		/// the name of the trigger
		/// </param>
		/// <param name="">groupName
		/// the group containing the trigger
		/// </param>
		/// <returns> the number of rows deleted
		/// </returns>
		public virtual int DeleteTrigger(IDbConnection legacy, string triggerName, string groupName)
		{
			OleDbCommand ps = null;

			try
			{
				ps = SupportClass.TransactionManager.manager.PrepareStatement(conn, ReplaceTablePrefix(DELETE_TRIGGER));
				AddCommandParameter(cmd, 1, triggerName);
				AddCommandParameter(cmd, 2, groupName);

				return cmd.ExecuteNonQuery();
			}
			finally
			{
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}

		/// <summary> <p>
		/// Select the number of triggers associated with a given job.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <param name="">jobName
		/// the name of the job
		/// </param>
		/// <param name="">groupName
		/// the group containing the job
		/// </param>
		/// <returns> the number of triggers for the given job
		/// </returns>
		public virtual int SelectNumTriggersForJob(IDbConnection legacy, string jobName, string groupName)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         ReplaceTablePrefix(SELECT_NUM_TRIGGERS_FOR_JOB));
				AddCommandParameter(cmd, 1, jobName);
				AddCommandParameter(cmd, 2, groupName);
				rs = ps.ExecuteReader();

				if (rs.Read())
				{
					return rs.GetInt32(1 - 1);
				}
				else
				{
					return 0;
				}
			}
			finally
			{
				if (null != rs)
				{
					try
					{
						rs.Close();
					}
					catch (OleDbException ignore)
					{
					}
				}
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}

		/// <summary> <p>
		/// Select the job to which the trigger is associated.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <param name="">triggerName
		/// the name of the trigger
		/// </param>
		/// <param name="">groupName
		/// the group containing the trigger
		/// </param>
		/// <returns> the <code>{@link org.quartz.JobDetail}</code> object
		/// associated with the given trigger
		/// </returns>
		/// <throws>  SQLException </throws>
		/// <throws>  ClassNotFoundException </throws>
		public virtual JobDetail SelectJobForTrigger(IDbConnection legacy, string triggerName, string groupName,
		                                             IClassLoadHelper loadHelper)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
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
							job.JobClass = loadHelper.LoadClass(Convert.ToString(rs[4 - 1]));
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
		}

		/// <summary> <p>
		/// Select the triggers for a job
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <param name="">jobName
		/// the name of the trigger
		/// </param>
		/// <param name="">groupName
		/// the group containing the trigger
		/// </param>
		/// <returns> an array of <code>(@link org.quartz.Trigger)</code> objects
		/// associated with a given job.
		/// </returns>
		/// <throws>  SQLException </throws>
		public virtual Trigger[] SelectTriggersForJob(IDbConnection legacy, string jobName, string groupName)
		{
			ArrayList trigList = new ArrayList();
			using (IDbConnection conn = dbProvider.CreateConnection())
			{
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
							if(t != null)
								trigList.Add(t);
						}
					}
				}
			}

			return (Trigger[]) trigList.ToArray(typeof (Trigger));
		}


		public virtual Trigger[] SelectTriggersForCalendar(IDbConnection legacy, string calName)
		{
			ArrayList trigList = new ArrayList();
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         ReplaceTablePrefix(SELECT_TRIGGERS_FOR_CALENDAR));
				AddCommandParameter(cmd, 1, calName);
				rs = ps.ExecuteReader();

				while (rs.Read())
				{
					trigList.Add(
						SelectTrigger(conn, Convert.ToString(rs[AdoConstants.COL_TRIGGER_NAME]),
						              Convert.ToString(rs[AdoConstants.COL_TRIGGER_GROUP])));
				}
			}
			finally
			{
				if (null != rs)
				{
					try
					{
						rs.Close();
					}
					catch (OleDbException ignore)
					{
					}
				}
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}

			return (Trigger[]) trigList.ToArray(typeof (Trigger));
		}


		public virtual IList SelectStatefulJobsOfTriggerGroup(IDbConnection legacy, string groupName)
		{
			ArrayList jobList = new ArrayList();
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         ReplaceTablePrefix(
					                                                         	SELECT_STATEFUL_JOBS_OF_TRIGGER_GROUP));
				AddCommandParameter(cmd, 1, groupName);
				AddCommandParameter(cmd, 2, true);
				rs = ps.ExecuteReader();

				while (rs.Read())
				{
					jobList.Add(
						new Key(Convert.ToString(rs[AdoConstants.COL_JOB_NAME]), Convert.ToString(rs[AdoConstants.COL_JOB_GROUP])));
				}
			}
			finally
			{
				if (null != rs)
				{
					try
					{
						rs.Close();
					}
					catch (OleDbException ignore)
					{
					}
				}
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}

			return jobList;
		}

		/// <summary> <p>
		/// Select a trigger.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <param name="">triggerName
		/// the name of the trigger
		/// </param>
		/// <param name="">groupName
		/// the group containing the trigger
		/// </param>
		/// <returns> the <code>{@link org.quartz.Trigger}</code> object
		/// </returns>
		public virtual Trigger SelectTrigger(IDbConnection legacy, string triggerName, string groupName)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				Trigger trigger = null;

				ps = SupportClass.TransactionManager.manager.PrepareStatement(conn, ReplaceTablePrefix(SELECT_TRIGGER));
				AddCommandParameter(cmd, 1, triggerName);
				AddCommandParameter(cmd, 2, groupName);
				rs = ps.ExecuteReader();

				if (rs.Read())
				{
					String jobName = Convert.ToString(rs[AdoConstants.COL_JOB_NAME]);
					String jobGroup = Convert.ToString(rs[AdoConstants.COL_JOB_GROUP]);
					bool volatility = Convert.ToBoolean(rs[AdoConstants.COL_IS_VOLATILE]);
					String description = Convert.ToString(rs[AdoConstants.COL_DESCRIPTION]);
					long nextFireTime = Convert.ToInt64(rs[AdoConstants.COL_NEXT_FIRE_TIME]);
					long prevFireTime = Convert.ToInt64(rs[AdoConstants.COL_PREV_FIRE_TIME]);
					String triggerType = Convert.ToString(rs[AdoConstants.COL_TRIGGER_TYPE]);
					long startTime = Convert.ToInt64(rs[AdoConstants.COL_START_TIME]);
					long endTime = Convert.ToInt64(rs[AdoConstants.COL_END_TIME]);
					String calendarName = Convert.ToString(rs[AdoConstants.COL_CALENDAR_NAME]);
					int misFireInstr = Convert.ToInt32(rs[AdoConstants.COL_MISFIRE_INSTRUCTION]);

					IDictionary map = null;
					if (CanUseProperties())
					{
						map = GetMapFromProperties(rs);
					}
					else
					{
						map = (IDictionary) GetObjectFromBlob(rs, AdoConstants.COL_JOB_DATAMAP);
					}

					//UPGRADE_TODO: The 'DateTime' structure does not have an equivalent to NULL. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1291_3"'
					NullableDateTime nft = null;
					if (nextFireTime > 0)
					{
						//UPGRADE_TODO: Constructor 'java.util.Date.Date' was converted to 'DateTime.DateTime' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilDateDate_long_3"'
						nft = new DateTime(nextFireTime);
					}
					//UPGRADE_TODO: The 'DateTime' structure does not have an equivalent to NULL. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1291_3"'
					NullableDateTime pft = null;
					if (prevFireTime > 0)
					{
						//UPGRADE_TODO: Constructor 'java.util.Date.Date' was converted to 'DateTime.DateTime' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilDateDate_long_3"'
						pft = new DateTime(prevFireTime);
					}
					//UPGRADE_TODO: Constructor 'java.util.Date.Date' was converted to 'DateTime.DateTime' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilDateDate_long_3"'
					DateTime startTimeD = new DateTime(startTime);
					//UPGRADE_TODO: The 'DateTime' structure does not have an equivalent to NULL. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1291_3"'
					NullableDateTime endTimeD = null;
					if (endTime > 0)
					{
						//UPGRADE_TODO: Constructor 'java.util.Date.Date' was converted to 'DateTime.DateTime' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilDateDate_long_3"'
						endTimeD = new DateTime(endTime);
					}

					rs.Close();
					//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
					ps.close();

					if (triggerType.Equals(AdoConstants.TTYPE_SIMPLE))
					{
						ps =
							SupportClass.TransactionManager.manager.PrepareStatement(conn, ReplaceTablePrefix(SELECT_SIMPLE_TRIGGER));
						AddCommandParameter(cmd, 1, triggerName);
						AddCommandParameter(cmd, 2, groupName);
						rs = ps.ExecuteReader();

						if (rs.Read())
						{
							int repeatCount = Convert.ToInt32(rs[AdoConstants.COL_REPEAT_COUNT]);
							long repeatInterval = Convert.ToInt64(rs[AdoConstants.COL_REPEAT_INTERVAL]);
							int timesTriggered = Convert.ToInt32(rs[AdoConstants.COL_TIMES_TRIGGERED]);

							//UPGRADE_NOTE: ref keyword was added to struct-type parameters. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1303_3"'
							SimpleTrigger st =
								new SimpleTrigger(triggerName, groupName, jobName, jobGroup, startTimeD, endTimeD, repeatCount,
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
						ps =
							SupportClass.TransactionManager.manager.PrepareStatement(conn, ReplaceTablePrefix(SELECT_CRON_TRIGGER));
						AddCommandParameter(cmd, 1, triggerName);
						AddCommandParameter(cmd, 2, groupName);
						rs = ps.ExecuteReader();

						if (rs.Read())
						{
							String cronExpr = Convert.ToString(rs[AdoConstants.COL_CRON_EXPRESSION]);
							String timeZoneId = Convert.ToString(rs[AdoConstants.COL_TIME_ZONE_ID]);

							CronTrigger ct = null;
							try
							{
								TimeZone timeZone = null;
								if (timeZoneId != null)
								{
									//UPGRADE_ISSUE: Method 'java.util.TimeZone.getTimeZone' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javautilTimeZonegetTimeZone_javalangString_3"'
									timeZone = TimeZone.getTimeZone(timeZoneId);
								}
								//UPGRADE_NOTE: ref keyword was added to struct-type parameters. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1303_3"'
								ct =
									new CronTrigger(triggerName, groupName, jobName, jobGroup, startTimeD, endTimeD, cronExpr, timeZone);
							}
							catch (Exception neverHappens)
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
						ps =
							SupportClass.TransactionManager.manager.PrepareStatement(conn, ReplaceTablePrefix(SELECT_BLOB_TRIGGER));
						AddCommandParameter(cmd, 1, triggerName);
						AddCommandParameter(cmd, 2, groupName);
						rs = ps.ExecuteReader();

						if (rs.Read())
						{
							trigger = (Trigger) GetObjectFromBlob(rs, AdoConstants.COL_BLOB);
						}
					}
					else
					{
						//UPGRADE_NOTE: Exception 'java.lang.ClassNotFoundException' was converted to 'System.Exception' which has different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1100_3"'
						throw new Exception("class for trigger type '" + triggerType + "' not found.");
					}
				}

				return trigger;
			}
			finally
			{
				if (null != rs)
				{
					try
					{
						rs.Close();
					}
					catch (OleDbException ignore)
					{
					}
				}
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}

		/// <summary> <p>
		/// Select a trigger's JobDataMap.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <param name="">triggerName
		/// the name of the trigger
		/// </param>
		/// <param name="">groupName
		/// the group containing the trigger
		/// </param>
		/// <returns> the <code>{@link org.quartz.JobDataMap}</code> of the Trigger,
		/// never null, but possibly empty.
		/// </returns>
		public virtual JobDataMap SelectTriggerJobDataMap(IDbConnection legacy, string triggerName, string groupName)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				Trigger trigger = null;

				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, ReplaceTablePrefix(SELECT_TRIGGER_DATA));
				AddCommandParameter(cmd, 1, triggerName);
				AddCommandParameter(cmd, 2, groupName);
				rs = ps.ExecuteReader();

				if (rs.Read())
				{
					IDictionary map = null;
					if (CanUseProperties())
					{
						map = GetMapFromProperties(rs);
					}
					else
					{
						map = (IDictionary) GetObjectFromBlob(rs, AdoConstants.COL_JOB_DATAMAP);
					}

					rs.Close();
					//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
					ps.close();

					if (null != map)
					{
						return new JobDataMap(map);
					}
				}
			}
			finally
			{
				if (null != rs)
				{
					try
					{
						rs.Close();
					}
					catch (OleDbException ignore)
					{
					}
				}
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}

			return new JobDataMap();
		}


		/// <summary> <p>
		/// Select a trigger' state value.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <param name="">triggerName
		/// the name of the trigger
		/// </param>
		/// <param name="">groupName
		/// the group containing the trigger
		/// </param>
		/// <returns> the <code>{@link org.quartz.Trigger}</code> object
		/// </returns>
		public virtual string SelectTriggerState(IDbConnection legacy, string triggerName, string groupName)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				String state = null;

				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, ReplaceTablePrefix(SELECT_TRIGGER_STATE));
				AddCommandParameter(cmd, 1, triggerName);
				AddCommandParameter(cmd, 2, groupName);
				rs = ps.ExecuteReader();

				if (rs.Read())
				{
					state = Convert.ToString(rs[AdoConstants.COL_TRIGGER_STATE]);
				}
				else
				{
					state = AdoConstants.STATE_DELETED;
				}

				return String.Intern(state);
			}
			finally
			{
				if (null != rs)
				{
					try
					{
						rs.Close();
					}
					catch (OleDbException ignore)
					{
					}
				}
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}

		/// <summary> <p>
		/// Select a trigger' status (state & next fire time).
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <param name="">triggerName
		/// the name of the trigger
		/// </param>
		/// <param name="">groupName
		/// the group containing the trigger
		/// </param>
		/// <returns> a <code>TriggerStatus</code> object, or null
		/// </returns>
		public virtual TriggerStatus SelectTriggerStatus(IDbConnection legacy, string triggerName, string groupName)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				TriggerStatus status = null;

				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, ReplaceTablePrefix(SELECT_TRIGGER_STATUS));
				AddCommandParameter(cmd, 1, triggerName);
				AddCommandParameter(cmd, 2, groupName);
				rs = ps.ExecuteReader();

				if (rs.Read())
				{
					String state = Convert.ToString(rs[AdoConstants.COL_TRIGGER_STATE]);
					long nextFireTime = Convert.ToInt64(rs[AdoConstants.COL_NEXT_FIRE_TIME]);
					String jobName = Convert.ToString(rs[AdoConstants.COL_JOB_NAME]);
					String jobGroup = Convert.ToString(rs[AdoConstants.COL_JOB_GROUP]);

					//UPGRADE_TODO: The 'DateTime' structure does not have an equivalent to NULL. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1291_3"'
					NullableDateTime nft = null;
					if (nextFireTime > 0)
					{
						//UPGRADE_TODO: Constructor 'java.util.Date.Date' was converted to 'DateTime.DateTime' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilDateDate_long_3"'
						nft = new DateTime(nextFireTime);
					}

					status = new TriggerStatus(state, nft);
					status.Key = new Key(triggerName, groupName);
					status.JobKey = new Key(jobName, jobGroup);
				}

				return status;
			}
			finally
			{
				if (null != rs)
				{
					try
					{
						rs.Close();
					}
					catch (OleDbException ignore)
					{
					}
				}
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}

		/// <summary> <p>
		/// Select the total number of triggers stored.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <returns> the total number of triggers stored
		/// </returns>
		public virtual int SelectNumTriggers(IDbConnection conn)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				int count = 0;
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, ReplaceTablePrefix(SELECT_NUM_TRIGGERS));
				rs = ps.ExecuteReader();

				if (rs.Read())
				{
					count = rs.GetInt32(1 - 1);
				}

				return count;
			}
			finally
			{
				if (null != rs)
				{
					try
					{
						rs.Close();
					}
					catch (OleDbException ignore)
					{
					}
				}
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}

		/// <summary> <p>
		/// Select all of the trigger group names that are stored.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <returns> an array of <code>String</code> group names
		/// </returns>
		public virtual String[] SelectTriggerGroups(IDbConnection conn)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, ReplaceTablePrefix(SELECT_TRIGGER_GROUPS));
				rs = ps.ExecuteReader();

				ArrayList list = new ArrayList();
				while (rs.Read())
				{
					list.Add(Convert.ToString(rs[1 - 1]));
				}

				Object[] oArr = list.ToArray();
				String[] sArr = new String[oArr.Length];
				Array.Copy(oArr, 0, sArr, 0, oArr.Length);
				return sArr;
			}
			finally
			{
				if (null != rs)
				{
					try
					{
						rs.Close();
					}
					catch (OleDbException ignore)
					{
					}
				}
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}

		/// <summary> <p>
		/// Select all of the triggers contained in a given group.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <param name="">groupName
		/// the group containing the triggers
		/// </param>
		/// <returns> an array of <code>String</code> trigger names
		/// </returns>
		public virtual string[] SelectTriggersInGroup(IDbConnection legacy, string groupName)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
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

						object[] oArr = list.ToArray();
						string[] sArr = new string[oArr.Length];
						Array.Copy(oArr, 0, sArr, 0, oArr.Length);
						return sArr;
					}
				}
			}
		}


		public virtual int InsertPausedTriggerGroup(IDbConnection legacy, string groupName)
		{
			OleDbCommand ps = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         ReplaceTablePrefix(INSERT_PAUSED_TRIGGER_GROUP));
				AddCommandParameter(cmd, 1, groupName);
				int rows = cmd.ExecuteNonQuery();

				return rows;
			}
			finally
			{
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}


		public virtual int DeletePausedTriggerGroup(IDbConnection legacy, string groupName)
		{
			OleDbCommand ps = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         ReplaceTablePrefix(DELETE_PAUSED_TRIGGER_GROUP));
				AddCommandParameter(cmd, 1, groupName);
				int rows = cmd.ExecuteNonQuery();

				return rows;
			}
			finally
			{
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}


		public virtual int DeleteAllPausedTriggerGroups(IDbConnection conn)
		{
			OleDbCommand ps = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         ReplaceTablePrefix(DELETE_PAUSED_TRIGGER_GROUPS));
				int rows = cmd.ExecuteNonQuery();

				return rows;
			}
			finally
			{
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}


		public virtual bool IsTriggerGroupPaused(IDbConnection legacy, string groupName)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         ReplaceTablePrefix(SELECT_PAUSED_TRIGGER_GROUP));
				AddCommandParameter(cmd, 1, groupName);
				rs = ps.ExecuteReader();

				return rs.Read();
			}
			finally
			{
				if (null != rs)
				{
					try
					{
						rs.Close();
					}
					catch (OleDbException ignore)
					{
					}
				}
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}


		public virtual bool IsExistingTriggerGroup(IDbConnection legacy, string groupName)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         ReplaceTablePrefix(SELECT_NUM_TRIGGERS_IN_GROUP));
				AddCommandParameter(cmd, 1, groupName);
				rs = ps.ExecuteReader();

				if (!rs.Read())
				{
					return false;
				}

				return (rs.GetInt32(1 - 1) > 0);
			}
			finally
			{
				if (null != rs)
				{
					try
					{
						rs.Close();
					}
					catch (OleDbException ignore)
					{
					}
				}
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}

		//---------------------------------------------------------------------------
		// calendars
		//---------------------------------------------------------------------------

		/// <summary> <p>
		/// Insert a new calendar.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <param name="">calendarName
		/// the name for the new calendar
		/// </param>
		/// <param name="">calendar
		/// the calendar
		/// </param>
		/// <returns> the number of rows inserted
		/// </returns>
		/// <throws>  IOException </throws>
		/// <summary>           if there were problems serializing the calendar
		/// </summary>
		public virtual int InsertCalendar(IDbConnection legacy, string calendarName, ICalendar calendar)
		{
			MemoryStream baos = SerializeObject(calendar);

			OleDbCommand ps = null;

			try
			{
				ps = SupportClass.TransactionManager.manager.PrepareStatement(conn, ReplaceTablePrefix(INSERT_CALENDAR));
				AddCommandParameter(cmd, 1, calendarName);
				AddCommandParameter(cmd, 2, SupportClass.ToSByteArray(baos.ToArray()));

				return cmd.ExecuteNonQuery();
			}
			finally
			{
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}

		/// <summary> <p>
		/// Update a calendar.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <param name="">calendarName
		/// the name for the new calendar
		/// </param>
		/// <param name="">calendar
		/// the calendar
		/// </param>
		/// <returns> the number of rows updated
		/// </returns>
		/// <throws>  IOException </throws>
		/// <summary>           if there were problems serializing the calendar
		/// </summary>
		public virtual int UpdateCalendar(IDbConnection legacy, string calendarName, ICalendar calendar)
		{
			MemoryStream baos = SerializeObject(calendar);

			OleDbCommand ps = null;

			try
			{
				ps = SupportClass.TransactionManager.manager.PrepareStatement(conn, ReplaceTablePrefix(UPDATE_CALENDAR));
				AddCommandParameter(cmd, 1, SupportClass.ToSByteArray(baos.ToArray()));
				AddCommandParameter(cmd, 2, calendarName);

				return cmd.ExecuteNonQuery();
			}
			finally
			{
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}

		/// <summary> <p>
		/// Check whether or not a calendar exists.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <param name="">calendarName
		/// the name of the calendar
		/// </param>
		/// <returns> true if the trigger exists, false otherwise
		/// </returns>
		public virtual bool CalendarExists(IDbConnection legacy, string calendarName)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         ReplaceTablePrefix(SELECT_CALENDAR_EXISTENCE));
				AddCommandParameter(cmd, 1, calendarName);
				rs = ps.ExecuteReader();

				if (rs.Read())
				{
					return true;
				}
				else
				{
					return false;
				}
			}
			finally
			{
				if (null != rs)
				{
					try
					{
						rs.Close();
					}
					catch (OleDbException ignore)
					{
					}
				}
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}

		/// <summary> <p>
		/// Select a calendar.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <param name="">calendarName
		/// the name of the calendar
		/// </param>
		/// <returns> the Calendar
		/// </returns>
		/// <throws>  ClassNotFoundException </throws>
		/// <summary>           if a class found during deserialization cannot be found be
		/// found
		/// </summary>
		/// <throws>  IOException </throws>
		/// <summary>           if there were problems deserializing the calendar
		/// </summary>
		public virtual ICalendar SelectCalendar(IDbConnection legacy, string calendarName)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;
			try
			{
				String selCal = ReplaceTablePrefix(SELECT_CALENDAR);
				ps = SupportClass.TransactionManager.manager.PrepareStatement(conn, selCal);
				AddCommandParameter(cmd, 1, calendarName);
				rs = ps.ExecuteReader();

				ICalendar cal = null;
				if (rs.Read())
				{
					cal = (ICalendar) GetObjectFromBlob(rs, AdoConstants.COL_CALENDAR);
				}
				if (null == cal)
				{
					logger.Warn("Couldn't find calendar with name '" + calendarName + "'.");
				}
				return cal;
			}
			finally
			{
				if (null != rs)
				{
					try
					{
						rs.Close();
					}
					catch (OleDbException ignore)
					{
					}
				}
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}

		/// <summary> <p>
		/// Check whether or not a calendar is referenced by any triggers.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <param name="">calendarName
		/// the name of the calendar
		/// </param>
		/// <returns> true if any triggers reference the calendar, false otherwise
		/// </returns>
		public virtual bool CalendarIsReferenced(IDbConnection legacy, string calendarName)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;
			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         ReplaceTablePrefix(SELECT_REFERENCED_CALENDAR));
				AddCommandParameter(cmd, 1, calendarName);
				rs = ps.ExecuteReader();

				if (rs.Read())
				{
					return true;
				}
				else
				{
					return false;
				}
			}
			finally
			{
				if (null != rs)
				{
					try
					{
						rs.Close();
					}
					catch (OleDbException ignore)
					{
					}
				}
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}

		/// <summary> <p>
		/// Delete a calendar.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <param name="">calendarName
		/// the name of the trigger
		/// </param>
		/// <returns> the number of rows deleted
		/// </returns>
		public virtual int DeleteCalendar(IDbConnection legacy, string calendarName)
		{
			OleDbCommand ps = null;

			try
			{
				ps = SupportClass.TransactionManager.manager.PrepareStatement(conn, ReplaceTablePrefix(DELETE_CALENDAR));
				AddCommandParameter(cmd, 1, calendarName);

				return cmd.ExecuteNonQuery();
			}
			finally
			{
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}

		/// <summary> <p>
		/// Select the total number of calendars stored.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <returns> the total number of calendars stored
		/// </returns>
		public virtual int SelectNumCalendars(IDbConnection conn)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				int count = 0;
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, ReplaceTablePrefix(SELECT_NUM_CALENDARS));

				rs = ps.ExecuteReader();

				if (rs.Read())
				{
					count = rs.GetInt32(1 - 1);
				}

				return count;
			}
			finally
			{
				if (null != rs)
				{
					try
					{
						rs.Close();
					}
					catch (OleDbException ignore)
					{
					}
				}
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}

		/// <summary> <p>
		/// Select all of the stored calendars.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <returns> an array of <code>String</code> calendar names
		/// </returns>
		public virtual String[] SelectCalendars(IDbConnection conn)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps = SupportClass.TransactionManager.manager.PrepareStatement(conn, ReplaceTablePrefix(SELECT_CALENDARS));
				rs = ps.ExecuteReader();

				ArrayList list = new ArrayList();
				while (rs.Read())
				{
					list.Add(Convert.ToString(rs[1 - 1]));
				}

				Object[] oArr = list.ToArray();
				String[] sArr = new String[oArr.Length];
				Array.Copy(oArr, 0, sArr, 0, oArr.Length);
				return sArr;
			}
			finally
			{
				if (null != rs)
				{
					try
					{
						rs.Close();
					}
					catch (OleDbException ignore)
					{
					}
				}
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}

		//---------------------------------------------------------------------------
		// trigger firing
		//---------------------------------------------------------------------------

		/// <summary> <p>
		/// Select the next time that a trigger will be fired.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <returns> the next fire time, or 0 if no trigger will be fired
		/// </returns>
		public virtual DateTime SelectNextFireTime(IDbConnection conn)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;
			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, ReplaceTablePrefix(SELECT_NEXT_FIRE_TIME));
				AddCommandParameter(cmd, 1, AdoConstants.STATE_WAITING);
				rs = ps.ExecuteReader();

				if (rs.Read())
				{
					return Convert.ToInt64(rs[AdoConstants.ALIAS_COL_NEXT_FIRE_TIME]);
				}
				else
				{
					return 0L;
				}
			}
			finally
			{
				if (null != rs)
				{
					try
					{
						rs.Close();
					}
					catch (OleDbException ignore)
					{
					}
				}
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}

		/// <summary> <p>
		/// Select the trigger that will be fired at the given fire time.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <param name="">fireTime
		/// the time that the trigger will be fired
		/// </param>
		/// <returns> a <code>{@link org.quartz.utils.Key}</code> representing the
		/// trigger that will be fired at the given fire time, or null if no
		/// trigger will be fired at that time
		/// </returns>
		public virtual Key SelectTriggerForFireTime(IDbConnection legacy, DateTime fireTime)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;
			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         ReplaceTablePrefix(SELECT_TRIGGER_FOR_FIRE_TIME));
				AddCommandParameter(cmd, 1, AdoConstants.STATE_WAITING);
				AddCommandParameter(cmd, 2, Decimal.Parse(Convert.ToString(fireTime), NumberStyles.Any));
				rs = ps.ExecuteReader();

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
			finally
			{
				if (null != rs)
				{
					try
					{
						rs.Close();
					}
					catch (OleDbException ignore)
					{
					}
				}
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException)
					{
					}
				}
			}
		}

		/// <summary> <p>
		/// Insert a fired trigger.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <param name="">trigger
		/// the trigger
		/// </param>
		/// <param name="">state
		/// the state that the trigger should be stored in
		/// </param>
		/// <returns> the number of rows inserted
		/// </returns>
		public virtual int InsertFiredTrigger(IDbConnection legacy, Trigger trigger, string state, JobDetail job)
		{
			OleDbCommand ps = null;
			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, ReplaceTablePrefix(INSERT_FIRED_TRIGGER));
				AddCommandParameter(cmd, 1, trigger.FireInstanceId);
				AddCommandParameter(cmd, 2, trigger.Name);
				AddCommandParameter(cmd, 3, trigger.Group);
				AddCommandParameter(cmd, 4, trigger.Volatile);
				AddCommandParameter(cmd, 5, instanceId);
				//UPGRADE_TODO: Method 'java.util.Date.getTime' was converted to 'DateTime.Ticks' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilDategetTime_3"'
				AddCommandParameter(cmd, 6,
				                                                 Decimal.Parse(
				                                                 	Convert.ToString(trigger.GetNextFireTime().Value.Ticks),
				                                                 	NumberStyles.Any));
				AddCommandParameter(cmd, 7, state);
				if (job != null)
				{
					AddCommandParameter(cmd, 8, trigger.JobName);
					AddCommandParameter(cmd, 9, trigger.JobGroup);
					AddCommandParameter(cmd, 10, job.Stateful);
					AddCommandParameter(cmd, 11, job.requestsRecovery());
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
			finally
			{
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}

		/// <summary> <p>
		/// Select the states of all fired-trigger records for a given trigger, or
		/// trigger group if trigger name is <code>null</code>.
		/// </p>
		/// 
		/// </summary>
		/// <returns> a List of FiredTriggerRecord objects.
		/// </returns>
		public virtual IList SelectFiredTriggerRecords(IDbConnection legacy, string triggerName, string groupName)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;
			try
			{
				//UPGRADE_TODO: Class 'java.util.LinkedList' was converted to 'System.Collections.ArrayList' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilLinkedList_3"'
				IList lst = new ArrayList();

				if (triggerName != null)
				{
					ps =
						SupportClass.TransactionManager.manager.PrepareStatement(conn, ReplaceTablePrefix(SELECT_FIRED_TRIGGER));
					AddCommandParameter(cmd, 1, triggerName);
					AddCommandParameter(cmd, 2, groupName);
				}
				else
				{
					ps =
						SupportClass.TransactionManager.manager.PrepareStatement(conn,
						                                                         ReplaceTablePrefix(SELECT_FIRED_TRIGGER_GROUP));
					AddCommandParameter(cmd, 1, groupName);
				}
				rs = ps.ExecuteReader();

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
							new Key(Convert.ToString(rs[AdoConstants.COL_JOB_NAME]), Convert.ToString(rs[AdoConstants.COL_JOB_GROUP]));
					}
					lst.Add(rec);
				}

				return lst;
			}
			finally
			{
				if (null != rs)
				{
					try
					{
						rs.Close();
					}
					catch (OleDbException ignore)
					{
					}
				}
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}

		/// <summary> <p>
		/// Select the states of all fired-trigger records for a given job, or job
		/// group if job name is <code>null</code>.
		/// </p>
		/// 
		/// </summary>
		/// <returns> a List of FiredTriggerRecord objects.
		/// </returns>
		public virtual IList SelectFiredTriggerRecordsByJob(IDbConnection legacy, string jobName, string groupName)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;
			try
			{
				//UPGRADE_TODO: Class 'java.util.LinkedList' was converted to 'System.Collections.ArrayList' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilLinkedList_3"'
				IList lst = new ArrayList();

				if (jobName != null)
				{
					ps =
						SupportClass.TransactionManager.manager.PrepareStatement(conn,
						                                                         ReplaceTablePrefix(SELECT_FIRED_TRIGGERS_OF_JOB));
					AddCommandParameter(cmd, 1, jobName);
					AddCommandParameter(cmd, 2, groupName);
				}
				else
				{
					ps =
						SupportClass.TransactionManager.manager.PrepareStatement(conn,
						                                                         ReplaceTablePrefix(
						                                                         	SELECT_FIRED_TRIGGERS_OF_JOB_GROUP));
					AddCommandParameter(cmd, 1, groupName);
				}
				rs = ps.ExecuteReader();

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
							new Key(Convert.ToString(rs[AdoConstants.COL_JOB_NAME]), Convert.ToString(rs[AdoConstants.COL_JOB_GROUP]));
					}
					lst.Add(rec);
				}

				return lst;
			}
			finally
			{
				if (null != rs)
				{
					try
					{
						rs.Close();
					}
					catch (OleDbException ignore)
					{
					}
				}
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}


		public virtual IList SelectInstancesFiredTriggerRecords(IDbConnection legacy, string instanceName)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;
			try
			{
				//UPGRADE_TODO: Class 'java.util.LinkedList' was converted to 'System.Collections.ArrayList' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilLinkedList_3"'
				IList lst = new ArrayList();

				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         ReplaceTablePrefix(
					                                                         	SELECT_INSTANCES_FIRED_TRIGGERS));
				AddCommandParameter(cmd, 1, instanceName);
				rs = ps.ExecuteReader();

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
							new Key(Convert.ToString(rs[AdoConstants.COL_JOB_NAME]), Convert.ToString(rs[AdoConstants.COL_JOB_GROUP]));
					}
					lst.Add(rec);
				}

				return lst;
			}
			finally
			{
				if (null != rs)
				{
					try
					{
						rs.Close();
					}
					catch (OleDbException ignore)
					{
					}
				}
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}

		/// <summary> <p>
		/// Delete a fired trigger.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">
		/// the DB Connection
		/// </param>
		/// <param name="">entryId
		/// the fired trigger entry to delete
		/// </param>
		/// <returns> the number of rows deleted
		/// </returns>
		public virtual int DeleteFiredTrigger(IDbConnection legacy, string entryId)
		{
			OleDbCommand ps = null;
			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, ReplaceTablePrefix(DELETE_FIRED_TRIGGER));
				AddCommandParameter(cmd, 1, entryId);

				return cmd.ExecuteNonQuery();
			}
			finally
			{
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}


		public virtual int SelectJobExecutionCount(IDbConnection legacy, string jobName, string jobGroup)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         ReplaceTablePrefix(SELECT_JOB_EXECUTION_COUNT));
				AddCommandParameter(cmd, 1, jobName);
				AddCommandParameter(cmd, 2, jobGroup);

				rs = ps.ExecuteReader();

				if (rs.Read())
				{
					return rs.GetInt32(1 - 1);
				}
				else
				{
					return 0;
				}
			}
			finally
			{
				if (null != rs)
				{
					try
					{
						rs.Close();
					}
					catch (OleDbException ignore)
					{
					}
				}
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}


		public virtual int DeleteVolatileFiredTriggers(IDbConnection conn)
		{
			OleDbCommand ps = null;
			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         ReplaceTablePrefix(DELETE_VOLATILE_FIRED_TRIGGERS));
				AddCommandParameter(cmd, 1, true);

				return cmd.ExecuteNonQuery();
			}
			finally
			{
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}


		public virtual int InsertSchedulerState(IDbConnection legacy, string instanceId, long checkInTime, long interval,
		                                        string recoverer)
		{
			OleDbCommand ps = null;
			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, ReplaceTablePrefix(INSERT_SCHEDULER_STATE));
				AddCommandParameter(cmd, 1, instanceId);
				AddCommandParameter(cmd, 2, checkInTime);
				AddCommandParameter(cmd, 3, interval);
				AddCommandParameter(cmd, 4, recoverer);

				return cmd.ExecuteNonQuery();
			}
			finally
			{
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}


		public virtual int DeleteSchedulerState(IDbConnection legacy, string instanceId)
		{
			OleDbCommand ps = null;
			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, ReplaceTablePrefix(DELETE_SCHEDULER_STATE));
				AddCommandParameter(cmd, 1, instanceId);

				return cmd.ExecuteNonQuery();
			}
			finally
			{
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}


		public virtual int UpdateSchedulerState(IDbConnection legacy, string instanceId, long checkInTime, string recoverer)
		{
			IDbCommand cmd = PrepareCommand(legacy, ReplaceTablePrefix(UPDATE_SCHEDULER_STATE));
			try
			{
				AddCommandParameter(cmd, 1, checkInTime);
				AddCommandParameter(cmd, 2, recoverer);
				AddCommandParameter(cmd, 3, instanceId);

				return cmd.ExecuteNonQuery();
			}
			finally
			{
				if (null != cmd)
				{
					try
					{
						cmd.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}


		public virtual IList SelectSchedulerStateRecords(IDbConnection legacy, string instanceId)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;
			try
			{
				//UPGRADE_TODO: Class 'java.util.LinkedList' was converted to 'System.Collections.ArrayList' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilLinkedList_3"'
				IList lst = new ArrayList();

				if (instanceId != null)
				{
					ps =
						SupportClass.TransactionManager.manager.PrepareStatement(conn, ReplaceTablePrefix(SELECT_SCHEDULER_STATE));
					AddCommandParameter(cmd, 1, instanceId);
				}
				else
				{
					ps =
						SupportClass.TransactionManager.manager.PrepareStatement(conn,
						                                                         ReplaceTablePrefix(SELECT_SCHEDULER_STATES));
				}
				rs = ps.ExecuteReader();

				while (rs.Read())
				{
					SchedulerStateRecord rec = new SchedulerStateRecord();

					rec.SchedulerInstanceId = Convert.ToString(rs[AdoConstants.COL_INSTANCE_NAME]);
					rec.CheckinTimestamp = Convert.ToInt64(rs[AdoConstants.COL_LAST_CHECKIN_TIME]);
					rec.CheckinInterval = Convert.ToInt64(rs[AdoConstants.COL_CHECKIN_INTERVAL]);
					rec.Recoverer = Convert.ToString(rs[AdoConstants.COL_RECOVERER]);

					lst.Add(rec);
				}

				return lst;
			}
			finally
			{
				if (null != rs)
				{
					try
					{
						rs.Close();
					}
					catch (OleDbException ignore)
					{
					}
				}
				if (null != ps)
				{
					try
					{
						//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
						ps.close();
					}
					catch (OleDbException ignore)
					{
					}
				}
			}
		}

		//---------------------------------------------------------------------------
		// protected methods that can be overridden by subclasses
		//---------------------------------------------------------------------------

		/// <summary> <p>
		/// Replace the table prefix in a query by replacing any occurrences of
		/// "{0}" with the table prefix.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">query
		/// the unsubstitued query
		/// </param>
		/// <returns> the query, with proper table prefix substituted
		/// </returns>
		protected internal string ReplaceTablePrefix(String query)
		{
			return Util.ReplaceTablePrefix(query, tablePrefix);
		}

		/// <summary> <p>
		/// Create a serialized <code>java.util.ByteArrayOutputStream</code>
		/// version of an Object.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">obj
		/// the object to serialize
		/// </param>
		/// <returns> the serialized ByteArrayOutputStream
		/// </returns>
		/// <throws>  IOException </throws>
		/// <summary>           if serialization causes an error
		/// </summary>
		protected internal virtual MemoryStream SerializeObject(Object obj)
		{
			MemoryStream baos = new MemoryStream();
			if (null != obj)
			{
				//UPGRADE_TODO: Class 'java.io.ObjectOutputStream' was converted to 'System.IO.BinaryWriter' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javaioObjectOutputStream_3"'
				BinaryWriter out_Renamed = new BinaryWriter(baos);
				//UPGRADE_TODO: Method 'java.io.ObjectOutputStream.writeObject' was converted to 'SupportClass.Serialize' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javaioObjectOutputStreamwriteObject_javalangObject_3"'
				SupportClass.Serialize(out_Renamed, obj);
				out_Renamed.Flush();
			}
			return baos;
		}

		/// <summary> <p>
		/// Remove the transient data from and then create a serialized <code>java.util.ByteArrayOutputStream</code>
		/// version of a <code>{@link org.quartz.JobDataMap}</code>.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">obj
		/// the object to serialize
		/// </param>
		/// <returns> the serialized ByteArrayOutputStream
		/// </returns>
		/// <throws>  IOException </throws>
		/// <summary>           if serialization causes an error
		/// </summary>
		protected internal virtual MemoryStream SerializeJobData(JobDataMap data)
		{
			if (CanUseProperties())
			{
				return SerializeProperties(data);
			}

			if (null != data)
			{
				data.RemoveTransientData();
				return SerializeObject(data);
			}
			else
			{
				return SerializeObject(null);
			}
		}

		/// <summary> serialize the java.util.Properties</summary>
		private MemoryStream SerializeProperties(JobDataMap data)
		{
			MemoryStream ba = new MemoryStream();
			if (null != data)
			{
				NameValueCollection properties = ConvertToProperty(data.WrappedMap);
				//UPGRADE_ISSUE: Method 'java.util.Properties.store' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javautilPropertiesstore_javaioOutputStream_javalangString_3"'
				properties.store(ba, "");
			}

			return ba;
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
			//UPGRADE_ISSUE: Class hierarchy differences between 'java.util.Properties' and 'System.Collections.Specialized.NameValueCollection' may cause compilation errors. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1186_3"'
			//UPGRADE_TODO: Format of property file may need to be changed. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1089_3"'
			NameValueCollection properties = new NameValueCollection();
			//UPGRADE_TODO: Method 'java.util.Map.keySet' was converted to 'SupportClass.HashSetSupport' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilMapkeySet_3"'
			HashSet keys = new HashSet(data.Keys);
			IEnumerator it = keys.GetEnumerator();
			//UPGRADE_TODO: Method 'java.util.Iterator.hasNext' was converted to 'System.Collections.IEnumerator.MoveNext' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratorhasNext_3"'
			while (it.MoveNext())
			{
				//UPGRADE_TODO: Method 'java.util.Iterator.next' was converted to 'System.Collections.IEnumerator.Current' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratornext_3"'
				Object key = it.Current;
				Object val = data[key];
				if (!(key is String))
				{
					//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Object.toString' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
					throw new IOException("JobDataMap keys/values must be Strings " + "when the 'useProperties' property is set. " +
					                      " offending Key: " + key);
				}
				if (!(val is String))
				{
					//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Object.toString' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
					throw new IOException("JobDataMap values must be Strings " + "when the 'useProperties' property is set. " +
					                      " Key of offending value: " + key);
				}
				if (val == null)
				{
					val = "";
				}
				properties[(String) key] = (String) val;
			}
			return properties;
		}

		/// <summary>
		/// This method should be overridden by any delegate subclasses that need
		/// special handling for BLOBs. The default implementation uses standard
		/// ADO.NET operations.
		/// </summary>
		/// <param name="rs">The data reader, already queued to the correct row.</param>
		/// <param name="colName">The column name for the BLOB.</param>
		/// <returns>The deserialized object from the DataReader BLOB.</returns>
		protected internal virtual object GetObjectFromBlob(IDataReader dr, string colName)
		{
			object obj = null;

			byte[] data = ReadBytesFromBlob(dr, colName);
			if (data != null)
			{
				MemoryStream ms = new MemoryStream(data);
				BinaryFormatter bf = new BinaryFormatter();
				obj = bf.Deserialize(ms);
			}
			return obj;
		}

		protected byte[] ReadBytesFromBlob(IDataReader dr, string name)
		{
			int bufferSize = 1024;
			MemoryStream stream = new MemoryStream();

			// can read the data
			byte[] outbyte = new byte[bufferSize];

			// Reset the starting byte for the new BLOB.
			int startIndex;
			startIndex = 0;

			// Read the bytes into outbyte[] and retain the number of bytes returned.

			int retval; // The bytes returned from GetBytes.
			retval = (int) dr.GetBytes(0, startIndex, outbyte, 0, bufferSize);

			// Continue reading and writing while there are bytes beyond the size of the buffer.
			while (retval == bufferSize)
			{
				stream.Write(outbyte, 0, retval);

				// Reposition the start index to the end of the last buffer and fill the buffer.
				startIndex += bufferSize;
				retval = (int) dr.GetBytes(0, startIndex, outbyte, 0, bufferSize);
			}

			// Write the remaining buffer.
			stream.Write(outbyte, 0, retval);

			return stream.GetBuffer();
		}


		public virtual Key[] SelectVolatileTriggers(IDbConnection legacy)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
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
		}


		public virtual Key[] SelectVolatileJobs(IDbConnection legacy)
		{
			using (IDbConnection conn = dbProvider.CreateConnection())
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
		}

		/// <summary>
		/// This method should be overridden by any delegate subclasses that need
		/// special handling for BLOBs for job details. 
		/// </summary>
		/// <param name="dr">The result set, already queued to the correct row.</param>
		/// <param name="colName">The column name for the BLOB.</param>
		/// <returns>The deserialized Object from the ResultSet BLOB.</returns>
		protected internal virtual object GetJobDetailFromBlob(IDataReader dr, string colName)
		{
			if (CanUseProperties())
			{
				// TODO
				byte[] blobLocator = null; //; SupportClass.(rs, colName);
				if (blobLocator != null)
				{
					//UPGRADE_ISSUE: Method 'java.sql.Blob.getBinaryStream' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlBlobgetBinaryStream_3"'
					Stream binaryInput = blobLocator.getBinaryStream();
					return binaryInput;
				}
				else
				{
					return null;
				}
			}

			return GetObjectFromBlob(rs, colName);
		}

		/// <seealso cref="DriverDelegate.SelectPausedTriggerGroups(IDbConnection)" />
		public virtual ISet SelectPausedTriggerGroups(IDbConnection legacy)
		{
			HashSet retValue = new HashSet();

			using (IDbConnection conn = dbProvider.CreateConnection())
			{
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
		}

		protected virtual IDbCommand PrepareCommand(IDbConnection connection, string commandText)
		{
			IDbCommand cmd = dbProvider.CreateCommand();
			cmd.CommandText = commandText;
			cmd.Connection = connection;
			return cmd;
		}

		protected virtual void AddCommandParameter(IDbCommand cmd, int i, object paramValue)
		{
			IDbDataParameter param = cmd.CreateParameter();
			param.Value = paramValue;
			cmd.Parameters.Add(param);
		}
	}


	// EOF
}