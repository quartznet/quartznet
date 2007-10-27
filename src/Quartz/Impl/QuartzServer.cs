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
using System.Globalization;
using System.Threading;

using Quartz.Listener;

namespace Quartz.Impl
{
	/// <summary>
	/// Instantiates an instance of Quartz Scheduler as a stand-alone program, if
	/// the scheduler is configured for RMI it will be made available.
	/// <p>
	/// The main() method of this class currently accepts 0 or 1 arguemtns, if there
	/// is an argument, and its value is <code>"console"</code>, then the program
	/// will print a short message on the console (std-out) and wait for the user to
	/// type "exit" - at which time the scheduler will be Shutdown.
	/// </p>
	/// <p>
	/// Future versions of this server should allow additional configuration for
	/// responding to scheduler events by allowing the user to specify <code>JobListener</code>,
	/// <code>TriggerListener</code> and <code>SchedulerListener</code>
	/// classes.
	/// </p>
	/// <p>
	/// Please read the Quartz FAQ entries about RMI before asking questions in the
	/// forums or mail-lists.
	/// </p>
	/// </summary>
	/// <author>James House</author>
	/// <author>Marko Lahma (.NET)</author>
    public class QuartzServer : SchedulerListenerSupport
	{
		private IScheduler sched = null;

		internal QuartzServer()
		{
		}

		public virtual void Serve(ISchedulerFactory schedFact, bool console)
		{
			sched = schedFact.GetScheduler();

			sched.Start();

			Thread.Sleep(3000);

			Console.Out.WriteLine("\n*** The scheduler successfully started.");

			if (console)
			{
				Console.Out.WriteLine("\n");
				Console.Out.WriteLine("The scheduler will now run until you type \"exit\"");
				Console.Out.WriteLine("   If it was configured to export itself via RMI,");
				Console.Out.WriteLine("   then other process may now use it.");
				while (true)
				{
					Console.Out.Write("Type 'exit' to Shutdown the server: ");
					if ("exit".Equals(Console.ReadLine()))
					{
						break;
					}
				}

				Console.Out.WriteLine("\n...Shutting down server...");

				sched.Shutdown(true);
			}
		}

		/// <summary>
		/// Called by the <code>Scheduler</code> when a serious error has
		/// occured within the scheduler - such as repeated failures in the <code>JobStore</code>,
		/// or the inability to instantiate a <code>Job</code> instance when its
		/// <code>Trigger</code> has fired.
		/// <p>
		/// The <code>getErrorCode()</code> method of the given SchedulerException
		/// can be used to determine more specific information about the type of
		/// error that was encountered.
		/// </p>
		/// </summary>
		public override void SchedulerError(string msg, SchedulerException cause)
		{
			Console.Error.WriteLine("*** " + msg);
			Console.Error.WriteLine(cause.ToString());
		}

		/// <summary>
		/// Called by the <code>Scheduler</code> to inform the listener
		/// that it has Shutdown.
		/// </summary>
		public override void SchedulerShutdown()
		{
			Console.Out.WriteLine("\n*** The scheduler is now Shutdown.");
			sched = null;
		}

		/*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Main Method.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

		[STAThread]
		public static void Main(string[] args)
		{

			try
			{
				QuartzServer server = new QuartzServer();
				if (args.Length == 0)
				{
					server.Serve(new StdSchedulerFactory(), false);
				}
				else if (args.Length == 1 && args[0].ToUpper(CultureInfo.InvariantCulture).Equals("console".ToUpper(CultureInfo.InvariantCulture)))
				{
					server.Serve(new StdSchedulerFactory(), true);
				}
				else
				{
					Console.Error.WriteLine("\nUsage: QuartzServer [console]");
				}
			}
			catch (Exception e)
			{
				Console.Error.WriteLine(e.ToString());
			}
		}
	}
}