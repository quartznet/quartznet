using Quartz.HttpApiContract;

namespace Quartz.Tests.Unit.HttpApi;

/// <summary>
/// The HTTP contract carries a job type as a name, and never resolves one that arrived over the wire.
/// </summary>
public class JobDetailDtoTest
{
    private const string UnresolvableJobTypeName = "Quartz.Tests.Unit.HttpApi.NoSuchJob, No.Such.Assembly";

    [Test]
    public void UnresolvableJobTypeNameIsGenuinelyUnresolvable()
    {
        // Everything below is vacuous if this name happens to resolve.
        Type.GetType(UnresolvableJobTypeName, throwOnError: false).Should().BeNull();
    }

    [Test]
    public void ValidateShouldAcceptJobTypeNameThatDoesNotResolve()
    {
        JobDetailDto dto = CreateDto(UnresolvableJobTypeName);

        dto.Validate().Should().BeEmpty("a job type name is validated for shape only, never by resolving it");
    }

    [Test]
    public void AsIJobDetailShouldKeepJobTypeNameThatDoesNotResolve()
    {
        JobDetailDto dto = CreateDto(UnresolvableJobTypeName);

        (IJobDetail jobDetail, string errorReason) = dto.AsIJobDetail();

        errorReason.Should().BeNull();
        jobDetail.Should().NotBeNull();
        jobDetail.JobType.FullName.Should().Be(UnresolvableJobTypeName,
            "the name has to survive for a reader that does not have the job's assembly, such as a dashboard");
        jobDetail.Key.Should().Be(new JobKey("job-name", "job-group"));
        jobDetail.Description.Should().Be("job description");
        jobDetail.Durable.Should().BeTrue();
        jobDetail.RequestsRecovery.Should().BeTrue();
        jobDetail.ConcurrentExecutionDisallowed.Should().BeTrue();
        jobDetail.PersistJobDataAfterExecution.Should().BeTrue();
        jobDetail.JobDataMap.Should().ContainKey("key").WhoseValue.Should().Be("value");
    }

    [Test]
    public void JobDetailShouldRoundTripThroughDtoWithoutResolvingJobType()
    {
        JobDetailDto dto = CreateDto(UnresolvableJobTypeName);

        (IJobDetail jobDetail, string _) = dto.AsIJobDetail();
        JobDetailDto roundTripped = JobDetailDto.Create(jobDetail);

        roundTripped.Should().Be(dto);
    }

    /// <summary>
    /// Neither direction of the conversion asks anything to resolve the name. An unresolvable name cannot
    /// prove this on its own — a resolution that was attempted and failed looks exactly like one that was
    /// never attempted — so the type carries a resolver that counts what it is asked.
    /// </summary>
    /// <remarks>
    /// This is the whole of the promise <see cref="JobDetailDto" />'s own remarks make: the name is
    /// carried as a name. On the server a resolution would walk the host's probing paths on a caller's
    /// say-so; on the client it would let a hostile server choose an assembly simple name the client's
    /// runtime goes looking for, running a module initializer in whatever matches and driving any
    /// <c>AssemblyResolve</c> handler the client registered.
    /// </remarks>
    [Test]
    public void NeitherDirectionOfTheConversionResolvesTheJobTypeName()
    {
        int resolutions = 0;
        JobType countingJobType = new(UnresolvableJobTypeName, _ =>
        {
            resolutions++;
            return null;
        });

        IJobDetail jobDetail = JobBuilder.Create()
            .OfType(countingJobType)
            .WithIdentity("job-name", "job-group")
            .Build();

        resolutions.Should().Be(0, "building a job detail out of a name must not go looking for the type");

        JobDetailDto projected = JobDetailDto.Create(jobDetail);

        resolutions.Should().Be(0, "projecting a job detail onto the wire must not go looking for the type either");
        projected.ConcurrentExecutionDisallowed.Should().BeNull(
            "nothing stated the flag and nothing knows the type, so the wire says nothing rather than 'false'");
        projected.PersistJobDataAfterExecution.Should().BeNull();

        (IJobDetail rebuilt, string _) = projected.AsIJobDetail();

        rebuilt.JobType.FullName.Should().Be(UnresolvableJobTypeName);
    }

    /// <summary>
    /// An omitted flag means "whatever the type says", not <see langword="false" />: a wire format that
    /// cannot say "not stated" silently drops <see cref="DisallowConcurrentExecutionAttribute" /> from
    /// every job scheduled by a request that did not mention it.
    /// </summary>
    [Test]
    public void AnOmittedFlagLeavesTheAttributeToSayWhatItSays()
    {
        JobDetailDto dto = CreateDto(typeof(NonConcurrentJob).AssemblyQualifiedName!) with
        {
            ConcurrentExecutionDisallowed = null,
            PersistJobDataAfterExecution = null
        };

        (IJobDetail jobDetail, string _) = dto.AsIJobDetail();

        jobDetail.ConcurrentExecutionDisallowed.Should().BeTrue(
            "the job's author declared it unsafe to run concurrently and the request said nothing to the contrary");
        jobDetail.PersistJobDataAfterExecution.Should().BeTrue();
    }

    /// <summary>
    /// A stated flag still wins, which is what lets a caller schedule a job of a type it does not have.
    /// </summary>
    [Test]
    public void AStatedFlagOverridesTheAttribute()
    {
        JobDetailDto dto = CreateDto(typeof(NonConcurrentJob).AssemblyQualifiedName!) with
        {
            ConcurrentExecutionDisallowed = false,
            PersistJobDataAfterExecution = false
        };

        (IJobDetail jobDetail, string _) = dto.AsIJobDetail();

        jobDetail.ConcurrentExecutionDisallowed.Should().BeFalse();
        jobDetail.PersistJobDataAfterExecution.Should().BeFalse();
    }

    /// <summary>
    /// Reading a job whose type resolves nowhere answers with what is known instead of throwing out of the
    /// projection — which is the ordinary heterogeneous-cluster case as much as it is a poisoned row.
    /// </summary>
    [Test]
    public void ProjectingAJobWhoseTypeResolvesNowhereDoesNotThrow()
    {
        IJobDetail jobDetail = JobBuilder.Create()
            .OfType((JobType) UnresolvableJobTypeName)
            .WithIdentity("job-name", "job-group")
            .Build();

        Action project = () => JobDetailDto.Create(jobDetail);

        project.Should().NotThrow<InvalidOperationException>(
            "a node without the job's assembly still has to be able to list the job");
        jobDetail.ConcurrentExecutionDisallowed.Should().BeFalse(
            "an effective value has to answer something, and 'not known to be disallowed' is false");
    }

    [DisallowConcurrentExecution]
    [PersistJobDataAfterExecution]
    private sealed class NonConcurrentJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

    [Test]
    public void AsIJobDetailShouldNotTouchTheJobTypeOfADetailItBuilt()
    {
        // Reading these must not force the name to resolve: they were carried explicitly for exactly that
        // reason, and deducing them from the type would defeat the point.
        JobDetailDto dto = CreateDto(UnresolvableJobTypeName) with { ConcurrentExecutionDisallowed = false, PersistJobDataAfterExecution = false };

        (IJobDetail jobDetail, string _) = dto.AsIJobDetail();

        jobDetail.ConcurrentExecutionDisallowed.Should().BeFalse();
        jobDetail.PersistJobDataAfterExecution.Should().BeFalse();
    }

    [TestCase(null, "Job detail is missing job type")]
    [TestCase("", "Job detail has malformed job type")]
    [TestCase("   ", "Job detail has malformed job type")]
    [TestCase(", Some.Assembly", "Job detail has malformed job type")]
    [TestCase("Some.Job\r\n, Some.Assembly", "Job detail has malformed job type")]
    public void ValidateShouldRejectMalformedJobTypeName(string jobTypeName, string expectedMessage)
    {
        JobDetailDto dto = CreateDto(jobTypeName);

        dto.Validate().Should().ContainSingle().Which.Should().StartWith(expectedMessage);
    }

    [Test]
    public void ValidateShouldRejectAnAbsurdlyLongJobTypeName()
    {
        JobDetailDto dto = CreateDto(new string('a', 1025));

        dto.Validate().Should().ContainSingle().Which.Should().StartWith("Job detail has malformed job type");
    }

    [Test]
    public void AsIJobDetailShouldRefuseMalformedJobTypeName()
    {
        JobDetailDto dto = CreateDto("   ");

        (IJobDetail jobDetail, string errorReason) = dto.AsIJobDetail();

        jobDetail.Should().BeNull();
        errorReason.Should().Be("Missing or malformed job type");
    }

    private static JobDetailDto CreateDto(string jobTypeName)
    {
        return new JobDetailDto(
            Name: "job-name",
            Group: "job-group",
            JobType: jobTypeName,
            Description: "job description",
            Durable: true,
            RequestsRecovery: true,
            ConcurrentExecutionDisallowed: true,
            PersistJobDataAfterExecution: true,
            JobDataMap: new JobDataMap { { "key", "value" } }
        );
    }
}
