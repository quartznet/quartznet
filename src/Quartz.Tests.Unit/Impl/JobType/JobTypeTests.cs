using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Impl.JobType;

public class JobTypeTests
{
    [Test]
    public void JobTypeMustImplementIJob()
    {
        Action act = () => new global::Quartz.JobType(typeof(ClassDoesNotImplementIJob));

        act.Should().Throw<ArgumentException>().WithMessage("Job type must implement Quartz.IJob interface*");
    }

    [Test]
    public void ConstructUnknownJobTypeName()
    {
        const string jobTypeFullName = "Library.UnknownType";
        var jobType = new global::Quartz.JobType(jobTypeFullName);
        jobType.FullName.Should().Be(jobTypeFullName);
    }

    [Test]
    public void ConstructUnknownJobTypeByName_WillThrowOnTypeResolve()
    {
        const string jobTypeFullName = "Library.UnknownType";
        var jobType = new global::Quartz.JobType(jobTypeFullName);
        jobType.FullName.Should().Be(jobTypeFullName);

        jobType.Invoking(jt => jt.Type)
            .Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void ConstructWithNameWillReturnType()
    {
        var typeFullName = typeof(LoggerJob).AssemblyQualifiedName;
        var jobType = new global::Quartz.JobType(typeFullName);
        jobType.Type.FullName.Should().Be(typeof(LoggerJob).FullName);
    }

    [Test]
    public void ConstructWithResolverWillResolveThroughIt()
    {
        const string StoredName = "Some.Name.The.Runtime.Cannot.Resolve";
        string asked = null;

        global::Quartz.JobType jobType = new global::Quartz.JobType(StoredName, name =>
        {
            asked = name;
            return typeof(LoggerJob);
        });

        jobType.Type.Should().Be<LoggerJob>("the resolver, not Type.GetType, decides what the name means");
        asked.Should().Be(StoredName, "the resolver is asked about the name exactly as it was given");
        jobType.FullName.Should().Be(StoredName, "resolving a name must never rewrite it");
    }

    [Test]
    public void ConstructWithResolverThatFindsNothing_WillThrowOnTypeResolve()
    {
        global::Quartz.JobType jobType = new global::Quartz.JobType("Library.UnknownType", static _ => null);

        jobType.Invoking(jt => jt.Type)
            .Should().Throw<InvalidOperationException>().WithMessage("*Library.UnknownType*",
                "a resolver that finds nothing has to fail the same way the runtime lookup does");
    }

    [Test]
    public void ConstructWithTypeLoaderResolverWillResolveAPre40Name()
    {
        // What a 2.x or 3.x scheduler wrote into JOB_CLASS_NAME; the type lives in Quartz.Jobs today.
        const string StoredName = "Quartz.Jobs.NoOpJob, Quartz";
        ITypeLoader typeLoader = new SimpleTypeLoader();

        global::Quartz.JobType jobType = new global::Quartz.JobType(StoredName, typeLoader.LoadType);

        jobType.Type.Should().Be<global::Quartz.Jobs.NoOpJob>(
            "a job type name stored before the assembly moved has to keep resolving through the type loader's fallback");
        jobType.FullName.Should().Be(StoredName,
            "the stored name is reported back unchanged, so nothing rewrites the persisted spelling");
    }

    [Test]
    public void ImplicitConversionFromTypeValidates()
    {
        global::Quartz.JobType fromType = typeof(LoggerJob);

        fromType.Type.Should().Be<LoggerJob>();

        Action act = () =>
        {
            global::Quartz.JobType bad = typeof(ClassDoesNotImplementIJob);
            GC.KeepAlive(bad);
        };
        act.Should().Throw<ArgumentException>("the implicit direction is the validated one");
    }

    [Test]
    public void ExplicitConversionFromStringIsUnvalidated()
    {
        var fromString = (global::Quartz.JobType) "Library.UnknownType";

        fromString.FullName.Should().Be("Library.UnknownType");
        fromString.TryResolve(out _).Should().BeFalse();
    }

    [Test]
    public void EqualityIsByFullName()
    {
        var first = new global::Quartz.JobType(typeof(LoggerJob));
        var byName = new global::Quartz.JobType(first.FullName);
        var other = (global::Quartz.JobType) "Library.UnknownType";

        first.Equals(byName).Should().BeTrue("FullName is the identity, however the instance was constructed");
        (first == byName).Should().BeTrue();
        (first != other).Should().BeTrue();
        ((global::Quartz.JobType) null == null).Should().BeTrue();
        (first == null).Should().BeFalse();
        first.GetHashCode().Should().Be(byName.GetHashCode());
    }

    [Test]
    public void TryResolveIsPublicAndDoesNotSettleType()
    {
        var jobType = new global::Quartz.JobType("Library.UnknownType");

        jobType.TryResolve(out var resolved).Should().BeFalse();
        resolved.Should().BeNull();

        var resolvable = new global::Quartz.JobType(typeof(LoggerJob).AssemblyQualifiedName);
        resolvable.TryResolve(out resolved).Should().BeTrue();
        resolved.Should().Be<LoggerJob>();
    }

    [Test]
    public void GetJobBuilderWorksForADetailWhoseTypeNameDoesNotResolve()
    {
        // The job store loads details with loadJobType: false; rebuilding one through its builder
        // must not force the stored name to resolve.
        IJobDetail detail = JobBuilder.Create()
            .WithIdentity("job", "group")
            .OfType((Quartz.JobType) "Library.UnknownType")
            .StoreDurably()
            .UsingJobData("key", "value")
            .DisallowConcurrentExecution()
            .PersistJobDataAfterExecution()
            .Build();

        IJobDetail rebuilt = detail.GetJobBuilder().Build();

        rebuilt.JobType.FullName.Should().Be("Library.UnknownType");
        rebuilt.Key.Should().Be(new JobKey("job", "group"));
        rebuilt.JobDataMap.GetString("key").Should().Be("value");
    }

    public sealed class LoggerJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Console.WriteLine("TestJobExecuted");
            return default;
        }
    }

    public sealed class ClassDoesNotImplementIJob;
}