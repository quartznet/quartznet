using System.Diagnostics;
using System.Runtime.InteropServices;

using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;
using Quartz.Jobs;
using Quartz.Tests.Unit.Plugin.History;

namespace Quartz.Tests.Unit.Job;

/// <summary>
/// What a spawned process writes reaches the log as the event for the stream it came out of.
/// </summary>
/// <remarks>
/// <para>
/// The two streams used to share one message, <c>{Type}&gt;{Line}</c>, with the stream's name as a
/// placeholder. They are now an event each — <c>stdout&gt;{Line}</c> at Information and
/// <c>stderr&gt;{Line}</c> at Warning — so which stream a line came from is an id an operator filters
/// rather than a property value they match. The catalogue snapshot pins the two templates; only a test
/// that runs a process can say the right one is called for the right stream, which is the way this
/// could have gone wrong without anything else noticing.
/// </para>
/// <para>
/// Non-parallelizable because <c>NativeJob</c> takes its logger from the ambient factory, so reading
/// what it logged means replacing the process-wide one for the duration.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class NativeJobStreamLoggingTest
{
    private static readonly TimeSpan RelayTimeout = TimeSpan.FromSeconds(30);

    [Test]
    public void AStandardOutputLineIsTheStandardOutputEvent()
    {
        List<LogEntry> entries = RunAndCapture(new NativeJobOptions
        {
            Command = "echo",
            Parameters = "quartz-3414",
            ConsumeStreams = true,
        }, eventId: 7201);

        entries.Should().Contain(
            x => x.EventId.Id == 7201 && x.Level == LogLevel.Information && x.Message.StartsWith("stdout>"),
            "a line the process wrote to stdout is event 7201 at Information, and the text a sink renders "
            + "still begins with the stream name the single shared template used to interpolate");
    }

    [Test]
    public void AStandardErrorLineIsTheStandardErrorEvent()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Assert.Ignore(
                "NativeJob only wraps the command in a shell on Windows and Linux; elsewhere it starts the "
                + "command itself, so an unknown command fails to start rather than making a shell complain "
                + "on stderr");
        }

        List<LogEntry> entries = RunAndCapture(new NativeJobOptions
        {
            Command = "quartz-no-such-command-3414",
            ConsumeStreams = true,
        }, eventId: 7202);

        entries.Should().Contain(
            x => x.EventId.Id == 7202 && x.Level == LogLevel.Warning && x.Message.StartsWith("stderr>"),
            "the shell's complaint about an unknown command comes out of stderr, which is event 7202 at "
            + "Warning - the level the shared template's stderr branch already logged at");
    }

    /// <summary>
    /// Runs the job and waits for the event the caller is after, because the stream consumers relay from
    /// a thread each and the job does not join them before it returns.
    /// </summary>
    private static List<LogEntry> RunAndCapture(NativeJobOptions options, int eventId)
    {
        RecordingLoggerProvider recorder = new();
        using LoggerFactory factory = new();
        factory.AddProvider(recorder);

        LogProvider.SetLogProvider(factory);
        try
        {
            NativeJob job = new NativeJob();
            IJobExecutionContext context = TestUtil.NewJobExecutionContextFor(job);

            foreach (KeyValuePair<string, object> pair in options.ToJobData())
            {
                context.MergedJobDataMap[pair.Key] = pair.Value;
            }

            job.Execute(context);

            Stopwatch waited = Stopwatch.StartNew();
            while (waited.Elapsed < RelayTimeout && !recorder.Entries.Exists(x => x.EventId.Id == eventId))
            {
                Thread.Sleep(20);
            }

            return recorder.Entries;
        }
        finally
        {
            LogProvider.SetLogProvider(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        }
    }
}
