
using Quartz.Jobs;

namespace Quartz.Tests.Unit.Job;

public class NativeJobTest
{
    [Test]
    public void TestNativeJob()
    {
        var job = new NativeJob();
        var context = TestUtil.NewJobExecutionContextFor(job);
        context.MergedJobDataMap[NativeJob.PropertyCommand] = "Test";

        Action act = () => job.Execute(context);

        act.Should().NotThrow<Exception>();
    }

    [Test]
    public void ShouldRunWhatTheTypedOptionsSay()
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

        job.Execute(context);

        context.Result.Should().Be(0,
            "the options waited for the process by default, so its exit code is the job's result");
    }
}