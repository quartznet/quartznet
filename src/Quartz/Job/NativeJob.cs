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
	    private readonly ILog log;

		/// <summary> 
		/// Required parameter that specifies the name of the command (executable) 
		/// to be ran.
		/// </summary>
		public const string PropertyCommand = "command";

		/// <summary> 
		/// Optional parameter that specifies the parameters to be passed to the
		/// executed command.
		/// </summary>
		public const string PropertyParameters = "parameters";


		/// <summary> 
		/// Optional parameter (value should be 'true' or 'false') that specifies 
		/// whether the job should wait for the execution of the native process to 
		/// complete before it completes.
		/// 
		/// <p>Defaults to <see langword="true" />.</p>  
		/// </summary>
		public const string PropertyWaitForProcess = "waitForProcess";

		/// <summary> 
		/// Optional parameter (value should be 'true' or 'false') that specifies 
		/// whether the spawned process's stdout and stderr streams should be 
		/// consumed.  If the process creates output, it is possible that it might
		/// 'hang' if the streams are not consumed.
		/// 
		/// <p>Defaults to <see langword="false" />.</p>  
		/// </summary>
		public const string PropertyConsumeStreams = "consumeStreams";


        /// <summary>
        /// Gets the log.
        /// </summary>
        /// <value>The log.</value>
	    protected ILog Log
	    {
	        get { return log; }
	    }

        /// <summary>
        /// Initializes a new instance of the <see cref="NativeJob"/> class.
        /// </summary>
	    public NativeJob()
	    {
            log = LogManager.GetLogger(typeof(NativeJob));
	    }

	    /*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Interface.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

		/// <summary>
		/// Called by the <see cref="IScheduler" /> when a <see cref="Trigger" />
		/// fires that is associated with the <see cref="IJob" />.
		/// <p>
		/// The implementation may wish to set a  result object on the
		/// JobExecutionContext before this method exits.  The result itself
		/// is meaningless to Quartz, but may be informative to
		/// <see cref="IJobListener" />s or
		/// <see cref="ITriggerListener" />s that are watching the job's
		/// execution.
		/// </p>
		/// </summary>
		/// <param name="context"></param>
		public virtual void Execute(JobExecutionContext context)
		{
			JobDataMap data = context.MergedJobDataMap;

			String command = data.GetString(PropertyCommand);

			String parameters = data.GetString(PropertyParameters);

			if (parameters == null)
			{
				parameters = "";
			}

			bool wait = true;
			if (data.Contains(PropertyWaitForProcess))
			{
				wait = data.GetBooleanValue(PropertyWaitForProcess);
			}
			bool consumeStreams = false;
			if (data.Contains(PropertyConsumeStreams))
			{
				consumeStreams = data.GetBooleanValue(PropertyConsumeStreams);
			}

			int exitCode = RunNativeCommand(command, parameters, wait, consumeStreams);
		    context.Result = exitCode;
		}

		private int RunNativeCommand(String command, string parameters, bool wait, bool consumeStreams)
		{
			string[] cmd = null;
			string[] args = new string[2];
			args[0] = command;
			args[1] = parameters;
		    int result = -1;

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
						temp = string.Format("{0} {1}", temp, cmd[i]);
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
                    result = proc.ExitCode;
				}
				// any error message?
			    
			}
			catch (Exception x)
			{
				throw new JobExecutionException("Error launching native command: ", x, false);
			}
            return result;
		}

		/// <summary> 
		/// Consumes data from the given input stream until EOF and prints the data to stdout
		/// </summary>
		/// <author>cooste</author>
		/// <author>James House</author>
		internal class StreamConsumer : QuartzThread
		{
			internal NativeJob outer;
			internal Stream inputStream;
			internal string type;

            /// <summary>
            /// Initializes a new instance of the <see cref="StreamConsumer"/> class.
            /// </summary>
            /// <param name="enclosingInstance">The enclosing instance.</param>
            /// <param name="inputStream">The input stream.</param>
            /// <param name="type">The type.</param>
			public StreamConsumer(NativeJob enclosingInstance, Stream inputStream, string type)
			{
				outer = enclosingInstance;
				this.inputStream = inputStream;
				this.type = type;
			}

			/// <summary> 
			/// Runs this object as a separate thread, printing the contents of the input stream
			/// supplied during instantiation, to either Console. or stderr
			/// </summary>
			public override void Run()
			{
				StreamReader br = null;
				try
				{
					br = new StreamReader(inputStream);
					string line;

					while ((line = br.ReadLine()) != null)
					{
						if (type.ToUpper().Equals("stderr".ToUpper()))
						{
							outer.Log.Warn(string.Format("{0}>{1}", type, line));
						}
						else
						{
							outer.Log.Info(string.Format("{0}>{1}", type, line));
						}
					}
				}
				catch (IOException ioe)
				{
					outer.Log.Error(string.Format("Error consuming {0} stream of spawned process.", type), ioe);
				}
				finally
				{
					if (br != null)
					{
						try
						{
							br.Close();
						}
						catch
						{
						    ;
						}
					}
				}
			}
		}
	}
}
