using FakeItEasy;

using Quartz.Impl;

namespace Quartz.Tests.Unit;

/// <summary>
/// Job data bound by naming the job property rather than spelling its key.
/// </summary>
public class TypedJobDataTest
{
    public enum RunMode
    {
        Slow,
        Fast
    }

    public class SampleJob : IJob
    {
        public string Name { get; set; } = "";

        public int RetryCount { get; set; }

        public RunMode Mode { get; set; }

        public TimeSpan Timeout { get; set; }

        public byte Percentage { get; set; }

        public float Ratio { get; set; }

        public object Level { get; set; } = "";

        public RunMode? OptionalMode { get; set; }

        // Legal C#, but the job factory only ever looks a key up upper-cased, so nothing can reach it.
        public string url { get; set; } = "";

        public string Computed => Name + RetryCount;

        public string PrivateSetter { get; private set; } = "";

        public string Field = "";

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

    public interface IParameterizedJob : IJob
    {
        string Name { get; set; }
    }

    public class ParameterizedJob : IParameterizedJob
    {
        public string Name { get; set; } = "";

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

    public class OtherJob : IJob
    {
        public string Name { get; set; } = "";

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

    public class DerivedSampleJob : SampleJob
    {
        public string DerivedOnly { get; set; } = "";
    }

    public class HidingJob : SampleJob
    {
        public new int Name { get; set; }
    }

    public class BaseJob : IJob
    {
        public virtual string Overridden { get; set; } = "";

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

    public class OverridingJob : BaseJob
    {
        public override string Overridden { get; set; } = "";
    }

    public class TimeoutJob : IJob
    {
        [TimeSpanParseRule(TimeSpanParseRule.Seconds)]
        public TimeSpan Timeout { get; set; }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

    public interface IExplicitJob : IJob
    {
        string Name { get; set; }
    }

    public class ExplicitJob : IExplicitJob
    {
        string IExplicitJob.Name { get; set; } = "";

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

    [Test]
    public void JobDataIsStoredUnderThePropertyName()
    {
        var job = JobBuilder.Create<SampleJob>()
            .UsingJobData(j => j.Name, "hello")
            .UsingJobData(j => j.RetryCount, 3)
            .UsingJobData(j => j.Timeout, TimeSpan.FromSeconds(5))
            .Build();

        job.JobDataMap["Name"].Should().Be("hello");
        job.JobDataMap["RetryCount"].Should().Be(3);
        job.JobDataMap["Timeout"].Should().Be(TimeSpan.FromSeconds(5));
    }

    [Test]
    public void EnumsAreStoredByName()
    {
        // A number would bind onto the property just as well, but it leaves anything reading the map - the
        // dashboard, a listener - looking at an integer, and it is what the JSON serializer would write.
        var job = JobBuilder.Create<SampleJob>()
            .UsingJobData(j => j.Mode, RunMode.Fast)
            .Build();

        job.JobDataMap["Mode"].Should().Be("Fast");
    }

    [Test]
    public void NullIsStoredAsGivenRatherThanSkipped()
    {
        // The point of naming the property is being able to say what the value is, including that it is
        // nothing - a template instance diffed against its defaults could not express this.
        var job = JobBuilder.Create<SampleJob>()
            .UsingJobData(j => j.Name, null!)
            .Build();

        job.JobDataMap.ContainsKey("Name").Should().BeTrue();
        job.JobDataMap["Name"].Should().BeNull();
    }

    [Test]
    public void ADefaultValueIsStoredJustLikeAnyOther()
    {
        var job = JobBuilder.Create<SampleJob>()
            .UsingJobData(j => j.RetryCount, 0)
            .Build();

        job.JobDataMap["RetryCount"].Should().Be(0);
    }

    [Test]
    public void APropertyWithoutAPublicSetterIsRejected()
    {
        var computed = () => JobBuilder.Create<SampleJob>().UsingJobData(j => j.Computed, "x");
        computed.Should().Throw<ArgumentException>().WithMessage("*Computed*no public setter*");

        var privateSetter = () => JobBuilder.Create<SampleJob>().UsingJobData(j => j.PrivateSetter, "x");
        privateSetter.Should().Throw<ArgumentException>().WithMessage("*PrivateSetter*no public setter*");
    }

    [Test]
    public void AnExpressionThatDoesNotNameAPropertyIsRejected()
    {
        var methodCall = () => JobBuilder.Create<SampleJob>().UsingJobData(j => j.ToString(), "x");
        methodCall.Should().Throw<ArgumentException>().WithMessage("*does not name one*");

        var field = () => JobBuilder.Create<SampleJob>().UsingJobData(j => j.Field, "x");
        field.Should().Throw<ArgumentException>().WithMessage("*does not name one*");
    }

    [Test]
    public void APropertyReachedThroughAnotherPropertyIsRejected()
    {
        // The job factory sets properties on the job instance itself, so a nested path has nowhere to land.
        var nested = () => JobBuilder.Create<SampleJob>().UsingJobData(j => j.Name.Length, 3);
        nested.Should().Throw<ArgumentException>().WithMessage("*read directly off the job*");
    }

    [Test]
    public void APropertyOfABaseJobIsAccepted()
    {
        var job = JobBuilder.Create<DerivedSampleJob>()
            .UsingJobData(j => j.Name, "hello")
            .Build();

        job.JobDataMap["Name"].Should().Be("hello");
    }

    [Test]
    public void ABuilderPointedAtAnotherJobTypeIsRejectedAtTheCall()
    {
        // OfType<T>() constrains T to the builder's own job type, so the generic overload cannot express
        // this at all - only the one that takes a Type, which says so where the mistake was made.
        var act = () => JobBuilder.Create<SampleJob>()
            .UsingJobData(j => j.Name, "hello")
            .OfType(typeof(OtherJob));

        act.Should().Throw<ArgumentException>().WithMessage("*SampleJob*OtherJob*");
    }

    [Test]
    public void ABuilderPointedAtAnotherJobTypeByNameIsRejectedWhenBuilt()
    {
        // A type named as a string is only known once it resolves, so this one waits for the build.
        var act = () => JobBuilder.Create<SampleJob>()
            .UsingJobData(j => j.Name, "hello")
            .OfType((JobType) typeof(OtherJob).AssemblyQualifiedName!)
            .Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*SampleJob*OtherJob*");
    }

    [Test]
    public void AJobTypeThatCannotBeResolvedIsLeftAlone()
    {
        // Named types are checked by the scheduler when it loads them; there is nothing to compare against
        // here, so the data is taken as given rather than rejected.
        var job = JobBuilder.Create<SampleJob>()
            .OfType((JobType) "Some.Assembly.That.Is.Not.Loaded.Job, Nowhere")
            .UsingJobData(j => j.Name, "hello")
            .Build();

        job.JobDataMap["Name"].Should().Be("hello");
    }

    [Test]
    public void AnUntypedBuilderStillTakesStringKeys()
    {
        // JobBuilder.Create() is a builder for IJob, which has no properties to name.
        var job = JobBuilder.Create()
            .OfType<SampleJob>()
            .UsingJobData("Name", "hello")
            .Build();

        job.JobDataMap["Name"].Should().Be("hello");
    }

    [Test]
    public void AValueIsStoredInThePropertysOwnType()
    {
        // C# widens the literal to int / double to satisfy both inference bounds, so without narrowing it
        // back the map would hold a type the property does not have.
        var job = JobBuilder.Create<SampleJob>()
            .UsingJobData(j => j.Percentage, 42)
            .UsingJobData(j => j.Ratio, 3.5)
            .Build();

        job.JobDataMap["Percentage"].Should().BeOfType<byte>().And.Be((byte) 42);
        job.JobDataMap["Ratio"].Should().BeOfType<float>().And.Be(3.5f);
    }

    [Test]
    public void AValueThatDoesNotFitThePropertyIsRejectedAtTheCall()
    {
        // Left alone this reaches the job factory, which swallows the overflow and leaves the property at
        // its default.
        var act = () => JobBuilder.Create<SampleJob>().UsingJobData(j => j.Percentage, 300);

        act.Should().Throw<ArgumentException>().WithMessage("*Percentage*");
    }

    [Test]
    public void EnumNormalizationFollowsThePropertyTypeNotTheValue()
    {
        // A loosely typed property keeps the enum: turned into a string it would come back as a string and
        // throw when the job casts it.
        var job = JobBuilder.Create<SampleJob>()
            .UsingJobData(j => j.Level, RunMode.Fast)
            .UsingJobData(j => j.OptionalMode, RunMode.Fast)
            .Build();

        job.JobDataMap["Level"].Should().Be(RunMode.Fast);
        job.JobDataMap["OptionalMode"].Should().Be("Fast", "a nullable enum property still binds from its name");
    }

    [Test]
    public void APropertyReachedThroughAConstrainedTypeParameterIsAccepted()
    {
        // Roslyn converts the lambda parameter to the constraint before reading the property, which a plain
        // reference comparison against the parameter would reject.
        static IJobDetail Build<TJob>() where TJob : IParameterizedJob =>
            JobBuilder.Create<TJob>().UsingJobData(j => j.Name, "hello").Build();

        Build<ParameterizedJob>().JobDataMap["Name"].Should().Be("hello");
    }

    [Test]
    public void ALowercaseFirstPropertyIsRejected()
    {
        // The job factory only ever looks a key up with its first character upper-cased, so a key named
        // after this property could never find it. Saying so beats storing a key that binds to nothing.
        var act = () => JobBuilder.Create<SampleJob>().UsingJobData(j => j.url, "https://example.org");

        act.Should().Throw<ArgumentException>().WithMessage("*looks a key up as 'Url'*");
    }

    [Test]
    public void AnUntypedTriggerBuilderDoesNotLookAtTheJobsType()
    {
        // TriggerBuilder<IJob> names no properties, so there is nothing to check - and a job detail that
        // carries no type at all still has to be accepted, the way it always was.
        var jobDetail = A.Fake<IJobDetail>();
        A.CallTo(() => jobDetail.Key).Returns(new JobKey("job"));

        var act = () => TriggerBuilder.Create().ForJob(jobDetail).Build();

        act.Should().NotThrow();
    }

    [Test]
    public void APropertyOfADerivedJobReachedByCastingIsRejected()
    {
        // The cast is stripped when the receiver is unwrapped, so without checking the declaring type this
        // would bind a property the job being built does not have.
        var act = () => JobBuilder.Create<SampleJob>().UsingJobData(j => ((DerivedSampleJob) j).DerivedOnly, "x");

        act.Should().Throw<ArgumentException>().WithMessage("*DerivedSampleJob*");
    }

    [Test]
    public void APropertyWhoseNameIsDeclaredTwiceIsRejectedFromEitherSide()
    {
        // Only the name reaches the map, so the job factory cannot tell the two apart - and it resolves
        // against the job's own type, which need not be the one the expression named.
        var hiding = () => JobBuilder.Create<HidingJob>().UsingJobData(j => j.Name, 5);
        hiding.Should().Throw<ArgumentException>().WithMessage("*more than one property*");

        // The base side is the one that actually loses the value: the map would carry a string and the
        // factory would bind it to the derived int.
        var hidden = () => JobBuilder.Create<HidingJob>().UsingJobData(j => ((SampleJob) j).Name, "x");
        hidden.Should().Throw<ArgumentException>().WithMessage("*more than one property*");
    }

    [Test]
    public void AnOverriddenPropertyIsNotMistakenForAHiddenOne()
    {
        var job = JobBuilder.Create<OverridingJob>()
            .UsingJobData(j => j.Overridden, "hello")
            .Build();

        job.JobDataMap["Overridden"].Should().Be("hello");
    }

    [Test]
    public void AnEnumPropertyStillChecksTheValue()
    {
        // The enum branch used to take anything and ToString it.
        var act = () => JobBuilder.Create<SampleJob>().UsingJobData<object>(j => j.Mode, new object());

        act.Should().Throw<ArgumentException>().WithMessage("*Mode*");
    }

    [Test]
    public void ATimeSpanWithAParseRuleBindsToTheJob()
    {
        var job = JobBuilder.Create<TimeoutJob>()
            .UsingJobData(j => j.Timeout, TimeSpan.FromSeconds(30))
            .Build();

        // The parse rule says how to read a bare number; an actual TimeSpan has nothing to parse, and
        // running it through the rule threw where the error handler swallowed it.
        var instance = new TimeoutJob();
        new PropertySettingJobFactory().SetObjectProperties(instance, job.JobDataMap);
        instance.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Test]
    public void ANullIsRejectedForAPropertyThatCannotHoldOne()
    {
        // Type inference widens TValue to the nullable form, so this compiles and would otherwise store a
        // null that the job factory quietly turns into the type's default.
        int? missing = null;
        var act = () => JobBuilder.Create<SampleJob>().UsingJobData(j => j.RetryCount, missing);

        act.Should().Throw<ArgumentException>().WithMessage("*RetryCount*");
    }

    [Test]
    public void ANullIsAcceptedForAPropertyThatCanHoldOne()
    {
        var job = JobBuilder.Create<SampleJob>()
            .UsingJobData(j => j.OptionalMode, null)
            .Build();

        job.JobDataMap.ContainsKey("OptionalMode").Should().BeTrue();
        job.JobDataMap["OptionalMode"].Should().BeNull();
    }

    [Test]
    public void AnExplicitlyImplementedPropertyIsRejected()
    {
        // The job factory looks the key up on the job class, where an explicit implementation is private -
        // so it would store a key that binds to nothing.
        static IJobDetail Build<TJob>() where TJob : IExplicitJob =>
            JobBuilder.Create<TJob>().UsingJobData(j => j.Name, "hello").Build();

        var act = () => Build<ExplicitJob>();

        act.Should().Throw<ArgumentException>().WithMessage("*no such public property*");
    }

    [Test]
    public void ANarrowingConversionThatLosesTheValueIsRejected()
    {
        // Type inference fixes TValue to the widest bound, so these compile - and the conversion would
        // round, saturate or truncate without saying so.
        var rounded = () => JobBuilder.Create<SampleJob>().UsingJobData(j => j.RetryCount, 2.5);
        rounded.Should().Throw<ArgumentException>().WithMessage("*without losing information*");

        var saturated = () => JobBuilder.Create<SampleJob>().UsingJobData(j => j.Ratio, 1e300);
        saturated.Should().Throw<ArgumentException>().WithMessage("*without losing information*");

        // The empty string converts to NUL and back to the empty string, so it looks lossless - the job
        // factory's own one-character rule is what catches it.
        var emptyToChar = () => JobBuilder.Create<CharJob>().UsingJobData<object>(j => j.Delimiter, "");
        emptyToChar.Should().Throw<ArgumentException>().WithMessage("*exactly one character*");
    }

    [Test]
    public void AWideningConversionThatKeepsTheValueIsAccepted()
    {
        var job = JobBuilder.Create<SampleJob>()
            .UsingJobData(j => j.Percentage, 42)
            .UsingJobData(j => j.Ratio, 3.5)
            .Build();

        job.JobDataMap["Percentage"].Should().BeOfType<byte>().And.Be((byte) 42);
        job.JobDataMap["Ratio"].Should().BeOfType<float>().And.Be(3.5f);
    }

    public class CharJob : IJob
    {
        public char Delimiter { get; set; }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

    [Test]
    public void ATypedTriggerBuilderAcceptsAJobDetailWithNoResolvableType()
    {
        // The guard has to survive a job detail that carries no type - the interface says it is non-null,
        // but a fake or a third-party implementation need not honour that.
        var jobDetail = A.Fake<IJobDetail>();
        A.CallTo(() => jobDetail.Key).Returns(new JobKey("job"));

        var act = () => TriggerBuilder.Create<SampleJob>().ForJob(jobDetail).Build();

        act.Should().NotThrow();
    }

    [Test]
    public void AFailedConversionKeepsTheUnderlyingException()
    {
        var act = () => JobBuilder.Create<SampleJob>().UsingJobData<object>(j => j.Timeout, "not a timespan");

        // The converter's own exception says why; relabelling it as a bad argument would lose that.
        act.Should().Throw<ArgumentException>().WithInnerException<Exception>();
    }

    [Test]
    public void TriggerJobDataIsStoredUnderThePropertyName()
    {
        var trigger = TriggerBuilder.Create<SampleJob>()
            .ForJob("job")
            .UsingJobData(j => j.Name, "per-trigger")
            .Build();

        trigger.JobDataMap["Name"].Should().Be("per-trigger");
    }

    [Test]
    public void ATriggerCannotBePointedAtAJobOfAnotherType()
    {
        var job = JobBuilder.Create<OtherJob>().WithIdentity("job").Build();

        var act = () => TriggerBuilder.Create<SampleJob>().ForJob(job);

        act.Should().Throw<ArgumentException>().WithMessage("*SampleJob*OtherJob*");
    }

    [Test]
    public void ATriggerPointedAtAJobKeyIsTakenAsGiven()
    {
        // Only ForJob(IJobDetail) knows what the job is; a key carries no type to check against.
        var trigger = TriggerBuilder.Create<SampleJob>()
            .ForJob(new JobKey("job"))
            .UsingJobData(j => j.Name, "per-trigger")
            .Build();

        trigger.JobDataMap["Name"].Should().Be("per-trigger");
    }
}
