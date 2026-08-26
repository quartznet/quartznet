using Microsoft.AspNetCore.Http;

using Quartz.AspNetCore.HttpApi.Util;
using Quartz.HttpApiContract;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// The one place a job carried by a request becomes an <see cref="IJobDetail" />, which the three
/// endpoints that take a job in their body go through.
/// </summary>
public class RequestedJobDetailTest
{
    [Test]
    public void AJobTypeNameThatDoesNotResolveIsCarriedThrough()
    {
        IJobDetail jobDetail = RequestedJobDetail.From(CreateDto("Quartz.Tests.AspNetCore.NoSuchJob, No.Such.Assembly"));

        jobDetail.JobType.FullName.Should().Be("Quartz.Tests.AspNetCore.NoSuchJob, No.Such.Assembly",
            "the API stores the name a client sent rather than resolving it, which is what lets a job be "
            + "added from a process that does not have its assembly");
        jobDetail.Key.Should().Be(new JobKey("job-name", "job-group"));
    }

    [Test]
    public void AJobTypeThatIsNotAJobTypeNameIsRejectedWithTheReason()
    {
        Action act = () => RequestedJobDetail.From(CreateDto("   "));

        act.Should().Throw<BadHttpRequestException>()
            .WithMessage("*Missing or malformed job type*",
                "the conversion says why it failed, and the request that caused it is the one that should "
                + "hear about it — reading the detail out of the pair and null-forgiving it turned this "
                + "into a NullReferenceException, which is a 500 rather than a 400");
    }

    private static JobDetailDto CreateDto(string jobTypeName)
    {
        return new JobDetailDto(
            Name: "job-name",
            Group: "job-group",
            JobType: jobTypeName,
            Description: null,
            Durable: true,
            RequestsRecovery: false,
            ConcurrentExecutionDisallowed: false,
            PersistJobDataAfterExecution: false,
            JobDataMap: new JobDataMap()
        );
    }
}
