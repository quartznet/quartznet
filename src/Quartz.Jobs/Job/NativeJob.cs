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

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Logging;

namespace Quartz.Job;

/// <summary>
/// Built in job for executing native executables in a separate process.
/// </summary>
/// <remarks>
/// <example>
///     JobDetail job = new JobDetail("dumbJob", null, typeof(Quartz.Jobs.NativeJob));
///     job.JobDataMap.Put(Quartz.Jobs.NativeJob.PropertyCommand, "echo \"hi\" >> foobar.txt");
///     Trigger trigger = TriggerUtils.MakeSecondlyTrigger(5);
///     trigger.Name = "dumbTrigger";
///     sched.ScheduleJob(job, trigger);
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
/// The .NET Framework and netstandard2.0 builds of this package have neither
/// <c>ProcessStartInfo.ArgumentList</c> nor <c>Process.Kill(bool)</c>. On those, the Linux script is
/// still passed to the shell as the one <c>Arguments</c> string, which splits on whitespace, and a cancelled
/// job leaves its process running. Every .NET 8 or later application gets the net8.0 build.
/// </para>
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
    /// How long a job that waits for its process goes on waiting, once the process has exited, for the
    /// end of its output. A process it started and left running, such as <c>myserver &amp;</c>, holds the
    /// streams open for as long as it lives.
    /// </summary>
    private static readonly TimeSpan OutputDrainTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets the log.
    /// </summary>
    /// <value>The log.</value>
    private ILog Log { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="NativeJob"/> class.
    /// </summary>
    public NativeJob()
    {
        Log = LogProvider.GetLogger(typeof(NativeJob));
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
    /// <param name="context"></param>
    public virtual async Task Execute(IJobExecutionContext context)
    {
        JobDataMap data = context.MergedJobDataMap;

        string command = data.GetString(PropertyCommand) ?? throw new JobExecutionException("command missing");
        string parameters = data.TryGetString(PropertyParameters, out string? paramValue) ? paramValue! : string.Empty;

        bool wait = true;
        if (data.ContainsKey(PropertyWaitForProcess))
        {
            wait = data.GetBooleanValue(PropertyWaitForProcess);
        }

        bool consumeStreams = false;
        if (data.ContainsKey(PropertyConsumeStreams))
        {
            consumeStreams = data.GetBooleanValue(PropertyConsumeStreams);
        }

        data.TryGetString(PropertyWorkingDirectory, out var workingDirectory);
        int exitCode = await RunNativeCommand(command, parameters, workingDirectory, wait, consumeStreams, context.CancellationToken).ConfigureAwait(false);
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
    /// On Linux the shell's script is one entry of <c>ProcessStartInfo.ArgumentList</c>. It used to
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
    private async Task<int> RunNativeCommand(
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
                Log.Info($"About to run {proc.StartInfo.FileName} {proc.StartInfo.Arguments}...");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
#if NETFRAMEWORK || NETSTANDARD2_0
                // ProcessStartInfo.ArgumentList arrived in .NET Core 2.1. Without it the script stays joined
                // into Arguments, as it always was here; .NET 8 and later get the net8.0 build.
                proc.StartInfo.FileName = "/bin/sh";
                proc.StartInfo.Arguments = ("-c " + command + " " + parameters).Trim();
                Log.Info($"About to run {proc.StartInfo.FileName} {proc.StartInfo.Arguments}...");
#else
                // The script is the one argument after -c, so the shell sees the parameters.
                string script = parameters.Length == 0 ? command : command + " " + parameters;
                proc.StartInfo.FileName = "/bin/sh";
                proc.StartInfo.ArgumentList.Add("-c");
                proc.StartInfo.ArgumentList.Add(script);
                Log.Info($"About to run {proc.StartInfo.FileName} -c {script}...");
#endif
            }
            else
            {
                proc.StartInfo.FileName = command;
                proc.StartInfo.Arguments = parameters.Trim();
                Log.Info($"About to run {proc.StartInfo.FileName} {proc.StartInfo.Arguments}...");
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

            // Subscribed before the start, so that a process that is gone before the wait begins
            // still completes it.
            TaskCompletionSource<bool> exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (wait)
            {
                proc.EnableRaisingEvents = true;
                proc.Exited += (_, _) => exited.TrySetResult(true);
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
                using (cancellationToken.Register(() => exited.TrySetCanceled(cancellationToken)))
                {
                    await exited.Task.ConfigureAwait(false);
                }

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
                Log.Info($"The process exited with code {proc.ExitCode}, but {OutputDrainTimeout} later a process it started still holds its output open. The job completes, and that process's lines keep being logged.");
            }

            return proc.ExitCode;
        }
        catch (Exception x) when (x is not OperationCanceledException)
        {
            throw new JobExecutionException("Error launching native command: " + x.Message, x, false);
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
#if NETFRAMEWORK || NETSTANDARD2_0
        // Process.Kill(bool) arrived in .NET Core 3.0. Killing cmd.exe or /bin/sh alone would orphan the
        // command it runs, so the process is left running, as it always was here; .NET 8 and later get
        // the net8.0 build.
        _ = process;
#else
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // It exited on its own between the cancellation and the kill.
        }
#endif
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
                    Log.Warn($"{type}>{line}");
                }
                else
                {
                    Log.Info($"{type}>{line}");
                }
            }
        }
        catch (Exception e)
        {
            Log.ErrorException($"Error consuming {type} stream of spawned process.", e);
        }
    }
}
