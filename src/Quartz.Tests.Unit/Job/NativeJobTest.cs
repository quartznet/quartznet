using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using AwesomeAssertions.Execution;

using Microsoft.Extensions.Logging;

using Quartz.Job;
using Quartz.Logging;

namespace Quartz.Tests.Unit.Job;

/// <remarks>
/// Non-parallelizable because <c>NativeJob</c> takes its logger from the ambient provider, so the tests
/// that read what it logged replace the process-wide one for the duration.
/// </remarks>
[NonParallelizable]
public class NativeJobTest
{
    /// <summary>
    /// The line the pipe-filling command repeats: 124 characters, 125 bytes with its newline.
    /// </summary>
    private const string PipeFillingLine = "0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123";

    /// <summary>
    /// What the job logs when it stops waiting for output a process it started still holds open.
    /// </summary>
    private const string OutputStillHeldOpenText = "still holds its output open";

    private static readonly TimeSpan RelayTimeout = TimeSpan.FromSeconds(30);

    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    [Test]
    public async Task TestNativeJob()
    {
        var job = new NativeJob();
        var context = TestUtil.NewJobExecutionContextFor(job);
        context.MergedJobDataMap.Put(NativeJob.PropertyCommand, "Test");

        Func<Task> act = async () => await job.Execute(context);

        await act.Should().NotThrowAsync<Exception>();
    }

    /// <summary>
    /// The parameters reach the command.
    /// </summary>
    /// <remarks>
    /// On Linux the job used to join <c>-c</c> and the script into one <c>Arguments</c> string, which .NET
    /// splits on whitespace, so <c>/bin/sh -c echo hi</c> ran <c>echo</c> alone, with <c>hi</c> as
    /// <c>$0</c>, and printed an empty line. The exit code was 0 either way, which is all the test above
    /// can see (#4212).
    /// </remarks>
    [Test]
    public async Task TheParametersReachTheCommand()
    {
        using CapturedLog log = new();

        await Run(new NativeJobOptions
        {
            Command = "echo",
            Parameters = "hi",
            ConsumeStreams = true,
        });

        log.StandardOutputLines().Should().Contain("hi",
            "the shell ran 'echo hi', not 'echo' with 'hi' as its $0");
    }

    /// <summary>
    /// A process that writes more than a pipe buffer holds completes with the defaults.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both streams used to be redirected whatever <c>consumeStreams</c> said, while the consumer threads
    /// were started only when it was on — so with the defaults (<c>consumeStreams</c> off,
    /// <c>waitForProcess</c> on) the child blocked writing to a pipe nobody drained, and the synchronous
    /// <c>WaitForExit</c> held a Quartz worker thread for as long as that lasted, which was for ever.
    /// A megabyte is far past any platform's buffer, which is a few kilobytes.
    /// </para>
    /// <para>
    /// The token is as load-bearing as the assertion: without the fix the wait never returns, so a bare
    /// <c>await</c> would hang the whole test run rather than fail it. With the token it fails after a
    /// minute, and passes in well under a second.
    /// </para>
    /// <para>
    /// Nothing is read with the defaults, so this cannot see whether the megabyte was written;
    /// <see cref="EveryLineOfAProcessThatFillsThePipeIsLoggedBeforeTheJobCompletes" /> runs the same
    /// command and counts the lines.
    /// </para>
    /// </remarks>
    [Test]
    public async Task AProcessThatFillsThePipeStillCompletesWithTheDefaults()
    {
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(60));

        IJobExecutionContext context = await Run(FillThePipe(consumeStreams: false), cancellation.Token);

        context.Result.Should().Be(0,
            "nothing is redirected unless somebody is reading it, so the child never blocks on a full pipe");
    }

    /// <summary>
    /// Every line of a process that writes a megabyte is logged, and logged before the job completes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On Linux the megabyte was never written: with the parameters dropped, the job ran
    /// <c>sh -c sh -c "yes … | head …"</c>, whose inner shell had no command and exited on stdin's end.
    /// Elsewhere it was written, but relayed by two threads the job never joined, so lines could still be
    /// on their way to the log when the job completed.
    /// </para>
    /// <para>
    /// Windows echoes the line 8192 times. Elsewhere <c>head -c 1048576</c> cuts the stream at a megabyte:
    /// 8388 lines of 125 bytes and a last, partial one of 76.
    /// </para>
    /// </remarks>
    [Test]
    public async Task EveryLineOfAProcessThatFillsThePipeIsLoggedBeforeTheJobCompletes()
    {
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(60));
        using CapturedLog log = new();

        IJobExecutionContext context = await Run(FillThePipe(consumeStreams: true), cancellation.Token);

        context.Result.Should().Be(0, "the command ran to its end");

        List<string> lines = log.StandardOutputLines();
        lines.Should().HaveCount(IsWindows ? 8192 : 8389,
            "the job reads both streams to their end before it completes, so every line the command wrote "
            + "is in the log by then");
        lines[0].Should().Be(PipeFillingLine, "the command repeats one line");
    }

    /// <summary>
    /// Cancelling a job that waits for its process kills the process, and ends the job as cancelled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The command sleeps for three seconds and then creates a file; the job is cancelled half a second
    /// in. The job used to stop waiting and leave the process running, so the file appeared, and it
    /// reported the cancellation as <c>JobExecutionException</c>: "Error launching native command".
    /// </para>
    /// <para>
    /// With <c>consumeStreams</c> on, the streams are still being read when the job is cancelled, and are
    /// left to drain in the background rather than being disposed under the loops reading them.
    /// </para>
    /// <para>
    /// .NET Framework has no <c>Process.Kill(bool)</c>, so there the job still ends as cancelled but
    /// leaves the process running, as it always did: the file appears.
    /// </para>
    /// <para>
    /// The assertions are a scope so that a failing run still waits out the command before the marker is
    /// deleted, and reports both halves.
    /// </para>
    /// </remarks>
    [TestCase(false)]
    [TestCase(true)]
    public async Task CancellingTheJobKillsTheProcessAndEndsTheJobAsCancelled(bool consumeStreams)
    {
        string marker = Path.Combine(Path.GetTempPath(), "quartz-4212-" + Guid.NewGuid().ToString("N"));
        try
        {
            using AssertionScope scope = new();
            using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(500));
            Stopwatch elapsed = Stopwatch.StartNew();

            Func<Task> act = () => Run(SleepThenCreate(marker, consumeStreams), cancellation.Token);

            await act.Should().ThrowAsync<OperationCanceledException>(
                "a cancelled job is cancelled, not a command that failed to launch");
            elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2.5),
                "the job stops waiting when it is cancelled, well before the command would have finished");

            await Task.Delay(TimeSpan.FromSeconds(5));

#if NETFRAMEWORK
            File.Exists(marker).Should().BeTrue(
                "without Process.Kill(bool) the process is left running, so the command got past the sleep");
#else
            File.Exists(marker).Should().BeFalse(
                "the process was killed with its descendants, so the command never got past the sleep");
#endif
        }
        finally
        {
            File.Delete(marker);
        }
    }

    /// <summary>
    /// A process the command leaves running does not hold the job past the command's own exit, even
    /// though it holds the output open.
    /// </summary>
    /// <remarks>
    /// The backgrounded subshell inherits the shell's stdout and keeps it open for ten seconds. Waiting for
    /// the end of the output without a bound held the job until the subshell exited; the job used to end
    /// with the shell, and still does, five seconds of draining later.
    /// </remarks>
    [Test]
    public async Task AProcessLeftRunningDoesNotHoldTheJobOpen()
    {
        if (IsWindows)
        {
            NUnit.Framework.Assert.Ignore("Backgrounding a process with '&' is a POSIX shell's; cmd.exe has no equivalent");
        }

        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(60));
        using CapturedLog log = new();
        Stopwatch elapsed = Stopwatch.StartNew();

        IJobExecutionContext context = await Run(InShell("(sleep 10; echo late) & echo early"), cancellation.Token);

        elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(8),
            "the job waits for the output only a bounded time after the shell exits, not for the subshell");
        context.Result.Should().Be(0, "the shell itself exited at once, successfully");
        log.StandardOutputLines().Should().Contain("early", "what the shell wrote before it exited is logged");
        log.Messages().Should().Contain(x => x.Contains(OutputStillHeldOpenText),
            "the job says why it completed with the output still open");
    }

    /// <summary>
    /// A job that does not wait for its process returns at once, and what the process writes is still
    /// logged afterwards.
    /// </summary>
    /// <remarks>
    /// The streams are read in the background, and the process is disposed only once they have been read
    /// to the end; it used to be disposed as the job returned, under the threads reading them.
    /// </remarks>
    [Test]
    public async Task AJobThatDoesNotWaitStillLogsWhatTheProcessWrites()
    {
        using CapturedLog log = new();

        IJobExecutionContext context = await Run(new NativeJobOptions
        {
            Command = "echo",
            Parameters = "quartz-4212",
            WaitForProcess = false,
            ConsumeStreams = true,
        });

        context.Result.Should().Be(-1, "a job that does not wait has no exit code to report");

        Stopwatch waited = Stopwatch.StartNew();
        while (waited.Elapsed < RelayTimeout && !log.StandardOutputLines().Contains("quartz-4212"))
        {
            await Task.Delay(20);
        }

        log.StandardOutputLines().Should().Contain("quartz-4212",
            "the streams are drained after the job returns, by a loop that outlives it");
    }

    /// <summary>
    /// A megabyte of output: <see cref="PipeFillingLine" /> repeated.
    /// </summary>
    private static NativeJobOptions FillThePipe(bool consumeStreams) => new()
    {
        Command = IsWindows ? "cmd.exe" : "sh",
        Parameters = IsWindows
            ? $"/C \"for /L %i in (1,1,8192) do @echo {PipeFillingLine}\""
            : $"-c \"yes {PipeFillingLine} | head -c 1048576\"",
        ConsumeStreams = consumeStreams,
    };

    /// <summary>
    /// A command that sleeps for about three seconds and then creates <paramref name="marker" />.
    /// </summary>
    /// <remarks>
    /// Windows pings itself four times, a second apart, since <c>timeout /t</c> refuses to run with its
    /// input redirected. Outside Windows and Linux the job starts the command itself rather than a shell,
    /// so the command is the shell.
    /// </remarks>
    private static NativeJobOptions SleepThenCreate(string marker, bool consumeStreams)
    {
        if (IsWindows)
        {
            return new NativeJobOptions
            {
                Command = "ping",
                Parameters = $"-n 4 127.0.0.1 >NUL && echo x > \"{marker}\"",
                ConsumeStreams = consumeStreams,
            };
        }

        if (IsLinux)
        {
            return new NativeJobOptions
            {
                Command = "sleep",
                Parameters = $"3 && touch '{marker}'",
                ConsumeStreams = consumeStreams,
            };
        }

        return new NativeJobOptions
        {
            Command = "/bin/sh",
            Parameters = $"-c \"sleep 3 && touch '{marker}'\"",
            ConsumeStreams = consumeStreams,
        };
    }

    /// <summary>
    /// A shell script, with its streams consumed. On Linux the job hands the command to
    /// <c>/bin/sh -c</c> itself; elsewhere it starts the command directly, so the command is the shell.
    /// </summary>
    private static NativeJobOptions InShell(string script)
    {
        return IsLinux
            ? new NativeJobOptions { Command = script, ConsumeStreams = true }
            : new NativeJobOptions { Command = "/bin/sh", Parameters = $"-c \"{script}\"", ConsumeStreams = true };
    }

    /// <summary>
    /// Runs a <see cref="NativeJob" /> with what <paramref name="options" /> says, cancelling it when
    /// <paramref name="cancellationToken" /> is cancelled.
    /// </summary>
    private static async Task<IJobExecutionContext> Run(NativeJobOptions options, CancellationToken cancellationToken = default)
    {
        NativeJob job = new NativeJob();
        ICancellableJobExecutionContext context = (ICancellableJobExecutionContext) TestUtil.NewJobExecutionContextFor(job);

        context.MergedJobDataMap.Put(NativeJob.PropertyCommand, options.Command);
        if (options.Parameters is not null)
        {
            context.MergedJobDataMap.Put(NativeJob.PropertyParameters, options.Parameters);
        }

        context.MergedJobDataMap.Put(NativeJob.PropertyWaitForProcess, options.WaitForProcess);
        context.MergedJobDataMap.Put(NativeJob.PropertyConsumeStreams, options.ConsumeStreams);

        using (cancellationToken.Register(context.Cancel))
        {
            await job.Execute(context);
        }

        return context;
    }

    /// <summary>
    /// What a test asks <see cref="NativeJob" /> to run: its job data, by name.
    /// </summary>
    private sealed class NativeJobOptions
    {
        public string Command { get; init; }

        public string Parameters { get; init; }

        public bool WaitForProcess { get; init; } = true;

        public bool ConsumeStreams { get; init; }
    }

    /// <summary>
    /// Records what a <c>NativeJob</c> constructed while this is alive logs.
    /// </summary>
    private sealed class CapturedLog : IDisposable
    {
        private const string StandardOutputPrefix = "stdout>";

        private readonly ConcurrentQueue<string> messages = new();
        private readonly ILoggerFactory factory;

        public CapturedLog()
        {
            factory = LoggerFactory.Create(builder => builder.AddProvider(new RecordingLoggerProvider(messages)));
            LogContext.SetCurrentLogProvider(factory);
        }

        /// <summary>
        /// The lines logged from the process's stdout so far, in order, without the stream's prefix.
        /// </summary>
        public List<string> StandardOutputLines() => messages
            .Where(x => x.StartsWith(StandardOutputPrefix, StringComparison.Ordinal))
            .Select(x => x.Substring(StandardOutputPrefix.Length))
            .ToList();

        /// <summary>
        /// Every message logged so far, in order.
        /// </summary>
        public List<string> Messages() => messages.ToList();

        public void Dispose()
        {
            LogProvider.SetCurrentLogProvider(null);
            factory.Dispose();
        }

        /// <summary>
        /// Records every message, from whichever thread logs it.
        /// </summary>
        private sealed class RecordingLoggerProvider : ILoggerProvider
        {
            private readonly ConcurrentQueue<string> messages;

            public RecordingLoggerProvider(ConcurrentQueue<string> messages)
            {
                this.messages = messages;
            }

            public ILogger CreateLogger(string categoryName) => new RecordingLogger(messages);

            public void Dispose()
            {
            }

            private sealed class RecordingLogger : ILogger
            {
                private readonly ConcurrentQueue<string> messages;

                public RecordingLogger(ConcurrentQueue<string> messages)
                {
                    this.messages = messages;
                }

                public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;

                public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

                public void Log<TState>(
                    Microsoft.Extensions.Logging.LogLevel logLevel,
                    EventId eventId,
                    TState state,
                    Exception exception,
                    Func<TState, Exception, string> formatter)
                {
                    messages.Enqueue(formatter(state, exception));
                }
            }
        }
    }
}
