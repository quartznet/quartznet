using System;

using NUnit.Framework;

namespace Quartz.Tests.Unit;

[TestFixture]
public class JobExecutionExceptionTest
{
    /// <summary>
    /// Regression test for issue #1442: JobExecutionException should have a JobDetail property
    /// that can be set and retrieved.
    /// </summary>
    [Test]
    public void JobDetail_PropertyCanBeSetAndRetrieved()
    {
        var jobDetail = JobBuilder.Create<NoOpJob>()
            .WithIdentity("testJob", "testGroup")
            .Build();

        var exception = new JobExecutionException("test error");
        Assert.IsNull(exception.JobDetail);

        exception.JobDetail = jobDetail;
        Assert.IsNotNull(exception.JobDetail);
        Assert.AreEqual("testJob", exception.JobDetail.Key.Name);
        Assert.AreEqual("testGroup", exception.JobDetail.Key.Group);
    }

    [Test]
    public void JobDetail_DefaultsToNull()
    {
        var ex1 = new JobExecutionException();
        Assert.IsNull(ex1.JobDetail);

        var ex2 = new JobExecutionException("msg");
        Assert.IsNull(ex2.JobDetail);

        var ex3 = new JobExecutionException(new Exception("inner"));
        Assert.IsNull(ex3.JobDetail);

        var ex4 = new JobExecutionException(true);
        Assert.IsNull(ex4.JobDetail);
    }

    private class NoOpJob : IJob
    {
        public System.Threading.Tasks.Task Execute(IJobExecutionContext context)
        {
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }
}
