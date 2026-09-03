using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using Quartz.Job;

namespace Quartz.Tests.Unit.Job;

public class NativeJobTest
{
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
    /// A process that writes more than a pipe buffer holds completes with the defaults.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both streams used to be redirected whatever <c>consumeStreams</c> said, while the consumer threads
    /// were started only when it was on — so with the defaults (<c>consumeStreams</c> off,
    /// <c>waitForProcess</c> on) the child blocked writing to a pipe nobody drained, and the synchronous
    /// <c>WaitForExit</c> held a Quartz worker thread for as long as that lasted, which was for ever.
    /// Every branch below writes past any platform's buffer: Windows keeps a few kilobytes, macOS 16 KB
    /// and Linux 64 KB, and the smallest of these commands writes about 135 KB.
    /// </para>
    /// <para>
    /// The three branches are spelled differently because <c>RunNativeCommand</c> concatenates the
    /// command and the parameters into one <c>ProcessStartInfo.Arguments</c> string, which is then split
    /// into argv on whitespace outside quotes. On Windows that string is the command line
    /// <c>cmd.exe</c> parses for itself, so <c>for /L</c> arrives whole. On Linux a shell is started but
    /// it is handed an argv, so the whole command has to be a single element and therefore has to carry
    /// its own quotes: what runs is <c>/bin/sh -c "seq 1 25000"</c>, where a shell command written as
    /// several words would arrive as <c>-c</c>, its first word, and a list of positional parameters.
    /// On macOS no shell wraps anything at all — <c>RunNativeCommand</c> tests for Linux by name and
    /// executes the command directly on every other Unix — so the program and its arguments are named
    /// separately, and a <c>|</c> would be an argument of <c>seq</c> rather than a pipeline.
    /// </para>
    /// <para>
    /// The deadline is as load-bearing as the assertion: without the fix the wait never returns, so a
    /// bare <c>await</c> would hang the whole test run rather than fail it. With the deadline it fails
    /// after a minute, and passes in well under a second.
    /// </para>
    /// </remarks>
    [Test]
    public async Task AProcessThatFillsThePipeStillCompletesWithTheDefaults()
    {
        var job = new NativeJob();
        var context = TestUtil.NewJobExecutionContextFor(job);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // cmd.exe is handed the command line as written and parses it itself: 2,048 lines of 128
            // characters, and nothing reading them.
            const string Line = "01234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567";
            context.MergedJobDataMap.Put(NativeJob.PropertyCommand, "for");
            context.MergedJobDataMap.Put(NativeJob.PropertyParameters, $"/L %i in (1,1,2048) do @echo {Line}");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // The quotes are part of the value: they are what makes the command one argv element by the
            // time /bin/sh sees it, so what starts is /bin/sh -c "seq 1 25000".
            context.MergedJobDataMap.Put(NativeJob.PropertyCommand, "\"seq 1 25000\"");
        }
        else
        {
            // No shell here, so the program and its arguments are named separately: seq 1 25000.
            context.MergedJobDataMap.Put(NativeJob.PropertyCommand, "seq");
            context.MergedJobDataMap.Put(NativeJob.PropertyParameters, "1 25000");
        }

        Func<Task> act = () => job.Execute(context);

        await act.Should().CompleteWithinAsync(TimeSpan.FromSeconds(60),
            "nothing is redirected unless somebody is reading it, so the child never blocks on a full pipe");

        context.Result.Should().Be(0);
    }
}
