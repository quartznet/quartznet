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
//UPGRADE_TODO: The type 'org.apache.commons.logging.Log' could not be found. If it was not included in the conversion, there may be compiler issues. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1262_3"'
using System;
using System.Collections;
using System.Collections.Specialized;
using System.Configuration;
using System.Data.OleDb;
using System.Globalization;
using System.IO;
using log4net;

namespace Quartz.impl.jdbcjobstore
{
	/// <summary> <p>
	/// This is meant to be an abstract base class for most, if not all, <code>{@link org.quartz.impl.jdbcjobstore.DriverDelegate}</code>
	/// implementations. Subclasses should override only those methods that need
	/// special handling for the DBMS driver in question.
	/// </p>
	/// 
	/// </summary>
	/// <author>  <a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a>
	/// </author>
	/// <author>  James House
	/// </author>
	public class StdJDBCDelegate : DriverDelegate, StdJDBCConstants
	{
		/*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Data members.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

		protected internal ILog logger = null;

		protected internal String tablePrefix = Constants_Fields.DEFAULT_TABLE_PREFIX;

		protected internal String instanceId;

		protected internal bool useProperties;

		/*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Constructors.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

		/// <summary> <p>
		/// Create new StdJDBCDelegate instance.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">logger
		/// the logger to use during execution
		/// </param>
		/// <param name="">tablePrefix
		/// the prefix of all table names
		/// </param>
		public StdJDBCDelegate(ILog logger, String tablePrefix, String instanceId)
		{
			this.logger = logger;
			this.tablePrefix = tablePrefix;
			this.instanceId = instanceId;
		}

		/// <summary> <p>
		/// Create new StdJDBCDelegate instance.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">logger
		/// the logger to use during execution
		/// </param>
		/// <param name="">tablePrefix
		/// the prefix of all table names
		/// </param>
		//UPGRADE_NOTE: ref keyword was added to struct-type parameters. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1303_3"'
		public StdJDBCDelegate(ILog logger, String tablePrefix, String instanceId, ref Boolean useProperties)
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

		protected internal virtual bool canUseProperties()
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
		/// <param name="">conn
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
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int updateTriggerStatesFromOtherStates(OleDbConnection conn, String newState, String oldState1,
		                                                      String oldState2)
		{
			OleDbCommand ps = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         rtp(
					                                                         	StdJDBCConstants_Fields.
					                                                         		UPDATE_TRIGGER_STATES_FROM_OTHER_STATES));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, newState);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, oldState1);
				SupportClass.TransactionManager.manager.SetValue(ps, 3, oldState2);
				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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
		/// Get the names of all of the triggers that have misfired.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
		/// the DB Connection
		/// </param>
		/// <returns> an array of <code>{@link
		/// org.quartz.utils.Key}</code> objects
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual Key[] selectMisfiredTriggers(OleDbConnection conn, long ts)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         rtp(StdJDBCConstants_Fields.SELECT_MISFIRED_TRIGGERS));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, Decimal.Parse(Convert.ToString(ts), NumberStyles.Any));
				rs = ps.ExecuteReader();

				ArrayList list = new ArrayList();
				while (rs.Read())
				{
					String triggerName = Convert.ToString(rs[Constants_Fields.COL_TRIGGER_NAME]);
					String groupName = Convert.ToString(rs[Constants_Fields.COL_TRIGGER_GROUP]);
					list.Add(new Key(triggerName, groupName));
				}
				Object[] oArr = list.ToArray();
				Key[] kArr = new Key[oArr.Length];
				Array.Copy(oArr, 0, kArr, 0, oArr.Length);
				return kArr;
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
		/// Select all of the triggers in a given state.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
		/// the DB Connection
		/// </param>
		/// <param name="">state
		/// the state the triggers must be in
		/// </param>
		/// <returns> an array of trigger <code>Key</code> s
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual Key[] selectTriggersInState(OleDbConnection conn, String state)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         rtp(StdJDBCConstants_Fields.SELECT_TRIGGERS_IN_STATE));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, state);
				rs = ps.ExecuteReader();

				ArrayList list = new ArrayList();
				while (rs.Read())
				{
					list.Add(new Key(Convert.ToString(rs[1 - 1]), Convert.ToString(rs[2 - 1])));
				}

				Key[] sArr = (Key[]) SupportClass.ICollectionSupport.ToArray(list, new Key[list.Count]);
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

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual Key[] selectMisfiredTriggersInState(OleDbConnection conn, String state, long ts)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         rtp(
					                                                         	StdJDBCConstants_Fields.SELECT_MISFIRED_TRIGGERS_IN_STATE));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, Decimal.Parse(Convert.ToString(ts), NumberStyles.Any));
				SupportClass.TransactionManager.manager.SetValue(ps, 2, state);
				rs = ps.ExecuteReader();

				ArrayList list = new ArrayList();
				while (rs.Read())
				{
					String triggerName = Convert.ToString(rs[Constants_Fields.COL_TRIGGER_NAME]);
					String groupName = Convert.ToString(rs[Constants_Fields.COL_TRIGGER_GROUP]);
					list.Add(new Key(triggerName, groupName));
				}
				Object[] oArr = list.ToArray();
				Key[] kArr = new Key[oArr.Length];
				Array.Copy(oArr, 0, kArr, 0, oArr.Length);
				return kArr;
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
		/// Get the names of all of the triggers in the given group and state that
		/// have misfired.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
		/// the DB Connection
		/// </param>
		/// <returns> an array of <code>{@link
		/// org.quartz.utils.Key}</code> objects
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual Key[] selectMisfiredTriggersInGroupInState(OleDbConnection conn, String groupName, String state,
		                                                          long ts)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         rtp(
					                                                         	StdJDBCConstants_Fields.
					                                                         		SELECT_MISFIRED_TRIGGERS_IN_GROUP_IN_STATE));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, Decimal.Parse(Convert.ToString(ts), NumberStyles.Any));
				SupportClass.TransactionManager.manager.SetValue(ps, 2, groupName);
				SupportClass.TransactionManager.manager.SetValue(ps, 3, state);
				rs = ps.ExecuteReader();

				ArrayList list = new ArrayList();
				while (rs.Read())
				{
					String triggerName = Convert.ToString(rs[Constants_Fields.COL_TRIGGER_NAME]);
					list.Add(new Key(triggerName, groupName));
				}
				Object[] oArr = list.ToArray();
				Key[] kArr = new Key[oArr.Length];
				Array.Copy(oArr, 0, kArr, 0, oArr.Length);
				return kArr;
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
		/// <param name="">conn
		/// the DB Connection
		/// </param>
		/// <returns> an array of <code>{@link org.quartz.Trigger}</code> objects
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual Trigger[] selectTriggersForRecoveringJobs(OleDbConnection conn)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         rtp(
					                                                         	StdJDBCConstants_Fields.
					                                                         		SELECT_INSTANCES_RECOVERABLE_FIRED_TRIGGERS));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, instanceId);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, true);
				rs = ps.ExecuteReader();

				long dumId = (DateTime.Now.Ticks - 621355968000000000)/10000;
				ArrayList list = new ArrayList();
				while (rs.Read())
				{
					String jobName = Convert.ToString(rs[Constants_Fields.COL_JOB_NAME]);
					String jobGroup = Convert.ToString(rs[Constants_Fields.COL_JOB_GROUP]);
					String trigName = Convert.ToString(rs[Constants_Fields.COL_TRIGGER_NAME]);
					String trigGroup = Convert.ToString(rs[Constants_Fields.COL_TRIGGER_GROUP]);
					long firedTime = Convert.ToInt64(rs[Constants_Fields.COL_FIRED_TIME]);
					//UPGRADE_TODO: Constructor 'java.util.Date.Date' was converted to 'DateTime.DateTime' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilDateDate_long_3"'
					DateTime tempAux = new DateTime(firedTime);
					//UPGRADE_NOTE: ref keyword was added to struct-type parameters. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1303_3"'
					SimpleTrigger rcvryTrig =
						new SimpleTrigger("recover_" + instanceId + "_" + Convert.ToString(dumId++),
						                  Scheduler_Fields.DEFAULT_RECOVERY_GROUP, ref tempAux);
					rcvryTrig.JobName = jobName;
					rcvryTrig.JobGroup = jobGroup;
					rcvryTrig.MisfireInstruction = SimpleTrigger.MISFIRE_INSTRUCTION_FIRE_NOW;

					JobDataMap jd = selectTriggerJobDataMap(conn, trigName, trigGroup);
					jd.Put("QRTZ_FAILED_JOB_ORIG_TRIGGER_NAME", trigName);
					jd.Put("QRTZ_FAILED_JOB_ORIG_TRIGGER_GROUP", trigGroup);
					jd.Put("QRTZ_FAILED_JOB_ORIG_TRIGGER_FIRETIME_IN_MILLISECONDS_AS_STRING", Convert.ToString(firedTime));
					rcvryTrig.JobDataMap = jd;

					list.Add(rcvryTrig);
				}
				Object[] oArr = list.ToArray();
				Trigger[] tArr = new Trigger[oArr.Length];
				Array.Copy(oArr, 0, tArr, 0, oArr.Length);
				return tArr;
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
		/// Delete all fired triggers.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
		/// the DB Connection
		/// </param>
		/// <returns> the number of rows deleted
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int deleteFiredTriggers(OleDbConnection conn)
		{
			OleDbCommand ps = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.DELETE_FIRED_TRIGGERS));

				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int deleteFiredTriggers(OleDbConnection conn, String instanceId)
		{
			OleDbCommand ps = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         rtp(
					                                                         	StdJDBCConstants_Fields.DELETE_INSTANCES_FIRED_TRIGGERS));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, instanceId);

				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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

		//---------------------------------------------------------------------------
		// jobs
		//---------------------------------------------------------------------------

		/// <summary> <p>
		/// Insert the job detail record.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
		/// the DB Connection
		/// </param>
		/// <param name="">job
		/// the job to insert
		/// </param>
		/// <returns> number of rows inserted
		/// </returns>
		/// <throws>  IOException </throws>
		/// <summary>           if there were problems serializing the JobDataMap
		/// </summary>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int insertJobDetail(OleDbConnection conn, JobDetail job)
		{
			MemoryStream baos = serializeJobData(job.JobDataMap);

			OleDbCommand ps = null;

			int insertResult = 0;

			try
			{
				ps = SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.INSERT_JOB_DETAIL));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, job.Name);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, job.Group);
				SupportClass.TransactionManager.manager.SetValue(ps, 3, job.Description);
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Class.getName' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				SupportClass.TransactionManager.manager.SetValue(ps, 4, job.JobClass.FullName);
				SupportClass.TransactionManager.manager.SetValue(ps, 5, job.Durable);
				SupportClass.TransactionManager.manager.SetValue(ps, 6, job.Volatile);
				SupportClass.TransactionManager.manager.SetValue(ps, 7, job.Stateful);
				SupportClass.TransactionManager.manager.SetValue(ps, 8, job.requestsRecovery());
				SupportClass.TransactionManager.manager.SetValue(ps, 9, SupportClass.ToSByteArray(baos.ToArray()));

				insertResult = SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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
				String[] jobListeners = job.JobListenerNames;
				for (int i = 0; jobListeners != null && i < jobListeners.Length; i++)
				{
					insertJobListener(conn, job, jobListeners[i]);
				}
			}

			return insertResult;
		}

		/// <summary> <p>
		/// Update the job detail record.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
		/// the DB Connection
		/// </param>
		/// <param name="">job
		/// the job to update
		/// </param>
		/// <returns> number of rows updated
		/// </returns>
		/// <throws>  IOException </throws>
		/// <summary>           if there were problems serializing the JobDataMap
		/// </summary>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int updateJobDetail(OleDbConnection conn, JobDetail job)
		{
			MemoryStream baos = serializeJobData(job.JobDataMap);

			OleDbCommand ps = null;

			int insertResult = 0;

			try
			{
				ps = SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.UPDATE_JOB_DETAIL));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, job.Description);
				//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Class.getName' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
				SupportClass.TransactionManager.manager.SetValue(ps, 2, job.JobClass.FullName);
				SupportClass.TransactionManager.manager.SetValue(ps, 3, job.Durable);
				SupportClass.TransactionManager.manager.SetValue(ps, 4, job.Volatile);
				SupportClass.TransactionManager.manager.SetValue(ps, 5, job.Stateful);
				SupportClass.TransactionManager.manager.SetValue(ps, 6, job.requestsRecovery());
				SupportClass.TransactionManager.manager.SetValue(ps, 7, SupportClass.ToSByteArray(baos.ToArray()));
				SupportClass.TransactionManager.manager.SetValue(ps, 8, job.Name);
				SupportClass.TransactionManager.manager.SetValue(ps, 9, job.Group);

				insertResult = SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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
				deleteJobListeners(conn, job.Name, job.Group);

				String[] jobListeners = job.JobListenerNames;
				for (int i = 0; jobListeners != null && i < jobListeners.Length; i++)
				{
					insertJobListener(conn, job, jobListeners[i]);
				}
			}

			return insertResult;
		}

		/// <summary> <p>
		/// Get the names of all of the triggers associated with the given job.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
		/// the DB Connection
		/// </param>
		/// <param name="">jobName
		/// the name of the job
		/// </param>
		/// <param name="">groupName
		/// the group containing the job
		/// </param>
		/// <returns> an array of <code>{@link
		/// org.quartz.utils.Key}</code> objects
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual Key[] selectTriggerNamesForJob(OleDbConnection conn, String jobName, String groupName)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.SELECT_TRIGGERS_FOR_JOB));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, jobName);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, groupName);
				rs = ps.ExecuteReader();

				ArrayList list = new ArrayList(10);
				while (rs.Read())
				{
					String trigName = Convert.ToString(rs[Constants_Fields.COL_TRIGGER_NAME]);
					String trigGroup = Convert.ToString(rs[Constants_Fields.COL_TRIGGER_GROUP]);
					list.Add(new Key(trigName, trigGroup));
				}
				Object[] oArr = list.ToArray();
				Key[] kArr = new Key[oArr.Length];
				Array.Copy(oArr, 0, kArr, 0, oArr.Length);
				return kArr;
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
		/// Delete all job listeners for the given job.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
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
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int deleteJobListeners(OleDbConnection conn, String jobName, String groupName)
		{
			OleDbCommand ps = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.DELETE_JOB_LISTENERS));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, jobName);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, groupName);
				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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
		/// Delete the job detail record for the given job.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
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
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int deleteJobDetail(OleDbConnection conn, String jobName, String groupName)
		{
			OleDbCommand ps = null;

			try
			{
				logger.debug("Deleting job: " + groupName + "." + jobName);
				ps = SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.DELETE_JOB_DETAIL));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, jobName);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, groupName);
				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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
		/// Check whether or not the given job is stateful.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
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
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual bool isJobStateful(OleDbConnection conn, String jobName, String groupName)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.SELECT_JOB_STATEFUL));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, jobName);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, groupName);
				rs = ps.ExecuteReader();
				if (!rs.Read())
				{
					return false;
				}
				return Convert.ToBoolean(rs[Constants_Fields.COL_IS_STATEFUL]);
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
		/// Check whether or not the given job exists.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
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
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual bool jobExists(OleDbConnection conn, String jobName, String groupName)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.SELECT_JOB_EXISTENCE));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, jobName);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, groupName);
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
		/// Update the job data map for the given job.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
		/// the DB Connection
		/// </param>
		/// <param name="">job
		/// the job to update
		/// </param>
		/// <returns> the number of rows updated
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int updateJobData(OleDbConnection conn, JobDetail job)
		{
			MemoryStream baos = serializeJobData(job.JobDataMap);

			OleDbCommand ps = null;

			try
			{
				ps = SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.UPDATE_JOB_DATA));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, SupportClass.ToSByteArray(baos.ToArray()));
				SupportClass.TransactionManager.manager.SetValue(ps, 2, job.Name);
				SupportClass.TransactionManager.manager.SetValue(ps, 3, job.Group);

				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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
		/// Associate a listener with a job.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
		/// the DB Connection
		/// </param>
		/// <param name="">job
		/// the job to associate with the listener
		/// </param>
		/// <param name="">listener
		/// the listener to insert
		/// </param>
		/// <returns> the number of rows inserted
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int insertJobListener(OleDbConnection conn, JobDetail job, String listener)
		{
			OleDbCommand ps = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.INSERT_JOB_LISTENER));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, job.Name);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, job.Group);
				SupportClass.TransactionManager.manager.SetValue(ps, 3, listener);

				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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
		/// Get all of the listeners for a given job.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
		/// the DB Connection
		/// </param>
		/// <param name="">jobName
		/// the job name whose listeners are wanted
		/// </param>
		/// <param name="">groupName
		/// the group containing the job
		/// </param>
		/// <returns> array of <code>String</code> listener names
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual String[] selectJobListeners(OleDbConnection conn, String jobName, String groupName)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ArrayList list = new ArrayList();
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.SELECT_JOB_LISTENERS));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, jobName);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, groupName);
				rs = ps.ExecuteReader();

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
		/// Select the JobDetail object for a given job name / group name.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
		/// the DB Connection
		/// </param>
		/// <param name="">jobName
		/// the job name whose listeners are wanted
		/// </param>
		/// <param name="">groupName
		/// the group containing the job
		/// </param>
		/// <returns> the populated JobDetail object
		/// </returns>
		/// <throws>  ClassNotFoundException </throws>
		/// <summary>           if a class found during deserialization cannot be found or if
		/// the job class could not be found
		/// </summary>
		/// <throws>  IOException </throws>
		/// <summary>           if deserialization causes an error
		/// </summary>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual JobDetail selectJobDetail(OleDbConnection conn, String jobName, String groupName,
		                                         ClassLoadHelper loadHelper)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps = SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.SELECT_JOB_DETAIL));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, jobName);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, groupName);
				rs = ps.ExecuteReader();

				JobDetail job = null;

				if (rs.Read())
				{
					job = new JobDetail();

					job.Name = Convert.ToString(rs[Constants_Fields.COL_JOB_NAME]);
					job.Group = Convert.ToString(rs[Constants_Fields.COL_JOB_GROUP]);
					job.Description = Convert.ToString(rs[Constants_Fields.COL_DESCRIPTION]);
					job.JobClass = loadHelper.loadClass(Convert.ToString(rs[Constants_Fields.COL_JOB_CLASS]));
					job.Durability = Convert.ToBoolean(rs[Constants_Fields.COL_IS_DURABLE]);
					job.Volatility = Convert.ToBoolean(rs[Constants_Fields.COL_IS_VOLATILE]);
					job.RequestsRecovery = Convert.ToBoolean(rs[Constants_Fields.COL_REQUESTS_RECOVERY]);

					IDictionary map = null;
					if (canUseProperties())
					{
						map = getMapFromProperties(rs);
					}
					else
					{
						map = (IDictionary) getObjectFromBlob(rs, Constants_Fields.COL_JOB_DATAMAP);
					}

					if (null != map)
					{
						job.JobDataMap = new JobDataMap(map);
					}
				}

				return job;
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

		/// <summary> build Map from java.util.Properties encoding.</summary>
		//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
		private IDictionary getMapFromProperties(OleDbDataReader rs)
		{
			IDictionary map;
			Stream is_Renamed = (Stream) getJobDetailFromBlob(rs, Constants_Fields.COL_JOB_DATAMAP);
			if (is_Renamed == null)
			{
				return null;
			}
			//UPGRADE_ISSUE: Class hierarchy differences between 'java.util.Properties' and 'System.Collections.Specialized.NameValueCollection' may cause compilation errors. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1186_3"'
			//UPGRADE_TODO: Format of property file may need to be changed. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1089_3"'
			NameValueCollection properties = new NameValueCollection();
			if (is_Renamed != null)
			{
				//UPGRADE_TODO: Method 'java.util.Properties.load' was converted to 'System.Collections.Specialized.NameValueCollection' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilPropertiesload_javaioInputStream_3"'
				properties = new NameValueCollection(ConfigurationSettings.AppSettings);
			}
			map = convertFromProperty(properties);
			return map;
		}

		/// <summary> <p>
		/// Select the total number of jobs stored.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
		/// the DB Connection
		/// </param>
		/// <returns> the total number of jobs stored
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int selectNumJobs(OleDbConnection conn)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				int count = 0;
				ps = SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.SELECT_NUM_JOBS));
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
		/// Select all of the job group names that are stored.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
		/// the DB Connection
		/// </param>
		/// <returns> an array of <code>String</code> group names
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual String[] selectJobGroups(OleDbConnection conn)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps = SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.SELECT_JOB_GROUPS));
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
		/// Select all of the jobs contained in a given group.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
		/// the DB Connection
		/// </param>
		/// <param name="">groupName
		/// the group containing the jobs
		/// </param>
		/// <returns> an array of <code>String</code> job names
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual String[] selectJobsInGroup(OleDbConnection conn, String groupName)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.SELECT_JOBS_IN_GROUP));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, groupName);
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
		// triggers
		//---------------------------------------------------------------------------

		/// <summary> <p>
		/// Insert the base trigger data.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
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
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int insertTrigger(OleDbConnection conn, Trigger trigger, String state, JobDetail jobDetail)
		{
			MemoryStream baos = null;
			if (trigger.JobDataMap.Count > 0)
			{
				baos = serializeJobData(trigger.JobDataMap);
			}

			OleDbCommand ps = null;

			int insertResult = 0;

			try
			{
				ps = SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.INSERT_TRIGGER));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, trigger.Name);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, trigger.Group);
				SupportClass.TransactionManager.manager.SetValue(ps, 3, trigger.JobName);
				SupportClass.TransactionManager.manager.SetValue(ps, 4, trigger.JobGroup);
				SupportClass.TransactionManager.manager.SetValue(ps, 5, trigger.Volatile);
				SupportClass.TransactionManager.manager.SetValue(ps, 6, trigger.Description);
				//UPGRADE_TODO: Method 'java.util.Date.getTime' was converted to 'DateTime.Ticks' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilDategetTime_3"'
				SupportClass.TransactionManager.manager.SetValue(ps, 7,
				                                                 Decimal.Parse(Convert.ToString(trigger.GetNextFireTime().Ticks),
				                                                               NumberStyles.Any));
				long prevFireTime = - 1;
				//UPGRADE_TODO: The 'DateTime' structure does not have an equivalent to NULL. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1291_3"'
				if (trigger.GetPreviousFireTime() != null)
				{
					//UPGRADE_TODO: Method 'java.util.Date.getTime' was converted to 'DateTime.Ticks' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilDategetTime_3"'
					prevFireTime = trigger.GetPreviousFireTime().Ticks;
				}
				SupportClass.TransactionManager.manager.SetValue(ps, 8,
				                                                 Decimal.Parse(Convert.ToString(prevFireTime), NumberStyles.Any));
				SupportClass.TransactionManager.manager.SetValue(ps, 9, state);
				if (trigger is SimpleTrigger)
				{
					SupportClass.TransactionManager.manager.SetValue(ps, 10, Constants_Fields.TTYPE_SIMPLE);
				}
				else if (trigger is CronTrigger)
				{
					SupportClass.TransactionManager.manager.SetValue(ps, 10, Constants_Fields.TTYPE_CRON);
				}
				else
				{
					// (trigger instanceof BlobTrigger)
					SupportClass.TransactionManager.manager.SetValue(ps, 10, Constants_Fields.TTYPE_BLOB);
				}
				//UPGRADE_TODO: Method 'java.util.Date.getTime' was converted to 'DateTime.Ticks' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilDategetTime_3"'
				SupportClass.TransactionManager.manager.SetValue(ps, 11,
				                                                 Decimal.Parse(Convert.ToString(trigger.StartTime.Ticks),
				                                                               NumberStyles.Any));
				long endTime = 0;
				//UPGRADE_TODO: The 'DateTime' structure does not have an equivalent to NULL. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1291_3"'
				if (trigger.EndTime != null)
				{
					//UPGRADE_TODO: Method 'java.util.Date.getTime' was converted to 'DateTime.Ticks' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilDategetTime_3"'
					endTime = trigger.EndTime.Ticks;
				}
				SupportClass.TransactionManager.manager.SetValue(ps, 12, Decimal.Parse(Convert.ToString(endTime), NumberStyles.Any));
				SupportClass.TransactionManager.manager.SetValue(ps, 13, trigger.CalendarName);
				SupportClass.TransactionManager.manager.SetValue(ps, 14, trigger.MisfireInstruction);
				if (baos != null)
				{
					SupportClass.TransactionManager.manager.SetValue(ps, 15, SupportClass.ToSByteArray(baos.ToArray()));
				}
				else
				{
					SupportClass.TransactionManager.manager.SetValue(ps, 15, null);
				}

				insertResult = SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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
				String[] trigListeners = trigger.TriggerListenerNames;
				for (int i = 0; trigListeners != null && i < trigListeners.Length; i++)
				{
					insertTriggerListener(conn, trigger, trigListeners[i]);
				}
			}

			return insertResult;
		}

		/// <summary> <p>
		/// Insert the simple trigger data.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
		/// the DB Connection
		/// </param>
		/// <param name="">trigger
		/// the trigger to insert
		/// </param>
		/// <returns> the number of rows inserted
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int insertSimpleTrigger(OleDbConnection conn, SimpleTrigger trigger)
		{
			OleDbCommand ps = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.INSERT_SIMPLE_TRIGGER));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, trigger.Name);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, trigger.Group);
				SupportClass.TransactionManager.manager.SetValue(ps, 3, trigger.RepeatCount);
				SupportClass.TransactionManager.manager.SetValue(ps, 4,
				                                                 Decimal.Parse(Convert.ToString(trigger.RepeatInterval),
				                                                               NumberStyles.Any));
				SupportClass.TransactionManager.manager.SetValue(ps, 5, trigger.TimesTriggered);

				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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
		/// Insert the cron trigger data.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
		/// the DB Connection
		/// </param>
		/// <param name="">trigger
		/// the trigger to insert
		/// </param>
		/// <returns> the number of rows inserted
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int insertCronTrigger(OleDbConnection conn, CronTrigger trigger)
		{
			OleDbCommand ps = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.INSERT_CRON_TRIGGER));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, trigger.Name);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, trigger.Group);
				SupportClass.TransactionManager.manager.SetValue(ps, 3, trigger.CronExpression);
				SupportClass.TransactionManager.manager.SetValue(ps, 4, trigger.TimeZone.StandardName);

				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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
		/// Insert the blob trigger data.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
		/// the DB Connection
		/// </param>
		/// <param name="">trigger
		/// the trigger to insert
		/// </param>
		/// <returns> the number of rows inserted
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int insertBlobTrigger(OleDbConnection conn, Trigger trigger)
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
					SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.INSERT_BLOB_TRIGGER));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, trigger.Name);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, trigger.Group);
				SupportClass.TransactionManager.manager.SetValue(ps, 3, is_Renamed);

				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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
		/// Update the base trigger data.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
		/// the DB Connection
		/// </param>
		/// <param name="">trigger
		/// the trigger to insert
		/// </param>
		/// <param name="">state
		/// the state that the trigger should be stored in
		/// </param>
		/// <returns> the number of rows updated
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int updateTrigger(OleDbConnection conn, Trigger trigger, String state, JobDetail jobDetail)
		{
			// save some clock cycles by unnecessarily writing job data blob ...
			bool updateJobData = trigger.JobDataMap.Dirty;
			MemoryStream baos = null;
			if (updateJobData && trigger.JobDataMap.Count > 0)
			{
				baos = serializeJobData(trigger.JobDataMap);
			}

			OleDbCommand ps = null;

			int insertResult = 0;


			try
			{
				if (updateJobData)
				{
					ps = SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.UPDATE_TRIGGER));
				}
				else
				{
					ps =
						SupportClass.TransactionManager.manager.PrepareStatement(conn,
						                                                         rtp(StdJDBCConstants_Fields.UPDATE_TRIGGER_SKIP_DATA));
				}

				SupportClass.TransactionManager.manager.SetValue(ps, 1, trigger.JobName);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, trigger.JobGroup);
				SupportClass.TransactionManager.manager.SetValue(ps, 3, trigger.Volatile);
				SupportClass.TransactionManager.manager.SetValue(ps, 4, trigger.Description);
				long nextFireTime = - 1;
				//UPGRADE_TODO: The 'DateTime' structure does not have an equivalent to NULL. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1291_3"'
				if (trigger.GetNextFireTime() != null)
				{
					//UPGRADE_TODO: Method 'java.util.Date.getTime' was converted to 'DateTime.Ticks' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilDategetTime_3"'
					nextFireTime = trigger.GetNextFireTime().Ticks;
				}
				SupportClass.TransactionManager.manager.SetValue(ps, 5,
				                                                 Decimal.Parse(Convert.ToString(nextFireTime), NumberStyles.Any));
				long prevFireTime = - 1;
				//UPGRADE_TODO: The 'DateTime' structure does not have an equivalent to NULL. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1291_3"'
				if (trigger.GetPreviousFireTime() != null)
				{
					//UPGRADE_TODO: Method 'java.util.Date.getTime' was converted to 'DateTime.Ticks' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilDategetTime_3"'
					prevFireTime = trigger.GetPreviousFireTime().Ticks;
				}
				SupportClass.TransactionManager.manager.SetValue(ps, 6,
				                                                 Decimal.Parse(Convert.ToString(prevFireTime), NumberStyles.Any));
				SupportClass.TransactionManager.manager.SetValue(ps, 7, state);
				if (trigger is SimpleTrigger)
				{
					//                updateSimpleTrigger(conn, (SimpleTrigger)trigger);
					SupportClass.TransactionManager.manager.SetValue(ps, 8, Constants_Fields.TTYPE_SIMPLE);
				}
				else if (trigger is CronTrigger)
				{
					//                updateCronTrigger(conn, (CronTrigger)trigger);
					SupportClass.TransactionManager.manager.SetValue(ps, 8, Constants_Fields.TTYPE_CRON);
				}
				else
				{
					//                updateBlobTrigger(conn, trigger);
					SupportClass.TransactionManager.manager.SetValue(ps, 8, Constants_Fields.TTYPE_BLOB);
				}
				//UPGRADE_TODO: Method 'java.util.Date.getTime' was converted to 'DateTime.Ticks' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilDategetTime_3"'
				SupportClass.TransactionManager.manager.SetValue(ps, 9,
				                                                 Decimal.Parse(Convert.ToString(trigger.StartTime.Ticks),
				                                                               NumberStyles.Any));
				long endTime = 0;
				//UPGRADE_TODO: The 'DateTime' structure does not have an equivalent to NULL. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1291_3"'
				if (trigger.EndTime != null)
				{
					//UPGRADE_TODO: Method 'java.util.Date.getTime' was converted to 'DateTime.Ticks' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilDategetTime_3"'
					endTime = trigger.EndTime.Ticks;
				}
				SupportClass.TransactionManager.manager.SetValue(ps, 10, Decimal.Parse(Convert.ToString(endTime), NumberStyles.Any));
				SupportClass.TransactionManager.manager.SetValue(ps, 11, trigger.CalendarName);
				SupportClass.TransactionManager.manager.SetValue(ps, 12, trigger.MisfireInstruction);
				if (updateJobData)
				{
					SupportClass.TransactionManager.manager.SetValue(ps, 13, SupportClass.ToSByteArray(baos.ToArray()));

					SupportClass.TransactionManager.manager.SetValue(ps, 14, trigger.Name);
					SupportClass.TransactionManager.manager.SetValue(ps, 15, trigger.Group);
				}
				else
				{
					SupportClass.TransactionManager.manager.SetValue(ps, 13, trigger.Name);
					SupportClass.TransactionManager.manager.SetValue(ps, 14, trigger.Group);
				}

				insertResult = SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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
				deleteTriggerListeners(conn, trigger.Name, trigger.Group);

				String[] trigListeners = trigger.TriggerListenerNames;
				for (int i = 0; trigListeners != null && i < trigListeners.Length; i++)
				{
					insertTriggerListener(conn, trigger, trigListeners[i]);
				}
			}

			return insertResult;
		}

		/// <summary> <p>
		/// Update the simple trigger data.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
		/// the DB Connection
		/// </param>
		/// <param name="">trigger
		/// the trigger to insert
		/// </param>
		/// <returns> the number of rows updated
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int updateSimpleTrigger(OleDbConnection conn, SimpleTrigger trigger)
		{
			OleDbCommand ps = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.UPDATE_SIMPLE_TRIGGER));

				SupportClass.TransactionManager.manager.SetValue(ps, 1, trigger.RepeatCount);
				SupportClass.TransactionManager.manager.SetValue(ps, 2,
				                                                 Decimal.Parse(Convert.ToString(trigger.RepeatInterval),
				                                                               NumberStyles.Any));
				SupportClass.TransactionManager.manager.SetValue(ps, 3, trigger.TimesTriggered);
				SupportClass.TransactionManager.manager.SetValue(ps, 4, trigger.Name);
				SupportClass.TransactionManager.manager.SetValue(ps, 5, trigger.Group);

				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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
		/// Update the cron trigger data.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
		/// the DB Connection
		/// </param>
		/// <param name="">trigger
		/// the trigger to insert
		/// </param>
		/// <returns> the number of rows updated
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int updateCronTrigger(OleDbConnection conn, CronTrigger trigger)
		{
			OleDbCommand ps = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.UPDATE_CRON_TRIGGER));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, trigger.CronExpression);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, trigger.Name);
				SupportClass.TransactionManager.manager.SetValue(ps, 3, trigger.Group);

				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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
		/// Update the blob trigger data.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
		/// the DB Connection
		/// </param>
		/// <param name="">trigger
		/// the trigger to insert
		/// </param>
		/// <returns> the number of rows updated
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int updateBlobTrigger(OleDbConnection conn, Trigger trigger)
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
					SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.UPDATE_BLOB_TRIGGER));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, is_Renamed);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, trigger.Name);
				SupportClass.TransactionManager.manager.SetValue(ps, 3, trigger.Group);

				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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

		/// <summary> <p>
		/// Check whether or not a trigger exists.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
		/// the DB Connection
		/// </param>
		/// <param name="">triggerName
		/// the name of the trigger
		/// </param>
		/// <param name="">groupName
		/// the group containing the trigger
		/// </param>
		/// <returns> true if the trigger exists, false otherwise
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual bool triggerExists(OleDbConnection conn, String triggerName, String groupName)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         rtp(StdJDBCConstants_Fields.SELECT_TRIGGER_EXISTENCE));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, triggerName);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, groupName);
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
		/// Update the state for a given trigger.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
		/// the DB Connection
		/// </param>
		/// <param name="">triggerName
		/// the name of the trigger
		/// </param>
		/// <param name="">groupName
		/// the group containing the trigger
		/// </param>
		/// <param name="">state
		/// the new state for the trigger
		/// </param>
		/// <returns> the number of rows updated
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int updateTriggerState(OleDbConnection conn, String triggerName, String groupName, String state)
		{
			OleDbCommand ps = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.UPDATE_TRIGGER_STATE));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, state);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, triggerName);
				SupportClass.TransactionManager.manager.SetValue(ps, 3, groupName);

				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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
		/// Update the given trigger to the given new state, if it is one of the
		/// given old states.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
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
		/// <param name="">oldState1
		/// one of the old state the trigger must be in
		/// </param>
		/// <param name="">oldState2
		/// one of the old state the trigger must be in
		/// </param>
		/// <param name="">oldState3
		/// one of the old state the trigger must be in
		/// </param>
		/// <returns> int the number of rows updated
		/// </returns>
		/// <throws>  SQLException </throws>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int updateTriggerStateFromOtherStates(OleDbConnection conn, String triggerName, String groupName,
		                                                     String newState, String oldState1, String oldState2,
		                                                     String oldState3)
		{
			OleDbCommand ps = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         rtp(
					                                                         	StdJDBCConstants_Fields.UPDATE_TRIGGER_STATE_FROM_STATES));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, newState);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, triggerName);
				SupportClass.TransactionManager.manager.SetValue(ps, 3, groupName);
				SupportClass.TransactionManager.manager.SetValue(ps, 4, oldState1);
				SupportClass.TransactionManager.manager.SetValue(ps, 5, oldState2);
				SupportClass.TransactionManager.manager.SetValue(ps, 6, oldState3);

				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int updateTriggerStateFromOtherStatesBeforeTime(OleDbConnection conn, String newState, String oldState1,
		                                                               String oldState2, long time)
		{
			OleDbCommand ps = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         rtp(
					                                                         	StdJDBCConstants_Fields.
					                                                         		UPDATE_TRIGGER_STATE_FROM_OTHER_STATES_BEFORE_TIME));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, newState);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, oldState1);
				SupportClass.TransactionManager.manager.SetValue(ps, 3, oldState2);
				SupportClass.TransactionManager.manager.SetValue(ps, 4, time);

				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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
		/// Update all triggers in the given group to the given new state, if they
		/// are in one of the given old states.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
		/// the DB connection
		/// </param>
		/// <param name="">groupName
		/// the group containing the trigger
		/// </param>
		/// <param name="">newState
		/// the new state for the trigger
		/// </param>
		/// <param name="">oldState1
		/// one of the old state the trigger must be in
		/// </param>
		/// <param name="">oldState2
		/// one of the old state the trigger must be in
		/// </param>
		/// <param name="">oldState3
		/// one of the old state the trigger must be in
		/// </param>
		/// <returns> int the number of rows updated
		/// </returns>
		/// <throws>  SQLException </throws>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int updateTriggerGroupStateFromOtherStates(OleDbConnection conn, String groupName, String newState,
		                                                          String oldState1, String oldState2, String oldState3)
		{
			OleDbCommand ps = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         rtp(
					                                                         	StdJDBCConstants_Fields.
					                                                         		UPDATE_TRIGGER_GROUP_STATE_FROM_STATES));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, newState);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, groupName);
				SupportClass.TransactionManager.manager.SetValue(ps, 3, oldState1);
				SupportClass.TransactionManager.manager.SetValue(ps, 4, oldState2);
				SupportClass.TransactionManager.manager.SetValue(ps, 5, oldState3);

				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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
		/// Update the given trigger to the given new state, if it is in the given
		/// old state.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
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
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int updateTriggerStateFromOtherState(OleDbConnection conn, String triggerName, String groupName,
		                                                    String newState, String oldState)
		{
			OleDbCommand ps = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         rtp(
					                                                         	StdJDBCConstants_Fields.UPDATE_TRIGGER_STATE_FROM_STATE));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, newState);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, triggerName);
				SupportClass.TransactionManager.manager.SetValue(ps, 3, groupName);
				SupportClass.TransactionManager.manager.SetValue(ps, 4, oldState);

				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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
		/// Update all of the triggers of the given group to the given new state, if
		/// they are in the given old state.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
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
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int updateTriggerGroupStateFromOtherState(OleDbConnection conn, String groupName, String newState,
		                                                         String oldState)
		{
			OleDbCommand ps = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         rtp(
					                                                         	StdJDBCConstants_Fields.
					                                                         		UPDATE_TRIGGER_GROUP_STATE_FROM_STATE));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, newState);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, groupName);
				SupportClass.TransactionManager.manager.SetValue(ps, 3, oldState);

				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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
		/// Update the states of all triggers associated with the given job.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
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
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int updateTriggerStatesForJob(OleDbConnection conn, String jobName, String groupName, String state)
		{
			OleDbCommand ps = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         rtp(StdJDBCConstants_Fields.UPDATE_JOB_TRIGGER_STATES));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, state);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, jobName);
				SupportClass.TransactionManager.manager.SetValue(ps, 3, groupName);

				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int updateTriggerStatesForJobFromOtherState(OleDbConnection conn, String jobName, String groupName,
		                                                           String state, String oldState)
		{
			OleDbCommand ps = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         rtp(
					                                                         	StdJDBCConstants_Fields.
					                                                         		UPDATE_JOB_TRIGGER_STATES_FROM_OTHER_STATE));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, state);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, jobName);
				SupportClass.TransactionManager.manager.SetValue(ps, 3, groupName);
				SupportClass.TransactionManager.manager.SetValue(ps, 4, oldState);

				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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
		/// Delete all of the listeners associated with a given trigger.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
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
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int deleteTriggerListeners(OleDbConnection conn, String triggerName, String groupName)
		{
			OleDbCommand ps = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         rtp(StdJDBCConstants_Fields.DELETE_TRIGGER_LISTENERS));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, triggerName);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, groupName);
				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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
		/// Associate a listener with the given trigger.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
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
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int insertTriggerListener(OleDbConnection conn, Trigger trigger, String listener)
		{
			OleDbCommand ps = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.INSERT_TRIGGER_LISTENER));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, trigger.Name);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, trigger.Group);
				SupportClass.TransactionManager.manager.SetValue(ps, 3, listener);

				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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
		/// <param name="">conn
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
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual String[] selectTriggerListeners(OleDbConnection conn, String triggerName, String groupName)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         rtp(StdJDBCConstants_Fields.SELECT_TRIGGER_LISTENERS));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, triggerName);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, groupName);
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
		/// <param name="">conn
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
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int deleteSimpleTrigger(OleDbConnection conn, String triggerName, String groupName)
		{
			OleDbCommand ps = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.DELETE_SIMPLE_TRIGGER));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, triggerName);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, groupName);

				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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
		/// <param name="">conn
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
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int deleteCronTrigger(OleDbConnection conn, String triggerName, String groupName)
		{
			OleDbCommand ps = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.DELETE_CRON_TRIGGER));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, triggerName);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, groupName);

				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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
		/// <param name="">conn
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
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int deleteBlobTrigger(OleDbConnection conn, String triggerName, String groupName)
		{
			OleDbCommand ps = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.DELETE_BLOB_TRIGGER));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, triggerName);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, groupName);

				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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
		/// <param name="">conn
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
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int deleteTrigger(OleDbConnection conn, String triggerName, String groupName)
		{
			OleDbCommand ps = null;

			try
			{
				ps = SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.DELETE_TRIGGER));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, triggerName);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, groupName);

				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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
		/// <param name="">conn
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
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int selectNumTriggersForJob(OleDbConnection conn, String jobName, String groupName)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         rtp(StdJDBCConstants_Fields.SELECT_NUM_TRIGGERS_FOR_JOB));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, jobName);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, groupName);
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
		/// <param name="">conn
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
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual JobDetail selectJobForTrigger(OleDbConnection conn, String triggerName, String groupName,
		                                             ClassLoadHelper loadHelper)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.SELECT_JOB_FOR_TRIGGER));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, triggerName);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, groupName);
				rs = ps.ExecuteReader();

				if (rs.Read())
				{
					JobDetail job = new JobDetail();
					job.Name = Convert.ToString(rs[1 - 1]);
					job.Group = Convert.ToString(rs[2 - 1]);
					job.Durability = rs.GetBoolean(3 - 1);
					job.JobClass = loadHelper.loadClass(Convert.ToString(rs[4 - 1]));
					job.RequestsRecovery = rs.GetBoolean(5 - 1);

					return job;
				}
				else
				{
					logger.debug("No job for trigger '" + groupName + "." + triggerName + "'.");
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
					catch (OleDbException ignore)
					{
					}
				}
			}
		}

		/// <summary> <p>
		/// Select the triggers for a job
		/// </p>
		/// 
		/// </summary>
		/// <param name="">conn
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
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual Trigger[] selectTriggersForJob(OleDbConnection conn, String jobName, String groupName)
		{
			ArrayList trigList = new ArrayList();
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.SELECT_TRIGGERS_FOR_JOB));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, jobName);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, groupName);
				rs = ps.ExecuteReader();

				while (rs.Read())
				{
					trigList.Add(
						selectTrigger(conn, Convert.ToString(rs[Constants_Fields.COL_TRIGGER_NAME]),
						              Convert.ToString(rs[Constants_Fields.COL_TRIGGER_GROUP])));
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

			return (Trigger[]) SupportClass.ICollectionSupport.ToArray(trigList, new Trigger[trigList.Count]);
		}

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual Trigger[] selectTriggersForCalendar(OleDbConnection conn, String calName)
		{
			ArrayList trigList = new ArrayList();
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         rtp(StdJDBCConstants_Fields.SELECT_TRIGGERS_FOR_CALENDAR));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, calName);
				rs = ps.ExecuteReader();

				while (rs.Read())
				{
					trigList.Add(
						selectTrigger(conn, Convert.ToString(rs[Constants_Fields.COL_TRIGGER_NAME]),
						              Convert.ToString(rs[Constants_Fields.COL_TRIGGER_GROUP])));
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

			return (Trigger[]) SupportClass.ICollectionSupport.ToArray(trigList, new Trigger[trigList.Count]);
		}

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual IList selectStatefulJobsOfTriggerGroup(OleDbConnection conn, String groupName)
		{
			ArrayList jobList = new ArrayList();
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         rtp(
					                                                         	StdJDBCConstants_Fields.
					                                                         		SELECT_STATEFUL_JOBS_OF_TRIGGER_GROUP));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, groupName);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, true);
				rs = ps.ExecuteReader();

				while (rs.Read())
				{
					jobList.Add(
						new Key(Convert.ToString(rs[Constants_Fields.COL_JOB_NAME]), Convert.ToString(rs[Constants_Fields.COL_JOB_GROUP])));
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
		/// <param name="">conn
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
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual Trigger selectTrigger(OleDbConnection conn, String triggerName, String groupName)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				Trigger trigger = null;

				ps = SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.SELECT_TRIGGER));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, triggerName);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, groupName);
				rs = ps.ExecuteReader();

				if (rs.Read())
				{
					String jobName = Convert.ToString(rs[Constants_Fields.COL_JOB_NAME]);
					String jobGroup = Convert.ToString(rs[Constants_Fields.COL_JOB_GROUP]);
					bool volatility = Convert.ToBoolean(rs[Constants_Fields.COL_IS_VOLATILE]);
					String description = Convert.ToString(rs[Constants_Fields.COL_DESCRIPTION]);
					long nextFireTime = Convert.ToInt64(rs[Constants_Fields.COL_NEXT_FIRE_TIME]);
					long prevFireTime = Convert.ToInt64(rs[Constants_Fields.COL_PREV_FIRE_TIME]);
					String triggerType = Convert.ToString(rs[Constants_Fields.COL_TRIGGER_TYPE]);
					long startTime = Convert.ToInt64(rs[Constants_Fields.COL_START_TIME]);
					long endTime = Convert.ToInt64(rs[Constants_Fields.COL_END_TIME]);
					String calendarName = Convert.ToString(rs[Constants_Fields.COL_CALENDAR_NAME]);
					int misFireInstr = Convert.ToInt32(rs[Constants_Fields.COL_MISFIRE_INSTRUCTION]);

					IDictionary map = null;
					if (canUseProperties())
					{
						map = getMapFromProperties(rs);
					}
					else
					{
						map = (IDictionary) getObjectFromBlob(rs, Constants_Fields.COL_JOB_DATAMAP);
					}

					//UPGRADE_TODO: The 'DateTime' structure does not have an equivalent to NULL. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1291_3"'
					DateTime nft = null;
					if (nextFireTime > 0)
					{
						//UPGRADE_TODO: Constructor 'java.util.Date.Date' was converted to 'DateTime.DateTime' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilDateDate_long_3"'
						nft = new DateTime(nextFireTime);
					}
					//UPGRADE_TODO: The 'DateTime' structure does not have an equivalent to NULL. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1291_3"'
					DateTime pft = null;
					if (prevFireTime > 0)
					{
						//UPGRADE_TODO: Constructor 'java.util.Date.Date' was converted to 'DateTime.DateTime' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilDateDate_long_3"'
						pft = new DateTime(prevFireTime);
					}
					//UPGRADE_TODO: Constructor 'java.util.Date.Date' was converted to 'DateTime.DateTime' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilDateDate_long_3"'
					DateTime startTimeD = new DateTime(startTime);
					//UPGRADE_TODO: The 'DateTime' structure does not have an equivalent to NULL. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1291_3"'
					DateTime endTimeD = null;
					if (endTime > 0)
					{
						//UPGRADE_TODO: Constructor 'java.util.Date.Date' was converted to 'DateTime.DateTime' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilDateDate_long_3"'
						endTimeD = new DateTime(endTime);
					}

					rs.Close();
					//UPGRADE_ISSUE: Method 'java.sql.Statement.close' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlStatementclose_3"'
					ps.close();

					if (triggerType.Equals(Constants_Fields.TTYPE_SIMPLE))
					{
						ps =
							SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.SELECT_SIMPLE_TRIGGER));
						SupportClass.TransactionManager.manager.SetValue(ps, 1, triggerName);
						SupportClass.TransactionManager.manager.SetValue(ps, 2, groupName);
						rs = ps.ExecuteReader();

						if (rs.Read())
						{
							int repeatCount = Convert.ToInt32(rs[Constants_Fields.COL_REPEAT_COUNT]);
							long repeatInterval = Convert.ToInt64(rs[Constants_Fields.COL_REPEAT_INTERVAL]);
							int timesTriggered = Convert.ToInt32(rs[Constants_Fields.COL_TIMES_TRIGGERED]);

							//UPGRADE_NOTE: ref keyword was added to struct-type parameters. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1303_3"'
							SimpleTrigger st =
								new SimpleTrigger(triggerName, groupName, jobName, jobGroup, ref startTimeD, ref endTimeD, repeatCount,
								                  repeatInterval);
							st.CalendarName = calendarName;
							st.MisfireInstruction = misFireInstr;
							st.TimesTriggered = timesTriggered;
							st.Volatility = volatility;
							//UPGRADE_NOTE: ref keyword was added to struct-type parameters. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1303_3"'
							st.SetNextFireTime(ref nft);
							//UPGRADE_NOTE: ref keyword was added to struct-type parameters. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1303_3"'
							st.SetPreviousFireTime(ref pft);
							st.Description = description;
							if (null != map)
							{
								st.JobDataMap = new JobDataMap(map);
							}
							trigger = st;
						}
					}
					else if (triggerType.Equals(Constants_Fields.TTYPE_CRON))
					{
						ps =
							SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.SELECT_CRON_TRIGGER));
						SupportClass.TransactionManager.manager.SetValue(ps, 1, triggerName);
						SupportClass.TransactionManager.manager.SetValue(ps, 2, groupName);
						rs = ps.ExecuteReader();

						if (rs.Read())
						{
							String cronExpr = Convert.ToString(rs[Constants_Fields.COL_CRON_EXPRESSION]);
							String timeZoneId = Convert.ToString(rs[Constants_Fields.COL_TIME_ZONE_ID]);

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
									new CronTrigger(triggerName, groupName, jobName, jobGroup, ref startTimeD, ref endTimeD, cronExpr, timeZone);
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
								//UPGRADE_NOTE: ref keyword was added to struct-type parameters. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1303_3"'
								ct.SetNextFireTime(ref nft);
								//UPGRADE_NOTE: ref keyword was added to struct-type parameters. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1303_3"'
								ct.SetPreviousFireTime(ref pft);
								ct.Description = description;
								if (null != map)
								{
									ct.JobDataMap = new JobDataMap(map);
								}
								trigger = ct;
							}
						}
					}
					else if (triggerType.Equals(Constants_Fields.TTYPE_BLOB))
					{
						ps =
							SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.SELECT_BLOB_TRIGGER));
						SupportClass.TransactionManager.manager.SetValue(ps, 1, triggerName);
						SupportClass.TransactionManager.manager.SetValue(ps, 2, groupName);
						rs = ps.ExecuteReader();

						if (rs.Read())
						{
							trigger = (Trigger) getObjectFromBlob(rs, Constants_Fields.COL_BLOB);
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
		/// <param name="">conn
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
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual JobDataMap selectTriggerJobDataMap(OleDbConnection conn, String triggerName, String groupName)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				Trigger trigger = null;

				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.SELECT_TRIGGER_DATA));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, triggerName);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, groupName);
				rs = ps.ExecuteReader();

				if (rs.Read())
				{
					IDictionary map = null;
					if (canUseProperties())
					{
						map = getMapFromProperties(rs);
					}
					else
					{
						map = (IDictionary) getObjectFromBlob(rs, Constants_Fields.COL_JOB_DATAMAP);
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
		/// <param name="">conn
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
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual String selectTriggerState(OleDbConnection conn, String triggerName, String groupName)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				String state = null;

				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.SELECT_TRIGGER_STATE));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, triggerName);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, groupName);
				rs = ps.ExecuteReader();

				if (rs.Read())
				{
					state = Convert.ToString(rs[Constants_Fields.COL_TRIGGER_STATE]);
				}
				else
				{
					state = Constants_Fields.STATE_DELETED;
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
		/// <param name="">conn
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
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual TriggerStatus selectTriggerStatus(OleDbConnection conn, String triggerName, String groupName)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				TriggerStatus status = null;

				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.SELECT_TRIGGER_STATUS));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, triggerName);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, groupName);
				rs = ps.ExecuteReader();

				if (rs.Read())
				{
					String state = Convert.ToString(rs[Constants_Fields.COL_TRIGGER_STATE]);
					long nextFireTime = Convert.ToInt64(rs[Constants_Fields.COL_NEXT_FIRE_TIME]);
					String jobName = Convert.ToString(rs[Constants_Fields.COL_JOB_NAME]);
					String jobGroup = Convert.ToString(rs[Constants_Fields.COL_JOB_GROUP]);

					//UPGRADE_TODO: The 'DateTime' structure does not have an equivalent to NULL. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1291_3"'
					DateTime nft = null;
					if (nextFireTime > 0)
					{
						//UPGRADE_TODO: Constructor 'java.util.Date.Date' was converted to 'DateTime.DateTime' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilDateDate_long_3"'
						nft = new DateTime(nextFireTime);
					}

					//UPGRADE_NOTE: ref keyword was added to struct-type parameters. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1303_3"'
					status = new TriggerStatus(state, ref nft);
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
		/// <param name="">conn
		/// the DB Connection
		/// </param>
		/// <returns> the total number of triggers stored
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int selectNumTriggers(OleDbConnection conn)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				int count = 0;
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.SELECT_NUM_TRIGGERS));
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
		/// <param name="">conn
		/// the DB Connection
		/// </param>
		/// <returns> an array of <code>String</code> group names
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual String[] selectTriggerGroups(OleDbConnection conn)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.SELECT_TRIGGER_GROUPS));
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
		/// <param name="">conn
		/// the DB Connection
		/// </param>
		/// <param name="">groupName
		/// the group containing the triggers
		/// </param>
		/// <returns> an array of <code>String</code> trigger names
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual String[] selectTriggersInGroup(OleDbConnection conn, String groupName)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         rtp(StdJDBCConstants_Fields.SELECT_TRIGGERS_IN_GROUP));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, groupName);
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

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int insertPausedTriggerGroup(OleDbConnection conn, String groupName)
		{
			OleDbCommand ps = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         rtp(StdJDBCConstants_Fields.INSERT_PAUSED_TRIGGER_GROUP));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, groupName);
				int rows = SupportClass.TransactionManager.manager.ExecuteUpdate(ps);

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

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int deletePausedTriggerGroup(OleDbConnection conn, String groupName)
		{
			OleDbCommand ps = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         rtp(StdJDBCConstants_Fields.DELETE_PAUSED_TRIGGER_GROUP));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, groupName);
				int rows = SupportClass.TransactionManager.manager.ExecuteUpdate(ps);

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

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int deleteAllPausedTriggerGroups(OleDbConnection conn)
		{
			OleDbCommand ps = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         rtp(StdJDBCConstants_Fields.DELETE_PAUSED_TRIGGER_GROUPS));
				int rows = SupportClass.TransactionManager.manager.ExecuteUpdate(ps);

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

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual bool isTriggerGroupPaused(OleDbConnection conn, String groupName)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         rtp(StdJDBCConstants_Fields.SELECT_PAUSED_TRIGGER_GROUP));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, groupName);
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

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual bool isExistingTriggerGroup(OleDbConnection conn, String groupName)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         rtp(StdJDBCConstants_Fields.SELECT_NUM_TRIGGERS_IN_GROUP));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, groupName);
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
		/// <param name="">conn
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
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int insertCalendar(OleDbConnection conn, String calendarName, ICalendar calendar)
		{
			MemoryStream baos = serializeObject(calendar);

			OleDbCommand ps = null;

			try
			{
				ps = SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.INSERT_CALENDAR));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, calendarName);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, SupportClass.ToSByteArray(baos.ToArray()));

				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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
		/// <param name="">conn
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
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int updateCalendar(OleDbConnection conn, String calendarName, ICalendar calendar)
		{
			MemoryStream baos = serializeObject(calendar);

			OleDbCommand ps = null;

			try
			{
				ps = SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.UPDATE_CALENDAR));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, SupportClass.ToSByteArray(baos.ToArray()));
				SupportClass.TransactionManager.manager.SetValue(ps, 2, calendarName);

				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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
		/// <param name="">conn
		/// the DB Connection
		/// </param>
		/// <param name="">calendarName
		/// the name of the calendar
		/// </param>
		/// <returns> true if the trigger exists, false otherwise
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual bool calendarExists(OleDbConnection conn, String calendarName)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         rtp(StdJDBCConstants_Fields.SELECT_CALENDAR_EXISTENCE));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, calendarName);
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
		/// <param name="">conn
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
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual ICalendar selectCalendar(OleDbConnection conn, String calendarName)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;
			try
			{
				String selCal = rtp(StdJDBCConstants_Fields.SELECT_CALENDAR);
				ps = SupportClass.TransactionManager.manager.PrepareStatement(conn, selCal);
				SupportClass.TransactionManager.manager.SetValue(ps, 1, calendarName);
				rs = ps.ExecuteReader();

				ICalendar cal = null;
				if (rs.Read())
				{
					cal = (ICalendar) getObjectFromBlob(rs, Constants_Fields.COL_CALENDAR);
				}
				if (null == cal)
				{
					logger.warn("Couldn't find calendar with name '" + calendarName + "'.");
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
		/// <param name="">conn
		/// the DB Connection
		/// </param>
		/// <param name="">calendarName
		/// the name of the calendar
		/// </param>
		/// <returns> true if any triggers reference the calendar, false otherwise
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual bool calendarIsReferenced(OleDbConnection conn, String calendarName)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;
			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         rtp(StdJDBCConstants_Fields.SELECT_REFERENCED_CALENDAR));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, calendarName);
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
		/// <param name="">conn
		/// the DB Connection
		/// </param>
		/// <param name="">calendarName
		/// the name of the trigger
		/// </param>
		/// <returns> the number of rows deleted
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int deleteCalendar(OleDbConnection conn, String calendarName)
		{
			OleDbCommand ps = null;

			try
			{
				ps = SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.DELETE_CALENDAR));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, calendarName);

				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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
		/// <param name="">conn
		/// the DB Connection
		/// </param>
		/// <returns> the total number of calendars stored
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int selectNumCalendars(OleDbConnection conn)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				int count = 0;
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.SELECT_NUM_CALENDARS));

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
		/// <param name="">conn
		/// the DB Connection
		/// </param>
		/// <returns> an array of <code>String</code> calendar names
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual String[] selectCalendars(OleDbConnection conn)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps = SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.SELECT_CALENDARS));
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
		/// <param name="">conn
		/// the DB Connection
		/// </param>
		/// <returns> the next fire time, or 0 if no trigger will be fired
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual long selectNextFireTime(OleDbConnection conn)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;
			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.SELECT_NEXT_FIRE_TIME));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, Constants_Fields.STATE_WAITING);
				rs = ps.ExecuteReader();

				if (rs.Read())
				{
					return Convert.ToInt64(rs[Constants_Fields.ALIAS_COL_NEXT_FIRE_TIME]);
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
		/// <param name="">conn
		/// the DB Connection
		/// </param>
		/// <param name="">fireTime
		/// the time that the trigger will be fired
		/// </param>
		/// <returns> a <code>{@link org.quartz.utils.Key}</code> representing the
		/// trigger that will be fired at the given fire time, or null if no
		/// trigger will be fired at that time
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual Key selectTriggerForFireTime(OleDbConnection conn, long fireTime)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;
			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         rtp(StdJDBCConstants_Fields.SELECT_TRIGGER_FOR_FIRE_TIME));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, Constants_Fields.STATE_WAITING);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, Decimal.Parse(Convert.ToString(fireTime), NumberStyles.Any));
				rs = ps.ExecuteReader();

				if (rs.Read())
				{
					return
						new Key(Convert.ToString(rs[Constants_Fields.COL_TRIGGER_NAME]),
						        Convert.ToString(rs[Constants_Fields.COL_TRIGGER_GROUP]));
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
					catch (OleDbException ignore)
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
		/// <param name="">conn
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
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int insertFiredTrigger(OleDbConnection conn, Trigger trigger, String state, JobDetail job)
		{
			OleDbCommand ps = null;
			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.INSERT_FIRED_TRIGGER));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, trigger.FireInstanceId);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, trigger.Name);
				SupportClass.TransactionManager.manager.SetValue(ps, 3, trigger.Group);
				SupportClass.TransactionManager.manager.SetValue(ps, 4, trigger.Volatile);
				SupportClass.TransactionManager.manager.SetValue(ps, 5, instanceId);
				//UPGRADE_TODO: Method 'java.util.Date.getTime' was converted to 'DateTime.Ticks' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilDategetTime_3"'
				SupportClass.TransactionManager.manager.SetValue(ps, 6,
				                                                 Decimal.Parse(Convert.ToString(trigger.GetNextFireTime().Ticks),
				                                                               NumberStyles.Any));
				SupportClass.TransactionManager.manager.SetValue(ps, 7, state);
				if (job != null)
				{
					SupportClass.TransactionManager.manager.SetValue(ps, 8, trigger.JobName);
					SupportClass.TransactionManager.manager.SetValue(ps, 9, trigger.JobGroup);
					SupportClass.TransactionManager.manager.SetValue(ps, 10, job.Stateful);
					SupportClass.TransactionManager.manager.SetValue(ps, 11, job.requestsRecovery());
				}
				else
				{
					SupportClass.TransactionManager.manager.SetValue(ps, 8, null);
					SupportClass.TransactionManager.manager.SetValue(ps, 9, null);
					SupportClass.TransactionManager.manager.SetValue(ps, 10, false);
					SupportClass.TransactionManager.manager.SetValue(ps, 11, false);
				}

				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual IList selectFiredTriggerRecords(OleDbConnection conn, String triggerName, String groupName)
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
						SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.SELECT_FIRED_TRIGGER));
					SupportClass.TransactionManager.manager.SetValue(ps, 1, triggerName);
					SupportClass.TransactionManager.manager.SetValue(ps, 2, groupName);
				}
				else
				{
					ps =
						SupportClass.TransactionManager.manager.PrepareStatement(conn,
						                                                         rtp(StdJDBCConstants_Fields.SELECT_FIRED_TRIGGER_GROUP));
					SupportClass.TransactionManager.manager.SetValue(ps, 1, groupName);
				}
				rs = ps.ExecuteReader();

				while (rs.Read())
				{
					FiredTriggerRecord rec = new FiredTriggerRecord();

					rec.FireInstanceId = Convert.ToString(rs[Constants_Fields.COL_ENTRY_ID]);
					rec.FireInstanceState = Convert.ToString(rs[Constants_Fields.COL_ENTRY_STATE]);
					rec.FireTimestamp = Convert.ToInt64(rs[Constants_Fields.COL_FIRED_TIME]);
					rec.SchedulerInstanceId = Convert.ToString(rs[Constants_Fields.COL_INSTANCE_NAME]);
					rec.TriggerIsVolatile = Convert.ToBoolean(rs[Constants_Fields.COL_IS_VOLATILE]);
					rec.TriggerKey =
						new Key(Convert.ToString(rs[Constants_Fields.COL_TRIGGER_NAME]),
						        Convert.ToString(rs[Constants_Fields.COL_TRIGGER_GROUP]));
					if (!rec.FireInstanceState.Equals(Constants_Fields.STATE_ACQUIRED))
					{
						rec.JobIsStateful = Convert.ToBoolean(rs[Constants_Fields.COL_IS_STATEFUL]);
						rec.JobRequestsRecovery = Convert.ToBoolean(rs[Constants_Fields.COL_REQUESTS_RECOVERY]);
						rec.JobKey =
							new Key(Convert.ToString(rs[Constants_Fields.COL_JOB_NAME]), Convert.ToString(rs[Constants_Fields.COL_JOB_GROUP]));
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
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual IList selectFiredTriggerRecordsByJob(OleDbConnection conn, String jobName, String groupName)
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
						                                                         rtp(StdJDBCConstants_Fields.SELECT_FIRED_TRIGGERS_OF_JOB));
					SupportClass.TransactionManager.manager.SetValue(ps, 1, jobName);
					SupportClass.TransactionManager.manager.SetValue(ps, 2, groupName);
				}
				else
				{
					ps =
						SupportClass.TransactionManager.manager.PrepareStatement(conn,
						                                                         rtp(
						                                                         	StdJDBCConstants_Fields.
						                                                         		SELECT_FIRED_TRIGGERS_OF_JOB_GROUP));
					SupportClass.TransactionManager.manager.SetValue(ps, 1, groupName);
				}
				rs = ps.ExecuteReader();

				while (rs.Read())
				{
					FiredTriggerRecord rec = new FiredTriggerRecord();

					rec.FireInstanceId = Convert.ToString(rs[Constants_Fields.COL_ENTRY_ID]);
					rec.FireInstanceState = Convert.ToString(rs[Constants_Fields.COL_ENTRY_STATE]);
					rec.FireTimestamp = Convert.ToInt64(rs[Constants_Fields.COL_FIRED_TIME]);
					rec.SchedulerInstanceId = Convert.ToString(rs[Constants_Fields.COL_INSTANCE_NAME]);
					rec.TriggerIsVolatile = Convert.ToBoolean(rs[Constants_Fields.COL_IS_VOLATILE]);
					rec.TriggerKey =
						new Key(Convert.ToString(rs[Constants_Fields.COL_TRIGGER_NAME]),
						        Convert.ToString(rs[Constants_Fields.COL_TRIGGER_GROUP]));
					if (!rec.FireInstanceState.Equals(Constants_Fields.STATE_ACQUIRED))
					{
						rec.JobIsStateful = Convert.ToBoolean(rs[Constants_Fields.COL_IS_STATEFUL]);
						rec.JobRequestsRecovery = Convert.ToBoolean(rs[Constants_Fields.COL_REQUESTS_RECOVERY]);
						rec.JobKey =
							new Key(Convert.ToString(rs[Constants_Fields.COL_JOB_NAME]), Convert.ToString(rs[Constants_Fields.COL_JOB_GROUP]));
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

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual IList selectInstancesFiredTriggerRecords(OleDbConnection conn, String instanceName)
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
					                                                         rtp(
					                                                         	StdJDBCConstants_Fields.SELECT_INSTANCES_FIRED_TRIGGERS));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, instanceName);
				rs = ps.ExecuteReader();

				while (rs.Read())
				{
					FiredTriggerRecord rec = new FiredTriggerRecord();

					rec.FireInstanceId = Convert.ToString(rs[Constants_Fields.COL_ENTRY_ID]);
					rec.FireInstanceState = Convert.ToString(rs[Constants_Fields.COL_ENTRY_STATE]);
					rec.FireTimestamp = Convert.ToInt64(rs[Constants_Fields.COL_FIRED_TIME]);
					rec.SchedulerInstanceId = Convert.ToString(rs[Constants_Fields.COL_INSTANCE_NAME]);
					rec.TriggerIsVolatile = Convert.ToBoolean(rs[Constants_Fields.COL_IS_VOLATILE]);
					rec.TriggerKey =
						new Key(Convert.ToString(rs[Constants_Fields.COL_TRIGGER_NAME]),
						        Convert.ToString(rs[Constants_Fields.COL_TRIGGER_GROUP]));
					if (!rec.FireInstanceState.Equals(Constants_Fields.STATE_ACQUIRED))
					{
						rec.JobIsStateful = Convert.ToBoolean(rs[Constants_Fields.COL_IS_STATEFUL]);
						rec.JobRequestsRecovery = Convert.ToBoolean(rs[Constants_Fields.COL_REQUESTS_RECOVERY]);
						rec.JobKey =
							new Key(Convert.ToString(rs[Constants_Fields.COL_JOB_NAME]), Convert.ToString(rs[Constants_Fields.COL_JOB_GROUP]));
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
		/// <param name="">conn
		/// the DB Connection
		/// </param>
		/// <param name="">entryId
		/// the fired trigger entry to delete
		/// </param>
		/// <returns> the number of rows deleted
		/// </returns>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int deleteFiredTrigger(OleDbConnection conn, String entryId)
		{
			OleDbCommand ps = null;
			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.DELETE_FIRED_TRIGGER));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, entryId);

				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int selectJobExecutionCount(OleDbConnection conn, String jobName, String jobGroup)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         rtp(StdJDBCConstants_Fields.SELECT_JOB_EXECUTION_COUNT));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, jobName);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, jobGroup);

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

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int deleteVolatileFiredTriggers(OleDbConnection conn)
		{
			OleDbCommand ps = null;
			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         rtp(StdJDBCConstants_Fields.DELETE_VOLATILE_FIRED_TRIGGERS));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, true);

				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int insertSchedulerState(OleDbConnection conn, String instanceId, long checkInTime, long interval,
		                                        String recoverer)
		{
			OleDbCommand ps = null;
			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.INSERT_SCHEDULER_STATE));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, instanceId);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, checkInTime);
				SupportClass.TransactionManager.manager.SetValue(ps, 3, interval);
				SupportClass.TransactionManager.manager.SetValue(ps, 4, recoverer);

				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int deleteSchedulerState(OleDbConnection conn, String instanceId)
		{
			OleDbCommand ps = null;
			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.DELETE_SCHEDULER_STATE));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, instanceId);

				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual int updateSchedulerState(OleDbConnection conn, String instanceId, long checkInTime)
		{
			OleDbCommand ps = null;
			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.UPDATE_SCHEDULER_STATE));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, checkInTime);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, instanceId);

				return SupportClass.TransactionManager.manager.ExecuteUpdate(ps);
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

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual IList selectSchedulerStateRecords(OleDbConnection conn, String instanceId)
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
						SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.SELECT_SCHEDULER_STATE));
					SupportClass.TransactionManager.manager.SetValue(ps, 1, instanceId);
				}
				else
				{
					ps =
						SupportClass.TransactionManager.manager.PrepareStatement(conn,
						                                                         rtp(StdJDBCConstants_Fields.SELECT_SCHEDULER_STATES));
				}
				rs = ps.ExecuteReader();

				while (rs.Read())
				{
					SchedulerStateRecord rec = new SchedulerStateRecord();

					rec.SchedulerInstanceId = Convert.ToString(rs[Constants_Fields.COL_INSTANCE_NAME]);
					rec.CheckinTimestamp = Convert.ToInt64(rs[Constants_Fields.COL_LAST_CHECKIN_TIME]);
					rec.CheckinInterval = Convert.ToInt64(rs[Constants_Fields.COL_CHECKIN_INTERVAL]);
					rec.Recoverer = Convert.ToString(rs[Constants_Fields.COL_RECOVERER]);

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
		protected internal String rtp(String query)
		{
			return Util.rtp(query, tablePrefix);
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
		protected internal virtual MemoryStream serializeObject(Object obj)
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
		protected internal virtual MemoryStream serializeJobData(JobDataMap data)
		{
			if (canUseProperties())
			{
				return serializeProperties(data);
			}

			if (null != data)
			{
				data.removeTransientData();
				return serializeObject(data);
			}
			else
			{
				return serializeObject((Object) null);
			}
		}

		/// <summary> serialize the java.util.Properties</summary>
		private MemoryStream serializeProperties(JobDataMap data)
		{
			MemoryStream ba = new MemoryStream();
			if (null != data)
			{
				//UPGRADE_ISSUE: Class hierarchy differences between 'java.util.Properties' and 'System.Collections.Specialized.NameValueCollection' may cause compilation errors. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1186_3"'
				NameValueCollection properties = convertToProperty(data.WrappedMap);
				//UPGRADE_ISSUE: Method 'java.util.Properties.store' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javautilPropertiesstore_javaioOutputStream_javalangString_3"'
				properties.store(ba, "");
			}

			return ba;
		}

		/// <summary> convert the JobDataMap into a list of properties</summary>
		//UPGRADE_ISSUE: Class hierarchy differences between 'java.util.Properties' and 'System.Collections.Specialized.NameValueCollection' may cause compilation errors. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1186_3"'
		protected internal virtual IDictionary convertFromProperty(NameValueCollection properties)
		{
			//UPGRADE_TODO: Class 'java.util.HashMap' was converted to 'System.Collections.Hashtable' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilHashMap_3"'
			IDictionary data = new Hashtable();
			SupportClass.SetSupport keys = new SupportClass.HashSetSupport(properties);
			IEnumerator it = keys.GetEnumerator();
			//UPGRADE_TODO: Method 'java.util.Iterator.hasNext' was converted to 'System.Collections.IEnumerator.MoveNext' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratorhasNext_3"'
			while (it.MoveNext())
			{
				//UPGRADE_TODO: Method 'java.util.Iterator.next' was converted to 'System.Collections.IEnumerator.Current' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratornext_3"'
				Object key = it.Current;
				Object val = properties[(String) key];
				data[key] = val;
			}

			return data;
		}

		/// <summary> convert the JobDataMap into a list of properties</summary>
		//UPGRADE_ISSUE: Class hierarchy differences between 'java.util.Properties' and 'System.Collections.Specialized.NameValueCollection' may cause compilation errors. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1186_3"'
		protected internal virtual NameValueCollection convertToProperty(IDictionary data)
		{
			//UPGRADE_ISSUE: Class hierarchy differences between 'java.util.Properties' and 'System.Collections.Specialized.NameValueCollection' may cause compilation errors. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1186_3"'
			//UPGRADE_TODO: Format of property file may need to be changed. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1089_3"'
			NameValueCollection properties = new NameValueCollection();
			//UPGRADE_TODO: Method 'java.util.Map.keySet' was converted to 'SupportClass.HashSetSupport' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilMapkeySet_3"'
			SupportClass.SetSupport keys = new SupportClass.HashSetSupport(data.Keys);
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

		/// <summary> <p>
		/// This method should be overridden by any delegate subclasses that need
		/// special handling for BLOBs. The default implementation uses standard
		/// JDBC <code>java.sql.Blob</code> operations.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">rs
		/// the result set, already queued to the correct row
		/// </param>
		/// <param name="">colName
		/// the column name for the BLOB
		/// </param>
		/// <returns> the deserialized Object from the ResultSet BLOB
		/// </returns>
		/// <throws>  ClassNotFoundException </throws>
		/// <summary>           if a class found during deserialization cannot be found
		/// </summary>
		/// <throws>  IOException </throws>
		/// <summary>           if deserialization causes an error
		/// </summary>
		//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
		protected internal virtual Object getObjectFromBlob(OleDbDataReader rs, String colName)
		{
			Object obj = null;

			//UPGRADE_TODO: Interface 'java.sql.Blob' was converted to 'System.Byte[]' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlBlob_3"'
			//UPGRADE_TODO: Method 'java.sql.ResultSet.getBlob' was converted to 'SupportClass.GetBlob(System.Data.OleDb.OleDbDataReader, string)' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSetgetBlob_javalangString_3"'
			Byte[] blobLocator = SupportClass.GetBlob(rs, colName);
			if (blobLocator != null)
			{
				//UPGRADE_ISSUE: Method 'java.sql.Blob.getBinaryStream' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlBlobgetBinaryStream_3"'
				Stream binaryInput = blobLocator.getBinaryStream();

				if (null != binaryInput)
				{
					//UPGRADE_TODO: Class 'java.io.ObjectInputStream' was converted to 'System.IO.BinaryReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javaioObjectInputStream_3"'
					BinaryReader in_Renamed = new BinaryReader(binaryInput);
					//UPGRADE_WARNING: Method 'java.io.ObjectInputStream.readObject' was converted to 'SupportClass.Deserialize' which may throw an exception. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1101_3"'
					obj = SupportClass.Deserialize(in_Renamed);
					in_Renamed.Close();
				}
			}
			return obj;
		}

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual Key[] selectVolatileTriggers(OleDbConnection conn)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         rtp(StdJDBCConstants_Fields.SELECT_VOLATILE_TRIGGERS));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, true);
				rs = ps.ExecuteReader();

				ArrayList list = new ArrayList();
				while (rs.Read())
				{
					String triggerName = Convert.ToString(rs[Constants_Fields.COL_TRIGGER_NAME]);
					String groupName = Convert.ToString(rs[Constants_Fields.COL_TRIGGER_GROUP]);
					list.Add(new Key(triggerName, groupName));
				}
				Object[] oArr = list.ToArray();
				Key[] kArr = new Key[oArr.Length];
				Array.Copy(oArr, 0, kArr, 0, oArr.Length);
				return kArr;
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

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual Key[] selectVolatileJobs(OleDbConnection conn)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.SELECT_VOLATILE_JOBS));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, true);
				rs = ps.ExecuteReader();

				ArrayList list = new ArrayList();
				while (rs.Read())
				{
					String triggerName = Convert.ToString(rs[Constants_Fields.COL_JOB_NAME]);
					String groupName = Convert.ToString(rs[Constants_Fields.COL_JOB_GROUP]);
					list.Add(new Key(triggerName, groupName));
				}
				Object[] oArr = list.ToArray();
				Key[] kArr = new Key[oArr.Length];
				Array.Copy(oArr, 0, kArr, 0, oArr.Length);
				return kArr;
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
		/// This method should be overridden by any delegate subclasses that need
		/// special handling for BLOBs for job details. The default implementation
		/// uses standard JDBC <code>java.sql.Blob</code> operations.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">rs
		/// the result set, already queued to the correct row
		/// </param>
		/// <param name="">colName
		/// the column name for the BLOB
		/// </param>
		/// <returns> the deserialized Object from the ResultSet BLOB
		/// </returns>
		/// <throws>  ClassNotFoundException </throws>
		/// <summary>           if a class found during deserialization cannot be found
		/// </summary>
		/// <throws>  IOException </throws>
		/// <summary>           if deserialization causes an error
		/// </summary>
		//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
		protected internal virtual Object getJobDetailFromBlob(OleDbDataReader rs, String colName)
		{
			if (canUseProperties())
			{
				//UPGRADE_TODO: Interface 'java.sql.Blob' was converted to 'System.Byte[]' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlBlob_3"'
				//UPGRADE_TODO: Method 'java.sql.ResultSet.getBlob' was converted to 'SupportClass.GetBlob(System.Data.OleDb.OleDbDataReader, string)' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSetgetBlob_javalangString_3"'
				Byte[] blobLocator = SupportClass.GetBlob(rs, colName);
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

			return getObjectFromBlob(rs, colName);
		}

		/// <seealso cref="org.quartz.impl.jdbcjobstore.DriverDelegate#selectPausedTriggerGroups(java.sql.Connection)">
		/// </seealso>
		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual SupportClass.SetSupport selectPausedTriggerGroups(OleDbConnection conn)
		{
			OleDbCommand ps = null;
			//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
			OleDbDataReader rs = null;

			//UPGRADE_TODO: Class 'java.util.HashSet' was converted to 'SupportClass.HashSetSupport' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilHashSet_3"'
			SupportClass.HashSetSupport set_Renamed = new SupportClass.HashSetSupport();
			try
			{
				ps =
					SupportClass.TransactionManager.manager.PrepareStatement(conn,
					                                                         rtp(StdJDBCConstants_Fields.SELECT_PAUSED_TRIGGER_GROUPS));
				rs = ps.ExecuteReader();

				while (rs.Read())
				{
					String groupName = Convert.ToString(rs[Constants_Fields.COL_TRIGGER_GROUP]);
					set_Renamed.Add(groupName);
				}
				return set_Renamed;
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
	}

	// EOF
}