/* 
* Copyright 2005 OpenSymphony 
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
using System;
//UPGRADE_TODO: The type 'org.apache.commons.logging.Log' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using Log = org.apache.commons.logging.Log;
//UPGRADE_TODO: The type 'org.apache.commons.logging.LogFactory' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using LogFactory = org.apache.commons.logging.LogFactory;
//UPGRADE_TODO: The type 'org.quartz.Scheduler' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using Scheduler = org.quartz.Scheduler;
//UPGRADE_TODO: The type 'org.quartz.SchedulerFactory' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using SchedulerFactory = org.quartz.SchedulerFactory;
//UPGRADE_TODO: The type 'org.quartz.SchedulerMetaData' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using SchedulerMetaData = org.quartz.SchedulerMetaData;
//UPGRADE_TODO: The type 'org.quartz.impl.StdSchedulerFactory' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using StdSchedulerFactory = org.quartz.impl.StdSchedulerFactory;
namespace org.quartz.examples.example10
{
	
	/// <summary> This example will spawn a large number of jobs to run
	/// 
	/// </summary>
	/// <author>  James House, Bill Kratzer
	/// </author>
	public class PlugInExample
	{
		
		public virtual void  run()
		{
			Log log = LogFactory.getLog(typeof(PlugInExample));
			
			// First we must get a reference to a scheduler
			SchedulerFactory sf = new StdSchedulerFactory();
			Scheduler sched = sf.getScheduler();
			
			log.info("------- Initialization Complete -----------");
			
			log.info("------- (Not Scheduling any Jobs - relying on XML definitions --");
			
			log.info("------- Starting Scheduler ----------------");
			
			// start the schedule 
			sched.start();
			
			log.info("------- Started Scheduler -----------------");
			
			log.info("------- Waiting five minutes... -----------");
			
			// wait five minutes to give our jobs a chance to run
			try
			{
				//UPGRADE_TODO: Method 'java.lang.Thread.sleep' was converted to 'System.Threading.Thread.Sleep' which has a different behavior. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1073_javalangThreadsleep_long'"
				System.Threading.Thread.Sleep(new System.TimeSpan((System.Int64) 10000 * 300L * 1000L));
			}
			catch (System.Exception e)
			{
			}
			
			// shut down the scheduler
			log.info("------- Shutting Down ---------------------");
			sched.shutdown(true);
			log.info("------- Shutdown Complete -----------------");
			
			SchedulerMetaData metaData = sched.getMetaData();
			log.info("Executed " + metaData.numJobsExecuted() + " jobs.");
		}
		
		[STAThread]
		public static void  Main(System.String[] args)
		{
			
			PlugInExample example = new PlugInExample();
			example.run();
		}
	}
}