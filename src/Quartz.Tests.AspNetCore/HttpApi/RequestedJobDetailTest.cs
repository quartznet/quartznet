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
        IJobDetail jobDetail = RequestedJobDetail.From(CreateDto("Quartz.Tests.AspNetCore.NoSuchJob, No.Such.Assembly"), isJobTypeAllowed: null);

        jobDetail.JobType.FullName.Should().Be("Quartz.Tests.AspNetCore.NoSuchJob, No.Such.Assembly",
            "the API stores the name a client sent rather than resolving it, which is what lets a job be "
            + "added from a process that does not have its assembly");
        jobDetail.Key.Should().Be(new JobKey("job-name", "job-group"));
    }

    [Test]
    public void AJobTypeThatIsNotAJobTypeNameIsRejectedWithTheReason()
    {
        Action act = () => RequestedJobDetail.From(CreateDto("   "), isJobTypeAllowed: null);

        act.Should().Throw<BadHttpRequestException>()
            .WithMessage("*Missing or malformed job type*",
                "the conversion says why it failed, and the request that caused it is the one that should "
                + "hear about it — reading the detail out of the pair and null-forgiving it turned this "
                + "into a NullReferenceException, which is a 500 rather than a 400");
    }

    /// <summary>
    /// The predicate is handed the name the request spelled, character for character. It is what an
    /// allow-list is written against, so anything that normalized it first — trimming an assembly's
    /// version, resolving the type to compare it — would break the predicate the operator wrote.
    /// </summary>
    [Test]
    public void ThePredicateIsAskedWithTheNameAsTheRequestSpeltIt()
    {
        const string Spelling = "Acme.Jobs.Nightly, Acme.Jobs, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";
        List<string> asked = [];

        RequestedJobDetail.From(CreateDto(Spelling), jobType =>
        {
            asked.Add(jobType);
            return true;
        });

        asked.Should().Equal([Spelling]);
    }

    [Test]
    public void ARefusedJobTypeIsForbiddenAndTheReasonNamesIt()
    {
        Action act = () => RequestedJobDetail.From(CreateDto("Acme.Jobs.NativeJob, Acme.Jobs"), _ => false);

        act.Should().Throw<ForbiddenException>()
            .WithMessage("*Acme.Jobs.NativeJob, Acme.Jobs*",
                "the caller sent that name, so telling them which of the jobs they sent was refused gives "
                + "nothing away and is the only thing that makes the refusal actionable");
    }

    /// <summary>
    /// A malformed name stays a <c>400</c>. The two answers say different things — "fix the request"
    /// against "you may not ask for that" — and an allow-list must not turn the first into the second.
    /// </summary>
    [Test]
    public void AMalformedJobTypeIsStillABadRequestWithAnAllowListConfigured()
    {
        Action act = () => RequestedJobDetail.From(CreateDto("   "), _ => true);

        act.Should().Throw<BadHttpRequestException>().WithMessage("*Missing or malformed job type*");
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
