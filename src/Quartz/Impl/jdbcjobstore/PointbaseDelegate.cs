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
using System.Data.OleDb;
using System.Globalization;
using System.IO;
using log4net;

namespace org.quartz.impl.jdbcjobstore
{
	/// <summary> <p>
	/// This is a driver delegate for the Pointbase JDBC driver.
	/// </p>
	/// 
	/// </summary>
	/// <author>  Gregg Freeman
	/// </author>
	public class PointbaseDelegate : StdJDBCDelegate
	{
		//private static Category log =
		// Category.getInstance(PointbaseJDBCDelegate.class);
		/// <summary> <p>
		/// Create new PointbaseJDBCDelegate instance.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">logger
		/// the logger to use during execution
		/// </param>
		/// <param name="">tablePrefix
		/// the prefix of all table names
		/// </param>
		public PointbaseDelegate(ILog logger, String tablePrefix, String instanceId) : base(logger, tablePrefix, instanceId)
		{
		}

		/// <summary> <p>
		/// Create new PointbaseJDBCDelegate instance.
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
		public PointbaseDelegate(ILog logger, String tablePrefix, String instanceId, ref Boolean useProperties)
			: base(logger, tablePrefix, instanceId, useProperties)
		{
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
		public override int insertJobDetail(OleDbConnection conn, JobDetail job)
		{
			//log.debug( "Inserting JobDetail " + job );
			MemoryStream baos = serializeJobData(job.JobDataMap);
			int len = SupportClass.ToSByteArray(baos.ToArray()).Length;
			MemoryStream bais = new MemoryStream(SupportClass.ToByteArray(SupportClass.ToSByteArray(baos.ToArray())));

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
				SupportClass.TransactionManager.manager.SetValue(ps, 9, bais);

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
		public override int updateJobDetail(OleDbConnection conn, JobDetail job)
		{
			//log.debug( "Updating job detail " + job );
			MemoryStream baos = serializeJobData(job.JobDataMap);
			int len = SupportClass.ToSByteArray(baos.ToArray()).Length;
			MemoryStream bais = new MemoryStream(SupportClass.ToByteArray(SupportClass.ToSByteArray(baos.ToArray())));

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
				SupportClass.TransactionManager.manager.SetValue(ps, 7, bais);
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

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public override int insertTrigger(OleDbConnection conn, Trigger trigger, String state, JobDetail jobDetail)
		{
			MemoryStream baos = serializeJobData(trigger.JobDataMap);
			int len = SupportClass.ToSByteArray(baos.ToArray()).Length;
			MemoryStream bais = new MemoryStream(SupportClass.ToByteArray(SupportClass.ToSByteArray(baos.ToArray())));

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
				                                                 Decimal.Parse(Convert.ToString(trigger.getNextFireTime().Ticks),
				                                                               NumberStyles.Any));
				long prevFireTime = - 1;
				//UPGRADE_TODO: The 'DateTime' structure does not have an equivalent to NULL. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1291_3"'
				if (trigger.getPreviousFireTime() != null)
				{
					//UPGRADE_TODO: Method 'java.util.Date.getTime' was converted to 'DateTime.Ticks' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilDategetTime_3"'
					prevFireTime = trigger.getPreviousFireTime().Ticks;
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
				SupportClass.TransactionManager.manager.SetValue(ps, 15, bais);

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

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public override int updateTrigger(OleDbConnection conn, Trigger trigger, String state, JobDetail jobDetail)
		{
			MemoryStream baos = serializeJobData(trigger.JobDataMap);
			int len = SupportClass.ToSByteArray(baos.ToArray()).Length;
			MemoryStream bais = new MemoryStream(SupportClass.ToByteArray(SupportClass.ToSByteArray(baos.ToArray())));

			OleDbCommand ps = null;

			int insertResult = 0;


			try
			{
				ps = SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.UPDATE_TRIGGER));

				SupportClass.TransactionManager.manager.SetValue(ps, 1, trigger.JobName);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, trigger.JobGroup);
				SupportClass.TransactionManager.manager.SetValue(ps, 3, trigger.Volatile);
				SupportClass.TransactionManager.manager.SetValue(ps, 4, trigger.Description);
				long nextFireTime = - 1;
				//UPGRADE_TODO: The 'DateTime' structure does not have an equivalent to NULL. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1291_3"'
				if (trigger.getNextFireTime() != null)
				{
					//UPGRADE_TODO: Method 'java.util.Date.getTime' was converted to 'DateTime.Ticks' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilDategetTime_3"'
					nextFireTime = trigger.getNextFireTime().Ticks;
				}
				SupportClass.TransactionManager.manager.SetValue(ps, 5,
				                                                 Decimal.Parse(Convert.ToString(nextFireTime), NumberStyles.Any));
				long prevFireTime = - 1;
				//UPGRADE_TODO: The 'DateTime' structure does not have an equivalent to NULL. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1291_3"'
				if (trigger.getPreviousFireTime() != null)
				{
					//UPGRADE_TODO: Method 'java.util.Date.getTime' was converted to 'DateTime.Ticks' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilDategetTime_3"'
					prevFireTime = trigger.getPreviousFireTime().Ticks;
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
				SupportClass.TransactionManager.manager.SetValue(ps, 13, bais);
				SupportClass.TransactionManager.manager.SetValue(ps, 14, trigger.Name);
				SupportClass.TransactionManager.manager.SetValue(ps, 15, trigger.Group);

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
		public override int updateJobData(OleDbConnection conn, JobDetail job)
		{
			//log.debug( "Updating Job Data for Job " + job );
			MemoryStream baos = serializeJobData(job.JobDataMap);
			int len = SupportClass.ToSByteArray(baos.ToArray()).Length;
			MemoryStream bais = new MemoryStream(SupportClass.ToByteArray(SupportClass.ToSByteArray(baos.ToArray())));
			OleDbCommand ps = null;

			try
			{
				ps = SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.UPDATE_JOB_DATA));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, bais);
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

		//---------------------------------------------------------------------------
		// triggers
		//---------------------------------------------------------------------------

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
		public override int insertCalendar(OleDbConnection conn, String calendarName, Calendar calendar)
		{
			//log.debug( "Inserting Calendar " + calendarName + " : " + calendar
			// );
			MemoryStream baos = serializeObject(calendar);
			sbyte[] buf = SupportClass.ToSByteArray(baos.ToArray());
			MemoryStream bais = new MemoryStream(SupportClass.ToByteArray(buf));

			OleDbCommand ps = null;

			try
			{
				ps = SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.INSERT_CALENDAR));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, calendarName);
				SupportClass.TransactionManager.manager.SetValue(ps, 2, bais);

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
		public override int updateCalendar(OleDbConnection conn, String calendarName, Calendar calendar)
		{
			//log.debug( "Updating calendar " + calendarName + " : " + calendar );
			MemoryStream baos = serializeObject(calendar);
			sbyte[] buf = SupportClass.ToSByteArray(baos.ToArray());
			MemoryStream bais = new MemoryStream(SupportClass.ToByteArray(buf));

			OleDbCommand ps = null;

			try
			{
				ps = SupportClass.TransactionManager.manager.PrepareStatement(conn, rtp(StdJDBCConstants_Fields.UPDATE_CALENDAR));
				SupportClass.TransactionManager.manager.SetValue(ps, 1, bais);
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

		//---------------------------------------------------------------------------
		// protected methods that can be overridden by subclasses
		//---------------------------------------------------------------------------

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
		protected internal override Object getObjectFromBlob(OleDbDataReader rs, String colName)
		{
			//log.debug( "Getting blob from column: " + colName );
			Object obj = null;

			//UPGRADE_ISSUE: Method 'java.sql.ResultSet.getBytes' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlResultSetgetBytes_javalangString_3"'
			sbyte[] binaryData = rs.getBytes(colName);

			Stream binaryInput = new MemoryStream(SupportClass.ToByteArray(binaryData));

			if (null != binaryInput)
			{
				//UPGRADE_TODO: Class 'java.io.ObjectInputStream' was converted to 'System.IO.BinaryReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javaioObjectInputStream_3"'
				BinaryReader in_Renamed = new BinaryReader(binaryInput);
				//UPGRADE_WARNING: Method 'java.io.ObjectInputStream.readObject' was converted to 'SupportClass.Deserialize' which may throw an exception. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1101_3"'
				obj = SupportClass.Deserialize(in_Renamed);
				in_Renamed.Close();
			}

			return obj;
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
		protected internal override Object getJobDetailFromBlob(OleDbDataReader rs, String colName)
		{
			//log.debug( "Getting Job details from blob in col " + colName );
			if (canUseProperties())
			{
				//UPGRADE_ISSUE: Method 'java.sql.ResultSet.getBytes' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlResultSetgetBytes_javalangString_3"'
				sbyte[] data = rs.getBytes(colName);
				if (data == null)
				{
					return null;
				}
				Stream binaryInput = new MemoryStream(SupportClass.ToByteArray(data));
				return binaryInput;
			}

			return getObjectFromBlob(rs, colName);
		}
	}

	// EOF
}