namespace Quartz.Tests.Unit;

public class JobExecutionExceptionTest
{
    [Test]
    public void JobDetail_PropertyCanBeSetAndRetrieved()
    {
        var jobDetail = JobBuilder.Create<NoOpJob>()
            .WithIdentity("testJob", "testGroup")
            .Build();

        var exception = new JobExecutionException("test error");
        Assert.That(exception.JobDetail, Is.Null);

        exception.JobDetail = jobDetail;
        Assert.That(exception.JobDetail, Is.Not.Null);
        Assert.That(exception.JobDetail!.Key.Name, Is.EqualTo("testJob"));
        Assert.That(exception.JobDetail.Key.Group, Is.EqualTo("testGroup"));
    }

    [Test]
    public void JobDetail_DefaultsToNull()
    {
        new JobExecutionException().JobDetail.Should().BeNull();
        new JobExecutionException("msg").JobDetail.Should().BeNull();
        new JobExecutionException(new Exception("inner")).JobDetail.Should().BeNull();
        new JobExecutionException("msg", new Exception("inner")).JobDetail.Should().BeNull();
    }

    [Test]
    public void InstructionFlagsAreInitOnly()
    {
        JobExecutionException exception = new(new Exception("boom"))
        {
            RefireImmediately = true,
            UnscheduleFiringTrigger = true,
            UnscheduleAllTriggers = true
        };

        exception.RefireImmediately.Should().BeTrue();
        exception.UnscheduleFiringTrigger.Should().BeTrue();
        exception.UnscheduleAllTriggers.Should().BeTrue();
    }

    private sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
