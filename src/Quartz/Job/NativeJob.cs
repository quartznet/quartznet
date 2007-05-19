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
using System.Diagnostics;
using System.IO;
using System.Text;

using Common.Logging;

namespace Quartz.Job
{
	/// <summary>
	/// Built in job for executing native executables in a separate process.
	/// </summary>
	/// <author>Matthew Payne</author>
	/// <author>James House</author>
	/// <author>Steinar Overbeck Cook</author>
	public class NativeJob : IJob
	{
		private static readonly ILog Log = LogManager.GetLogger(typeof (NativeJob));

		/// <summary> 
		/// Required parameter that specifies the name of the command (executable) 
		/// to be ran.
		/// </summary>
		public const string PROP_COMMAND = "command";

		/// <summary> 
		/// Optional parameter that specifies the parameters to be passed to the
		/// executed command.
		/// </summary>
		public const string PROP_PARAMETERS = "parameters";


		/// <summary> 
		/// Optional parameter (value should be 'true' or 'false') that specifies 
		/// whether the job should wait for the execution of the native process to 
		/// complete before it completes.
		/// 
		/// <p>Defaults to <code>true</code>.</p>  
		/// </summary>
		public const string PROP_WAIT_FOR_PROCESS = "waitForProcess";

		/// <summary> 
		/// Optional parameter (value should be 'true' or 'false') that specifies 
		/// whether the spawned process's stdout and stderr streams should be 
		/// consumed.  If the process creates output, it is possible that it might
		/// 'hang' if the streams are not consumed.
		/// 
		/// <p>Defaults to <code>false</code>.</p>  
		/// </summary>
		public const string PROP_CONSUME_STREAMS = "consumeStreams";


		/*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Interface.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

		/// <summary>
		/// Called by the <code>Scheduler</code> when a <code>Trigger</code>
		/// fires that is associated with the <code>Job</code>.
		/// <p>
		/// The implementation may wish to set a  result object on the
		/// JobExecutionContext before this method exits.  The result itself
		/// is meaningless to Quartz, but may be informative to
		/// <code>JobListeners</code> or
		/// <code>TriggerListeners</code> that are watching the job's
		/// execution.
		/// </p>
		/// </summary>
		/// <param name="context"></param>
		public virtual void Execute(JobExecutionContext context)
		{
			JobDataMap data = context.JobDetail.JobDataMap;

			String command = data.GetString(PROP_COMMAND);

			String parameters = data.GetString(PROP_PARAMETERS);

			if (parameters == null)
			{
				parameters = "";
			}

			bool wait = true;
			if (data.Contains(PROP_WAIT_FOR_PROCESS))
			{
				wait = data.GetBooleanValue(PROP_WAIT_FOR_PROCESS);
			}
			bool consumeStreams = false;
			if (data.Contains(PROP_CONSUME_STREAMS))
			{
				consumeStreams = data.GetBooleanValue(PROP_CONSUME_STREAMS);
			}

			RunNativeCommand(command, parameters, wait, consumeStreams);
		}

		private void RunNativeCommand(String command, string parameters, bool wait, bool consumeStreams)
		{
			string[] cmd = null;
			string[] args = new string[2];
			args[0] = command;
			args[1] = parameters;

			try
			{
				//with this variable will be done the swithcing
				String osName = Environment.GetEnvironmentVariable("OS");

				//only will work with Windows NT
				if (osName.Equals("Windows NT"))
				{
					if (cmd == null)
					{
						cmd = new string[args.Length + 2];
					}
					cmd[0] = "cmd.exe";
					cmd[1] = "/C";
					for (int i = 0; i < args.Length; i++)
					{
						cmd[i + 2] = args[i];
					}
				}
					//only will work with Windows 95
				else if (osName.Equals("Windows 95"))
				{
					if (cmd == null)
					{
						cmd = new string[args.Length + 2];
					}
					cmd[0] = "command.com";
					cmd[1] = "/C";
					for (int i = 0; i < args.Length; i++)
					{
						cmd[i + 2] = args[i];
					}
				}
					//only will work with Windows 2003
				else if (osName.Equals("Windows 2003"))
				{
					if (cmd == null)
					{
						cmd = new string[args.Length + 2];
					}
					cmd[0] = "cmd.exe";
					cmd[1] = "/C";

					for (int i = 0; i < args.Length; i++)
					{
						cmd[i + 2] = args[i];
					}
				}
					//only will work with Windows 2000
				else if (osName.Equals("Windows 2000"))
				{
					if (cmd == null)
					{
						cmd = new string[args.Length + 2];
					}
					cmd[0] = "cmd.exe";
					cmd[1] = "/C";

					for (int i = 0; i < args.Length; i++)
					{
						cmd[i + 2] = args[i];
					}
				}
					//only will work with Windows XP
				else if (osName.Equals("Windows XP"))
				{
					if (cmd == null)
					{
						cmd = new string[args.Length + 2];
					}
					cmd[0] = "cmd.exe";
					cmd[1] = "/C";

					for (int i = 0; i < args.Length; i++)
					{
						cmd[i + 2] = args[i];
					}
				}
					//only will work with Linux
				else if (osName.Equals("Linux"))
				{
					if (cmd == null)
					{
						cmd = new string[args.Length];
					}
					cmd = args;
				}
					//will work with the rest
				else
				{
					if (cmd == null)
					{
						cmd = new string[args.Length];
					}
					cmd = args;
				}

				// Executes the command
				Log.Info(string.Format("About to run {0}{1}", cmd[0], cmd[1]));
				string temp = "";
				for (int i = 1; i < cmd.Length; i++)
				{
					if (i == 1)
					{
						temp = cmd[i];
					}
					else
					{
						temp = temp + " " + cmd[i];
					}
				}

				Process proc = Process.Start(cmd[0], temp);
				// Consumes the stdout from the process
				StreamConsumer stdoutConsumer = new StreamConsumer(this, proc.StandardInput.BaseStream, "stdout");

				// Consumes the stderr from the process
				if (consumeStreams)
				{
					StreamConsumer stderrConsumer = new StreamConsumer(this, proc.StandardError.BaseStream, "stderr");
					stdoutConsumer.Start();
					stderrConsumer.Start();
				}

				if (wait)
				{
					proc.WaitForExit();
				}
				// any error message?
			}
			catch (Exception x)
			{
				throw new JobExecutionException("Error launching native command: ", x, false);
			}
		}

		/// <summary> 
		/// Consumes data from the given input stream until EOF and prints the data to stdout
		/// </summary>
		/// <author>cooste</author>
		/// <author>James House</author>
		internal class StreamConsumer : QuartzThread
		{
			private void InitBlock(NativeJob job)
			{
				enclosingInstance = job;
			}

			private NativeJob enclosingInstance;

			public NativeJob Enclosing_Instance
			{
				get { return enclosingInstance; }
			}

			internal Stream is_Renamed;
			internal string type;

			/// <summary> </summary>
			public StreamConsumer(NativeJob enclosingInstance, Stream inputStream, string type)
			{
				InitBlock(enclosingInstance);
				is_Renamed = inputStream;
				this.type = type;
			}

			/// <summary> Runs this object as a separate thread, printing the contents of the InputStream
			/// supplied during instantiation, to either stdout or stderr
			/// </summary>
			public override void Run()
			{
				StreamReader br = null;
				try
				{
					br =
						new StreamReader(new StreamReader(is_Renamed, Encoding.Default).BaseStream,
						                 new StreamReader(is_Renamed, Encoding.Default).CurrentEncoding);
					string line = null;

					while ((line = br.ReadLine()) != null)
					{
						if (type.ToUpper().Equals("stderr".ToUpper()))
						{
							NativeJob.Log.Warn(type + ">" + line);
						}
						else
						{
							NativeJob.Log.Info(type + ">" + line);
						}
					}
				}
				catch (IOException ioe)
				{
					NativeJob.Log.Error("Error consuming " + type + " stream of spawned process.", ioe);
				}
				finally
				{
					if (br != null)
					{
						try
						{
							br.Close();
						}
						catch (Exception)
						{
						}
					}
				}
			}
		}
	}
}