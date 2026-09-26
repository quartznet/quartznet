using Quartz.HttpApiContract;

namespace Quartz.Tests.Unit.HttpApi;

/// <summary>
/// What the execution-limits endpoint accepts. The body is the one place a limit's scope crosses a
/// process boundary, so both halves of an entry — the count and the scope — are validated rather than
/// coerced.
/// </summary>
public class SetExecutionLimitsRequestTest
{
    [Test]
    public void ValidateShouldAcceptAnEmptyBody()
    {
        new SetExecutionLimitsRequest(null).Validate().Should().BeEmpty(
            "a body with no limits clears them, which is not a validation failure");
    }

    [Test]
    public void ValidateShouldAcceptEveryScopeAndGroupSpelling()
    {
        SetExecutionLimitsRequest request = new(new Dictionary<string, ExecutionLimitDto>
        {
            ["batch"] = new(2),
            ["tenant"] = new(8, ExecutionLimitScope.Cluster),
            ["*"] = new(1, ExecutionLimitScope.Cluster),
            ["_"] = new(3),
            ["free"] = new(null),
        });

        request.Validate().Should().BeEmpty();
    }

    [Test]
    public void ValidateShouldRejectABlankGroupKey()
    {
        SetExecutionLimitsRequest request = new(new Dictionary<string, ExecutionLimitDto>
        {
            ["   "] = new(1),
        });

        request.Validate().Should().ContainSingle().Which.Should().Contain("is invalid");
    }

    [Test]
    public void ValidateShouldRejectAMissingLimit()
    {
        SetExecutionLimitsRequest request = new(new Dictionary<string, ExecutionLimitDto>
        {
            ["batch"] = null,
        });

        request.Validate().Should().ContainSingle().Which.Should().Contain("is missing",
            "a key with no limit object says nothing about the group, and guessing what it meant would be worse than refusing it");
    }

    [Test]
    public void ValidateShouldRejectANegativeLimit()
    {
        SetExecutionLimitsRequest request = new(new Dictionary<string, ExecutionLimitDto>
        {
            ["batch"] = new(-1),
        });

        request.Validate().Should().ContainSingle().Which.Should().Contain("must be non-negative");
    }

    [Test]
    public void ValidateShouldRejectAScopeThatIsNeitherNodeNorCluster()
    {
        SetExecutionLimitsRequest request = new(new Dictionary<string, ExecutionLimitDto>
        {
            ["batch"] = new(2, (ExecutionLimitScope) 42),
        });

        request.Validate().Should().ContainSingle().Which.Should().Contain("must be Node or Cluster",
            "an unknown scope would otherwise be enforced as Node, silently turning a quota into a per-node limit");
    }

    [Test]
    public void ValidateShouldReportEveryProblemInOneBody()
    {
        SetExecutionLimitsRequest request = new(new Dictionary<string, ExecutionLimitDto>
        {
            ["batch"] = new(-1, (ExecutionLimitScope) 42),
        });

        request.Validate().Should().HaveCount(2, "the count and the scope are separate mistakes and a caller should hear about both at once");
    }

    [Test]
    public void ValidateShouldAcceptAPrefixKeyWithACount()
    {
        SetExecutionLimitsRequest request = new(new Dictionary<string, ExecutionLimitDto>
        {
            ["tenant:*"] = new(2, ExecutionLimitScope.Cluster),
        });

        request.Validate().Should().BeEmpty();
    }

    [Test]
    public void ValidateShouldRejectAnUnlimitedPrefix()
    {
        SetExecutionLimitsRequest request = new(new Dictionary<string, ExecutionLimitDto>
        {
            ["tenant:*"] = new(null),
        });

        request.Validate().Should().ContainSingle().Which.Should().Contain("cannot be unlimited",
            "the endpoint would otherwise build the limits and fail halfway, with a 500 rather than a 400");
    }

    [Test]
    public void ValidateShouldRejectAPrefixKeyThatNamesNoPrefix()
    {
        SetExecutionLimitsRequest request = new(new Dictionary<string, ExecutionLimitDto>
        {
            ["tenant:**"] = new(1),
        });

        request.Validate().Should().ContainSingle().Which.Should().Contain("names no prefix");
    }
}
