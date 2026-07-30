
namespace Quartz.Tests.Unit;

public class JobBuilderTest
{
    [DisallowConcurrentExecution]
    [PersistJobDataAfterExecution]
    public class TestStatefulJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

    public class TestJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

    [DisallowConcurrentExecution]
    [PersistJobDataAfterExecution]
    public class TestAnnotatedJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
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

    [Test]
    public void UsingJobData_StoresTheValueWithItsOwnType()
    {
        Guid guid = Guid.NewGuid();

        IJobDetail job = JobBuilder.Create<TestJob>()
            .UsingJobData("string", "text")
            .UsingJobData("int", 1)
            .UsingJobData("long", 2L)
            .UsingJobData("float", 3.5f)
            .UsingJobData("double", 4.5d)
            .UsingJobData("decimal", 5.5m)
            .UsingJobData("bool", true)
            .UsingJobData("guid", guid)
            .UsingJobData("char", 'c')
            .UsingJobData("null", null)
            .Build();

        // The one object?-typed overload replaced nine primitive ones; what lands in the map has
        // to be exactly what the primitive overloads stored, or every persisted map changes shape.
        job.JobDataMap["string"].Should().Be("text");
        job.JobDataMap["int"].Should().Be(1).And.BeOfType<int>();
        job.JobDataMap["long"].Should().Be(2L).And.BeOfType<long>();
        job.JobDataMap["float"].Should().Be(3.5f).And.BeOfType<float>();
        job.JobDataMap["double"].Should().Be(4.5d).And.BeOfType<double>();
        job.JobDataMap["decimal"].Should().Be(5.5m).And.BeOfType<decimal>();
        job.JobDataMap["bool"].Should().Be(true).And.BeOfType<bool>();
        job.JobDataMap["guid"].Should().Be(guid).And.BeOfType<Guid>();
        job.JobDataMap["char"].Should().Be('c').And.BeOfType<char>();
        job.JobDataMap["null"].Should().BeNull();
    }

    [Test]
    public void UsingJobData_Map_MergesIntoWhatTheBuilderAlreadyHolds()
    {
        JobDataMap map = new JobDataMap { { "added", "from map" }, { "kept", "overwritten" } };

        IJobDetail job = JobBuilder.Create<TestJob>()
            .UsingJobData("existing", "kept")
            .UsingJobData("kept", "original")
            .UsingJobData(map)
            .Build();

        job.JobDataMap.Should().ContainKey("existing").WhoseValue.Should().Be("kept",
            "merging must not discard what the builder already carried - that was SetJobData's job");
        job.JobDataMap["added"].Should().Be("from map");
        job.JobDataMap["kept"].Should().Be("overwritten");
    }
}