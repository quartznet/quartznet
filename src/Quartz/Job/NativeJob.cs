/* 
* Copyright 2004-2009 James House 
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
using System.Globalization;
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
        /// Optional parameter that specifies the workling directory to be used by 
        /// the executed command.
        /// </summary>
        public const string PropertyWorkingDirectory = "workingDirectory";


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

            string workingDirectory = data.GetString(PropertyWorkingDirectory);

			int exitCode = RunNativeCommand(command, parameters, workingDirectory, wait, consumeStreams);
		    context.Result = exitCode;
		}

		private int RunNativeCommand(String command, string parameters, string workingDirectory, bool wait, bool consumeStreams)
		{
			string[] cmd;
			string[] args = new string[2];
			args[0] = command;
			args[1] = parameters;
		    int result = -1;

			try
			{
				//with this variable will be done the swithcing
				string osName = Environment.GetEnvironmentVariable("OS");
                if (osName == null)
                {
                    throw new JobExecutionException("Could not read environment variable for OS");
                }

				if (osName.ToLower().IndexOf("windows") > -1)
				{
    				cmd = new string[args.Length + 2];
					cmd[0] = "cmd.exe";
					cmd[1] = "/C";
					for (int i = 0; i < args.Length; i++)
					{
						cmd[i + 2] = args[i];
					}
				}
                else if (osName.ToLower().IndexOf("linux") > -1) 
                {
                    cmd = new String[3];
                    cmd[0] = "/bin/sh";
                    cmd[1] = "-c";
                    cmd[2] = args[0] + " " + args[1];
                } 
                else 
                { 
                    // try this... 
                    cmd = args;
                }

				// Executes the command
				string temp = "";
				for (int i = 1; i < cmd.Length; i++)
				{
					temp += cmd[i] + " ";
				}

                temp = temp.Trim();

                Log.Info(string.Format(CultureInfo.InvariantCulture, "About to run {0} {1}...", cmd[0], temp));

				Process proc = new Process();
			    
                proc.StartInfo.FileName = cmd[0];
                proc.StartInfo.Arguments = temp;
                proc.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
			    proc.StartInfo.CreateNoWindow = true;
			    proc.StartInfo.UseShellExecute = false;
			    proc.StartInfo.RedirectStandardError = true;
                proc.StartInfo.RedirectStandardOutput = true;

                if (workingDirectory != null && workingDirectory.Length > 0)
                {
                    proc.StartInfo.WorkingDirectory = workingDirectory;
                }

			    proc.Start();

				// Consumes the stdout from the process
				StreamConsumer stdoutConsumer = new StreamConsumer(this, proc.StandardOutput.BaseStream, "stdout");

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
				throw new JobExecutionException("Error launching native command: " + x.Message, x, false);
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
			    try
			    {
                    using (StreamReader br = new StreamReader(inputStream))
                    {
                        string line;

                        while ((line = br.ReadLine()) != null)
                        {
                            if (type == "stderr")
                            {
                                outer.Log.Warn(string.Format(CultureInfo.InvariantCulture, "{0}>{1}", type, line));
                            }
                            else
                            {
                                outer.Log.Info(string.Format(CultureInfo.InvariantCulture, "{0}>{1}", type, line));
                            }
                        }
                    }

			    }
				catch (IOException ioe)
				{
					outer.Log.Error(string.Format(CultureInfo.InvariantCulture, "Error consuming {0} stream of spawned process.", type), ioe);
				}
			}
		}
	}
}
