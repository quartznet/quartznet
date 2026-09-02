#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
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

#endregion

using System.Diagnostics;
using System.Runtime.InteropServices;

using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;

namespace Quartz.Jobs;

/// <summary>
/// Built in job for executing native executables in a separate process.
/// </summary>
/// <remarks>
/// <para>
/// What is run is configured through the job data keys below. <see cref="NativeJobOptions" /> names
/// them all, and <see cref="JobConfiguratorExtensions.UsingNativeJobOptions{TConfigurator}" /> writes
/// them, so the settings can be given as a value rather than as string keys; the keys stay the
/// persisted form either way.
/// </para>
/// <example>
///     IJobDetail job = JobBuilder.Create&lt;NativeJob&gt;()
///         .WithIdentity("dumbJob")
///         .UsingNativeJobOptions(new NativeJobOptions { Command = "echo \"hi\" >> foobar.txt" })
///         .Build();
///
///     ITrigger trigger = TriggerBuilder.Create()
///         .WithIdentity("dumbTrigger")
///         .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(5)).RepeatForever())
///         .Build();
///
///     await scheduler.ScheduleJob(job, trigger);
/// </example>
/// If PropertyWaitForProcess is true, then the integer exit value of the process
/// will be saved as the job execution result in the JobExecutionContext.
/// </remarks>
/// <author>Matthew Payne</author>
/// <author>James House</author>
/// <author>Steinar Overbeck Cook</author>
/// <author>Marko Lahma (.NET)</author>
public class NativeJob : IJob
{
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
    /// <para>Defaults to <see langword="true" />.</para>
    /// </summary>
    public const string PropertyWaitForProcess = "waitForProcess";

    /// <summary>
    /// Optional parameter (value should be 'true' or 'false') that specifies
    /// whether the spawned process's stdout and stderr streams should be
    /// consumed.  If the process creates output, it is possible that it might
    /// 'hang' if the streams are not consumed.
    ///
    /// <para>Defaults to <see langword="false" />.</para>
    /// </summary>
    public const string PropertyConsumeStreams = "consumeStreams";

    /// <summary>
    /// Optional parameter that specifies the working directory to be used by
    /// the executed command.
    /// </summary>
    public const string PropertyWorkingDirectory = "workingDirectory";

    private const string StreamTypeStandardOutput = "stdout";
    private const string StreamTypeError = "stderr";

    /// <summary>
    /// Gets the log.
    /// </summary>
    /// <value>The log.</value>
    private ILogger<NativeJob> logger { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="NativeJob"/> class.
    /// </summary>
    public NativeJob()
    {
        logger = LogProvider.CreateLogger<NativeJob>();
    }

    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a <see cref="ITrigger" />
    /// fires that is associated with the <see cref="IJob" />.
    /// <para>
    /// The implementation may wish to set a  result object on the
    /// JobExecutionContext before this method exits.  The result itself
    /// is meaningless to Quartz, but may be informative to
    /// <see cref="IJobListener" />s or
    /// <see cref="ITriggerListener" />s that are watching the job's
    /// execution.
    /// </para>
    /// </summary>
    /// <param name="context">The firing this job is running for, whose merged job data names the command.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public virtual async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        NativeJobOptions options = NativeJobOptions.FromJobData(context.MergedJobDataMap);

        int exitCode = await RunNativeCommand(
            options.Command,
            options.Parameters ?? "",
            options.WorkingDirectory,
            options.WaitForProcess,
            options.ConsumeStreams,
            cancellationToken).ConfigureAwait(false);

        context.Result = exitCode;
    }

    /// <remarks>
    /// Both streams used to be redirected whatever <paramref name="consumeStreams" /> said, while the
    /// consumer threads were started only when it was true — so a process that wrote more than a pipe
    /// buffer holds (about 4 KB) blocked on its own write, and the synchronous <c>WaitForExit</c> below
    /// held a worker thread for as long as that lasted, which was for ever. Nothing is redirected unless
    /// somebody is reading it, and the wait is asynchronous.
    /// </remarks>
    private async ValueTask<int> RunNativeCommand(
        string command,
        string parameters,
        string? workingDirectory,
        bool wait,
        bool consumeStreams,
        CancellationToken cancellationToken)
    {
        string[] args = [command, parameters];
        int result = -1;

        try
        {
            string[] cmd;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                cmd = new string[args.Length + 2];
                cmd[0] = "cmd.exe";
                cmd[1] = "/C";
                for (int i = 0; i < args.Length; i++)
                {
                    cmd[i + 2] = args[i];
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                cmd = new string[3];
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

            logger.AboutToRun(cmd[0], temp);

            using Process proc = new Process();

            proc.StartInfo.FileName = cmd[0];
            proc.StartInfo.Arguments = temp;
            proc.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.UseShellExecute = false;

            // Redirected only when somebody reads them. A redirected pipe nobody drains fills up and the
            // process blocks writing to it.
            proc.StartInfo.RedirectStandardError = consumeStreams;
            proc.StartInfo.RedirectStandardOutput = consumeStreams;

            if (!string.IsNullOrEmpty(workingDirectory))
            {
                proc.StartInfo.WorkingDirectory = workingDirectory;
            }

            proc.Start();

            if (consumeStreams)
            {
                // Consumes the stdout and the stderr from the process
                StreamConsumer stdoutConsumer = new StreamConsumer(this, proc.StandardOutput.BaseStream, StreamTypeStandardOutput);
                StreamConsumer stderrConsumer = new StreamConsumer(this, proc.StandardError.BaseStream, StreamTypeError);
                new Thread(stdoutConsumer.Run).Start();
                new Thread(stderrConsumer.Run).Start();
            }

            if (wait)
            {
                await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                result = proc.ExitCode;
            }
            // any error message?
        }
        catch (Exception x)
        {
            throw new JobExecutionException("Error launching native command: " + x.Message, x);
        }

        return result;
    }

    /// <summary>
    /// Consumes data from the given input stream until EOF and prints the data to stdout
    /// </summary>
    /// <author>cooste</author>
    /// <author>James House</author>
    private sealed class StreamConsumer
    {
        private readonly NativeJob enclosingInstance;
        private readonly Stream inputStream;
        private readonly string type;

        /// <summary>
        /// Initializes a new instance of the <see cref="StreamConsumer"/> class.
        /// </summary>
        /// <param name="enclosingInstance">The enclosing instance.</param>
        /// <param name="inputStream">The input stream.</param>
        /// <param name="type">The type.</param>
        public StreamConsumer(NativeJob enclosingInstance, Stream inputStream, string type)
        {
            this.enclosingInstance = enclosingInstance;
            this.inputStream = inputStream;
            this.type = type;
        }

        /// <summary>
        /// Runs this object as a separate thread, printing the contents of the input stream
        /// supplied during instantiation, to either Console. or stderr
        /// </summary>
        public void Run()
        {
            try
            {
                using StreamReader br = new StreamReader(inputStream);
                string? line;
                while ((line = br.ReadLine()) is not null)
                {
                    if (type == StreamTypeError)
                    {
                        enclosingInstance.logger.StandardErrorLine(line);
                    }
                    else
                    {
                        enclosingInstance.logger.StandardOutputLine(line);
                    }
                }
            }
            catch (IOException ioe)
            {
                enclosingInstance.logger.StreamConsumptionFailed(type, ioe);
            }
        }
    }
}