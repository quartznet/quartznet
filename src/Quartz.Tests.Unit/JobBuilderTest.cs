using FluentAssertions;

namespace Quartz.Tests.Unit;

[TestFixture]
public class JobBuilderTest
{
    [DisallowConcurrentExecution]
    [PersistJobDataAfterExecution]
    public class TestStatefulJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context)
        {
            return default;
        }
    }

    public class TestJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context)
        {
            return default;
        }
    }

    [DisallowConcurrentExecution]
    [PersistJobDataAfterExecution]
    public class TestAnnotatedJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context)
        {
            return default;
        }
    }

    [SetUp]
    protected void SetUp()
    {
    }

    [Test]
    public void TestJobBuilder()
    {
        IJobDetail job = JobBuilder.Create()
            .OfType<TestJob>()
            .WithIdentity("j1")
            .StoreDurably()
            .Build();

        Assert.Multiple(() =>
        {
            Assert.That(job.Key.Name, Is.EqualTo("j1"), "Unexpected job name: " + job.Key.Name);
            Assert.That(job.Key.Group, Is.EqualTo(JobKey.DefaultGroup), "Unexpected job group: " + job.Key.Group);
            Assert.That(job.Key, Is.EqualTo(new JobKey("j1")), "Unexpected job key: " + job.Key);
            Assert.That(job.Description, Is.EqualTo(null), "Unexpected job description: " + job.Description);
            Assert.That(job.Durable, Is.True, "Expected isDurable == true ");
            Assert.That(job.RequestsRecovery, Is.False, "Expected requestsRecovery == false ");
            Assert.That(job.ConcurrentExecutionDisallowed, Is.False, "Expected isConcurrentExecutionDisallowed == false ");
            Assert.That(job.PersistJobDataAfterExecution, Is.False, "Expected isPersistJobDataAfterExecution == false ");
        });
        job.JobType.Type.Should().Be(typeof(TestJob));

        job = JobBuilder.Create()
            .OfType<TestAnnotatedJob>()
            .WithIdentity("j1")
            .WithDescription("my description")
            .StoreDurably()
            .RequestRecovery()
            .Build();

        Assert.Multiple(() =>
        {
            Assert.That(job.Description, Is.EqualTo("my description"), "Unexpected job description: " + job.Description);
            Assert.That(job.Durable, Is.True, "Expected isDurable == true ");
            Assert.That(job.RequestsRecovery, Is.True, "Expected requestsRecovery == true ");
            Assert.That(job.ConcurrentExecutionDisallowed, Is.True, "Expected isConcurrentExecutionDisallowed == true ");
            Assert.That(job.PersistJobDataAfterExecution, Is.True, "Expected isPersistJobDataAfterExecution == true ");
        });

        job = JobBuilder.Create()
            .OfType<TestStatefulJob>()
            .
            WithIdentity("j1", "g1")
            .RequestRecovery(false)
            .Build();

        Assert.Multiple(() =>
        {
            Assert.That(job.Key.Group, Is.EqualTo("g1"), "Unexpected job group: " + job.Key.Name);
            Assert.That(job.Durable, Is.False, "Expected isDurable == false ");
            Assert.That(job.RequestsRecovery, Is.False, "Expected requestsRecovery == false ");
            Assert.That(job.ConcurrentExecutionDisallowed, Is.True, "Expected isConcurrentExecutionDisallowed == true ");
            Assert.That(job.PersistJobDataAfterExecution, Is.True, "Expected isPersistJobDataAfterExecution == true ");
        });
    }
}