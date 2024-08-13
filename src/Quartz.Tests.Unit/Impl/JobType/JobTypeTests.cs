using FluentAssertions;

namespace Quartz.Tests.Unit.Impl.JobType;

public class JobTypeTests
{
    [Test]
    public void JobTypeMustImplementIJob()
    {
        Action act = () => new Quartz.Impl.JobType(typeof(ClassDoesNotImplementIJob));

        act.Should().Throw<ArgumentException>().WithMessage("Job type must implement Quartz.IJob interface*");
    }

    [Test]
    public void ConstructUnknownJobTypeName()
    {
        const string jobTypeFullName = "Library.UnknownType";
        var jobType = new Quartz.Impl.JobType(jobTypeFullName);
        jobType.FullName.Should().Be(jobTypeFullName);
    }

    [Test]
    public void ConstructUnknownJobTypeByName_WillThrowOnTypeResolve()
    {
        const string jobTypeFullName = "Library.UnknownType";
        var jobType = new Quartz.Impl.JobType(jobTypeFullName);
        jobType.FullName.Should().Be(jobTypeFullName);

        jobType.Invoking(jt => jt.Type)
            .Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void ConstructWithNameWillReturnType()
    {
        var typeFullName = typeof(LoggerJob).AssemblyQualifiedName;
        var jobType = new Quartz.Impl.JobType(typeFullName);
        jobType.Type.FullName.Should().Be(typeof(LoggerJob).FullName);
    }

    public sealed class LoggerJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context)
        {
            Console.WriteLine("TestJobExecuted");
            return default;
        }
    }

    public sealed class ClassDoesNotImplementIJob;
}