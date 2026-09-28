#nullable enable

using System.Net;
using System.Text;
using System.Text.Json;

using Quartz.HttpApiContract;
using Quartz.Serialization.SystemTextJson;

namespace Quartz.Tests.Unit.HttpApi;

/// <summary>
/// Which failed answers <c>HttpScheduler</c> reads as a refusal of the request itself — the ones a caller
/// raises as the <see cref="ArgumentException" /> an in-process call would have — and which it leaves to
/// <c>EnsureSuccess</c>.
/// </summary>
public class HttpClientRequestRefusalTest
{
    private static readonly JsonSerializerOptions wireOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        .ConfigureWireFormat(new SystemTextJsonSerializerRegistry());

    [Test]
    public void ABadRequestNamingTheRequestRefusalIsRead()
    {
        WireResponse response = Answer(HttpStatusCode.BadRequest, HttpApiConstants.RequestRefusedExceptionType, "The range must end after it starts.");

        response.TryReadRequestRefusal(wireOptions, out string? detail).Should().BeTrue();
        detail.Should().Be("The range must end after it starts.", "the refusal is raised with the host's own words");
    }

    [TestCase(HttpStatusCode.BadRequest, nameof(ObjectDoesNotExistException))]
    [TestCase(HttpStatusCode.NotFound, HttpApiConstants.RequestRefusedExceptionType)]
    public void AnyOtherProblemIsLeftToEnsureSuccess(HttpStatusCode status, string exceptionType)
    {
        WireResponse response = Answer(status, exceptionType, "Calendar 'gone' does not exist.");

        response.TryReadRequestRefusal(wireOptions, out string? detail).Should().BeFalse(
            "a scheduler's own exception comes back as itself, and only a 400 refuses a request");
        detail.Should().BeNull();
    }

    [Test]
    public void ABadRequestThatIsNotProblemDetailsIsLeftToEnsureSuccess()
    {
        WireResponse response = new(HttpStatusCode.BadRequest, Encoding.UTF8.GetBytes("<html>proxy error</html>"));

        response.TryReadRequestRefusal(wireOptions, out _).Should().BeFalse(
            "a body this API did not write says nothing about what the host decided");
    }

    [Test]
    public void ABadRequestNamingNoExceptionTypeIsLeftToEnsureSuccess()
    {
        WireResponse response = new(HttpStatusCode.BadRequest, Encoding.UTF8.GetBytes("""{"title":"Bad Request","status":400,"detail":"something"}"""));

        response.TryReadRequestRefusal(wireOptions, out _).Should().BeFalse();
    }

    private static WireResponse Answer(HttpStatusCode status, string exceptionType, string detail)
    {
        string body = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["title"] = "Bad Request",
            ["status"] = (int) status,
            ["detail"] = detail,
            [HttpApiConstants.ProblemDetailsExceptionType] = exceptionType
        });

        return new WireResponse(status, Encoding.UTF8.GetBytes(body));
    }
}
