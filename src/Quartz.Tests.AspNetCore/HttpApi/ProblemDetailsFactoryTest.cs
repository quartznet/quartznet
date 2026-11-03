using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

using Quartz.HttpApiContract;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// The problem details the core factory writes are the bodies the HTTP API's wire snapshots pin, member
/// for member: one failure is one body whichever carrier answers it.
/// </summary>
/// <remarks>
/// The snapshots are <c>WireFormatSnapshotTest</c>'s, written by ASP.NET Core's <c>Results.Problem</c>
/// through the API; the factory is what the dashboard agent answers with, with no ASP.NET Core in its
/// process. Reading the snapshot files here is what holds the two to each other.
/// </remarks>
public sealed class ProblemDetailsFactoryTest
{
    private static IEnumerable<TestCaseData> Snapshots()
    {
        yield return new TestCaseData(new SchedulerException("The scheduler has been shut down"), "SchedulerExceptionProblemDetailsBody")
            .SetName("a scheduler's refusal");
        yield return new TestCaseData(new InvalidOperationException("Something the API never promised"), "ServerFaultProblemDetailsBody")
            .SetName("a fault the caller cannot act on");
        yield return new TestCaseData(NotFoundException.ForJob(new JobKey("no-such-job", "DummyGroup")), "UnknownJobProblemDetailsBody")
            .SetName("an unknown job");
        yield return new TestCaseData(NotFoundException.ForScheduler("no-such-scheduler"), "UnknownSchedulerProblemDetailsBody")
            .SetName("an unknown scheduler");
        yield return new TestCaseData(new InvalidRequestException("Request validation failed: Job detail is missing name"), "ValidationProblemDetailsBody")
            .SetName("a request refused as malformed");
        yield return new TestCaseData(ForbiddenException.ForReadOnlyApi(), "ReadOnlyRefusalProblemDetailsBody")
            .SetName("a read-only refusal");
        yield return new TestCaseData(new NotServedException(new NotSupportedException("The history store keeps no per-job run status.")), "NotServedProblemDetailsBody")
            .SetName("a read the store does not serve");
    }

    [TestCaseSource(nameof(Snapshots))]
    public void TheFactoryWritesTheBodyTheApiWrites(Exception exception, string snapshot)
    {
        ProblemDetailsDto problem = ProblemDetailsFactory.Create(ProblemDetailsFactory.Classify(exception, includeStackTrace: false));
        byte[] written = JsonSerializer.SerializeToUtf8Bytes(problem, HttpApiJsonContext.Default.ProblemDetailsDto);

        JsonNode expected = JsonNode.Parse(File.ReadAllText(SnapshotPath(snapshot)))!;
        JsonNode actual = JsonNode.Parse(written)!;

        JsonNode.DeepEquals(actual, expected).Should().BeTrue(
            $"the agent's {snapshot} is the API's, and the factory wrote {actual.ToJsonString()} where the snapshot holds {expected.ToJsonString()}");
    }

    /// <summary>The snapshot beside this source file, found from it rather than from the output directory.</summary>
    private static string SnapshotPath(string snapshot, [CallerFilePath] string sourceFile = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", "Verify", $"WireFormatSnapshotTest_{snapshot}.verified.json"));
    }

    /// <summary>
    /// The agent's own <c>413</c>, a page too large for its channel, is written as ASP.NET Core 10 writes a
    /// <c>413</c>.
    /// </summary>
    [Test]
    public void AStatusTheAgentAloneAnswersHasTheTablesTypeAndTitle()
    {
        ProblemDetailsDto problem = ProblemDetailsFactory.Create(HttpStatusCode.RequestEntityTooLarge, "too big");
        string written = JsonSerializer.Serialize(problem, HttpApiJsonContext.Default.ProblemDetailsDto);

        written.Should().Be("""{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.14","title":"Content Too Large","status":413,"detail":"too big"}""",
            "the table is ASP.NET Core 10's, which names a 413");
    }

    [Test]
    public void AStatusOutsideTheTableIsWrittenWithoutATypeOrTitle()
    {
        ProblemDetailsDto problem = ProblemDetailsFactory.Create(HttpStatusCode.TooManyRequests, "slow down");
        string written = JsonSerializer.Serialize(problem, HttpApiJsonContext.Default.ProblemDetailsDto);

        written.Should().Be("""{"status":429,"detail":"slow down"}""",
            "no carrier of the contract answers one, so the factory guesses nothing for it");
    }

    /// <summary>
    /// The table the HTTP API now hands <c>Results.Problem</c> as well, so its <c>500</c> keeps the title
    /// ASP.NET Core 10 wrote: 11 writes <c>Internal Server Error</c> when it is asked to choose
    /// (dotnet/aspnetcore#65590).
    /// </summary>
    [Test]
    public void TheServerFaultTitleIsTheOneAspNetCore10Wrote()
    {
        ProblemDetailsFactory.Defaults(500).Should().Be(("https://tools.ietf.org/html/rfc9110#section-15.6.1", "An error occurred while processing your request."));
    }

    [Test]
    public void AStackTraceIsCarriedAsTheApiCarriesIt()
    {
        ProblemDetailsDto problem = ProblemDetailsFactory.Create(ProblemDetailsFactory.Classify(new SchedulerException("refused"), includeStackTrace: true), "   at Somewhere()");

        problem.Extensions.Should().ContainKey(HttpApiConstants.ProblemDetailsStackTrace)
            .WhoseValue.GetString().Should().Be("   at Somewhere()");
        problem.Extensions.Should().ContainKey(HttpApiConstants.ProblemDetailsExceptionType)
            .WhoseValue.GetString().Should().Be(nameof(SchedulerException), "the trace joins the type rather than replacing it");
    }
}
