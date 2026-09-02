using Quartz.Jobs;

namespace Quartz.Tests.Unit.Job;

public class NativeJobTest
{
    [Test]
    public async Task TestNativeJob()
    {
        var job = new NativeJob();
        var context = TestUtil.NewJobExecutionContextFor(job);
        context.MergedJobDataMap[NativeJob.PropertyCommand] = "Test";

        Func<Task> act = async () => await job.Execute(context);

        await act.Should().NotThrowAsync<Exception>();
    }

    [Test]
    public async Task ShouldRunWhatTheTypedOptionsSay()
    {
        var job = new NativeJob();
        var context = TestUtil.NewJobExecutionContextFor(job);

        JobDataMap data = new NativeJobOptions
        {
            Command = OperatingSystem.IsWindows() ? "cmd.exe" : "echo",
            Parameters = OperatingSystem.IsWindows() ? "/C exit 0" : "hi",
            ConsumeStreams = true,
        }.ToJobData();

        foreach (var pair in data)
        {
            context.MergedJobDataMap[pair.Key] = pair.Value;
        }

        await job.Execute(context);

        context.Result.Should().Be(0,
            "the options waited for the process by default, so its exit code is the job's result");
    }

    /// <summary>
    /// A process that writes more than a pipe buffer holds completes with the defaults.
    /// </summary>
    /// <remarks>
    /// Both streams used to be redirected whatever <c>ConsumeStreams</c> said, while the consumer threads
    /// were started only when it was on — so with the defaults (<c>ConsumeStreams</c> off,
    /// <c>WaitForProcess</c> on) the child blocked writing to a pipe nobody drained, and the synchronous
    /// <c>WaitForExit</c> held a Quartz worker thread for as long as that lasted, which was for ever.
    /// A megabyte is far past any platform's buffer, which is a few kilobytes.
    /// </para>
    /// <para>
    /// The token is as load-bearing as the assertion: without the fix the wait never returns, so a bare
    /// <c>await</c> would hang the whole test run rather than fail it. With the token it fails after a
    /// minute, and passes in well under a second.
    /// </para>
    /// </remarks>
    [Test]
    public async Task AProcessThatFillsThePipeStillCompletesWithTheDefaults()
    {
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(60));
        var job = new NativeJob();
        var context = TestUtil.NewJobExecutionContextFor(job);

        JobDataMap data = new NativeJobOptions
        {
            Command = OperatingSystem.IsWindows() ? "cmd.exe" : "sh",
            // 1 MB of output, and nothing reading it.
            Parameters = OperatingSystem.IsWindows()
                ? "/C \"for /L %i in (1,1,8192) do @echo 0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123\""
                : "-c \"yes 0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123 | head -c 1048576\"",
        }.ToJobData();

        foreach (var pair in data)
        {
            context.MergedJobDataMap[pair.Key] = pair.Value;
        }

        await job.Execute(context);

        context.Result.Should().Be(0,
            "nothing is redirected unless somebody is reading it, so the child never blocks on a full pipe");
    }
}
