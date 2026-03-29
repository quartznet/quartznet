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
        Assert.Multiple(() =>
        {
            Assert.That(new JobExecutionException().JobDetail, Is.Null);
            Assert.That(new JobExecutionException("msg").JobDetail, Is.Null);
            Assert.That(new JobExecutionException(new Exception("inner")).JobDetail, Is.Null);
            Assert.That(new JobExecutionException(true).JobDetail, Is.Null);
        });
    }

    private class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context) => default;
    }
}
