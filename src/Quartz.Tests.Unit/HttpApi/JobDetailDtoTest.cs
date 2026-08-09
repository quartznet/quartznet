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
