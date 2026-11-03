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
/// <para>
/// On Windows the command runs as <c>cmd.exe /C command parameters</c>. On Linux the command and its
/// parameters are joined into the one script <c>/bin/sh -c</c> runs. Elsewhere the command is started
/// itself, with the parameters as its arguments.
/// </para>
/// <para>
/// If <see cref="PropertyWaitForProcess" /> is true, the integer exit value of the process is the job
/// execution result in the JobExecutionContext, and cancelling the job (an interrupt, or a shutdown that
/// interrupts running jobs) kills the process and its descendants and ends the job with
/// <see cref="OperationCanceledException" />. If it is false, the job returns as soon as the process has
/// started, and the process is deliberately left running whatever happens to the job.
/// </para>
/// <para>
/// With <see cref="PropertyConsumeStreams" /> on as well, every line of output is logged before the job
/// completes, unless a process it started and left running keeps the streams open more than five seconds
/// after it exits. The job then completes, and that process's lines go on being logged.
/// </para>
/// <para>
/// Never retried unless its trigger names a retry policy of its own: it carries
/// <c>[RetryPolicy(0)]</c>, so the scheduler's default does not apply. A retry would run the command again.
/// </para>
/// </remarks>
/// <author>Matthew Payne</author>
/// <author>James House</author>
/// <author>Steinar Overbeck Cook</author>
/// <author>Marko Lahma (.NET)</author>
[RetryPolicy(0)]
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
    /// How long a job that waits for its process goes on waiting, once the process has exited, for the
    /// end of its output. A process it started and left running, such as <c>myserver &amp;</c>, holds the
    /// streams open for as long as it lives.
    /// </summary>
    private static readonly TimeSpan OutputDrainTimeout = TimeSpan.FromSeconds(5);

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
    /// <para>
    /// Both streams used to be redirected whatever <paramref name="consumeStreams" /> said, while the
    /// consumer threads were started only when it was true — so a process that wrote more than a pipe
    /// buffer holds (about 4 KB) blocked on its own write, and the synchronous <c>WaitForExit</c> below
    /// held a worker thread for as long as that lasted, which was for ever. Nothing is redirected unless
    /// somebody is reading it, and the wait is asynchronous.
    /// </para>
    /// <para>
    /// On Linux the shell's script is one entry of <see cref="ProcessStartInfo.ArgumentList" />. It used to
    /// be joined into <see cref="ProcessStartInfo.Arguments" />, which .NET splits on whitespace, so
    /// <c>/bin/sh -c echo hi</c> ran <c>echo</c> alone with <c>hi</c> as <c>$0</c>, and no parameter ever
    /// reached the command (#4212).
    /// </para>
    /// <para>
    /// Cancelling the wait kills the process tree before the cancellation propagates; it used to leave
    /// the process running and report the cancellation as a failure to launch. The streams are read by
    /// asynchronous loops rather than two foreground threads, which kept the host alive for as long as
    /// anything held the pipes open. The <see cref="Process" /> is disposed only once both loops have
    /// finished, by whoever is left waiting for them.
    /// </para>
    /// <para>
    /// The wait for the loops after the process exits is bounded by <see cref="OutputDrainTimeout" />.
    /// A descendant the process left running, such as <c>myserver &amp;</c>, inherits the pipes; an
    /// unbounded wait would hold the job for as long as it lived, where the job used to end with the
    /// process.
    /// </para>
    /// </remarks>
    private async ValueTask<int> RunNativeCommand(
        string command,
        string parameters,
        string? workingDirectory,
        bool wait,
        bool consumeStreams,
        CancellationToken cancellationToken)
    {
        Process proc = new Process();
        Task streamsConsumed = Task.CompletedTask;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // cmd.exe parses its own command line, so it is handed one string. It is the system's
                // own, never one that PATH or the working directory would find first.
                proc.StartInfo.FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe");
                proc.StartInfo.Arguments = ("/C " + command + " " + parameters).Trim();
                logger.AboutToRun(proc.StartInfo.FileName, proc.StartInfo.Arguments);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // The script is the one argument after -c, so the shell sees the parameters.
                string script = parameters.Length == 0 ? command : command + " " + parameters;
                proc.StartInfo.FileName = "/bin/sh";
                proc.StartInfo.ArgumentList.Add("-c");
                proc.StartInfo.ArgumentList.Add(script);
                logger.AboutToRun(proc.StartInfo.FileName, "-c " + script);
            }
            else
            {
                proc.StartInfo.FileName = command;
                proc.StartInfo.Arguments = parameters.Trim();
                logger.AboutToRun(proc.StartInfo.FileName, proc.StartInfo.Arguments);
            }

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
                streamsConsumed = Task.WhenAll(
                    ConsumeStream(proc.StandardOutput.BaseStream, StreamTypeStandardOutput),
                    ConsumeStream(proc.StandardError.BaseStream, StreamTypeError));
            }

            if (!wait)
            {
                // The process is left running, whatever happens to the job.
                return -1;
            }

            try
            {
                await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

                // All of the output is logged before the job completes, unless something the process
                // left running still holds the streams open.
                await streamsConsumed.WaitAsync(OutputDrainTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                KillProcessTree(proc);
                throw;
            }
            catch (TimeoutException)
            {
                // Not a failure: the process has exited. Its streams go on draining in the background.
                logger.OutputStillHeldOpen(proc.ExitCode, OutputDrainTimeout);
            }

            return proc.ExitCode;
        }
        catch (Exception x) when (x is not OperationCanceledException)
        {
            throw new JobExecutionException("Error launching native command: " + x.Message, x);
        }
        finally
        {
            // Never disposed under the loops still reading its streams: when the job did not wait for
            // them, was cancelled, or stopped waiting at OutputDrainTimeout, they drain in the background
            // and dispose the process when done.
            if (streamsConsumed.IsCompleted)
            {
                proc.Dispose();
            }
            else
            {
                _ = DisposeWhenConsumed(proc, streamsConsumed);
            }
        }
    }

    /// <summary>
    /// Kills the process and its descendants, tolerating one that has already exited.
    /// </summary>
    private static void KillProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // It exited on its own between the cancellation and the kill.
        }
    }

    /// <summary>
    /// Disposes the process once both of its streams have been read to the end.
    /// </summary>
    /// <remarks>
    /// Nothing awaits this. It cannot fault on account of a stream, since
    /// <see cref="ConsumeStream" /> logs whatever it fails with rather than throwing it.
    /// </remarks>
    private static async Task DisposeWhenConsumed(Process process, Task streamsConsumed)
    {
        try
        {
            await streamsConsumed.ConfigureAwait(false);
        }
        finally
        {
            process.Dispose();
        }
    }

    /// <summary>
    /// Reads the given stream of the process to its end, logging each line as the event for that stream.
    /// </summary>
    /// <remarks>
    /// An asynchronous loop rather than a thread of its own, so a child that holds its pipes open cannot
    /// keep the host alive. A failure is logged here rather than thrown, because when the job does not
    /// wait for the process, nothing else is left to observe it.
    /// </remarks>
    private async Task ConsumeStream(Stream stream, string type)
    {
        try
        {
            using StreamReader reader = new StreamReader(stream);
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (type == StreamTypeError)
                {
                    logger.StandardErrorLine(line);
                }
                else
                {
                    logger.StandardOutputLine(line);
                }
            }
        }
        catch (Exception e)
        {
            logger.StreamConsumptionFailed(type, e);
        }
    }
}