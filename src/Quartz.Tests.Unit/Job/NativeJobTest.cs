using FluentAssertions;

using Quartz.Job;

namespace Quartz.Tests.Unit.Job;

public class NativeJobTest
{
    [Test]
    public void TestNativeJob()
    {
        var job = new NativeJob();
        var context = TestUtil.NewJobExecutionContextFor(job);
        context.MergedJobDataMap.Put(NativeJob.PropertyCommand, "Test");

        Action act = () => job.Execute(context);

        act.Should().NotThrow<Exception>();
    }
}