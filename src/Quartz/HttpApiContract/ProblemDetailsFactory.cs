#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

using System.Net;
using System.Text.Json;

namespace Quartz.HttpApiContract;

/// <summary>
/// What a failure is to the caller: a request they got wrong, a thing that is not there, a rule that said
/// no, or a fault they cannot act on.
/// </summary>
internal enum ProblemKind
{
    /// <summary>The request was malformed: <c>400</c>, named as the request refusal.</summary>
    RequestRefused,

    /// <summary>The body could not be read: <c>400</c>.</summary>
    Deserialization,

    /// <summary>The subject of a read is absent: <c>404</c>.</summary>
    NotFound,

    /// <summary>What serves the read keeps no such thing: <c>501</c>.</summary>
    NotServed,

    /// <summary>A configured rule refused: <c>403</c>.</summary>
    Forbidden,

    /// <summary>The scheduler refused: <c>400</c>, naming its exception.</summary>
    SchedulerFault,

    /// <summary>A fault the caller cannot act on: <c>500</c>.</summary>
    Fault,
}

/// <summary>
/// One failure as a carrier answers it: the status, the detail, the exception type to name, and what kind
/// of failure it was for the carrier's log.
/// </summary>
internal sealed record ProblemClassification(HttpStatusCode Status, string Detail, string? ExceptionType, ProblemKind Kind);

/// <summary>
/// Turns an exception into the problem details every carrier of the contract answers errors with.
/// </summary>
/// <remarks>
/// <para>
/// The pure half of the HTTP API's exception handling, in core so that a carrier that is not ASP.NET
/// Core — the dashboard agent — answers a failure with the same status, the same detail and the same
/// <c>Quartz-ExceptionType</c> the API would have. The API's handler asks this and then writes an
/// <c>IResult</c> and a log line; the agent asks this and writes the bytes.
/// </para>
/// <para>
/// A client-actionable error names the exception type it came from; a server fault does not. A
/// <c>400</c> and a <c>404</c> are things the caller can reconstruct and handle — that is what
/// <see cref="HttpApiConstants.ProblemDetailsExceptionType" /> is read for — so every one of them carries
/// it whichever layer raised it. A <c>501</c> names <see cref="NotSupportedException" />: the history
/// store behind the route keeps no such thing, which the client raises as the same exception an in-process
/// read would have. A <c>403</c> is neither: it is a decision, not a failure, so it names no exception
/// type. A <c>500</c> is a fault the caller cannot act on, and naming the type that produced it buys
/// nothing it does not also leak; nor does its message, which routinely names the server, the database,
/// the login or the constraint. So the detail on that path is <see cref="ServerFaultDetail" />, one fixed
/// sentence, and the real message goes to the carrier's log. The switch that says "I am debugging this"
/// puts it back.
/// </para>
/// </remarks>
internal static class ProblemDetailsFactory
{
    /// <summary>
    /// What a <c>500</c> says instead of the exception's message.
    /// </summary>
    /// <remarks>
    /// Fixed text rather than an empty detail, so a client that renders the detail has something to
    /// render and one that matches on it matches on a constant rather than on whichever driver failed.
    /// </remarks>
    public const string ServerFaultDetail = "The scheduler failed to handle the request. The failure is recorded in the server's log.";

    /// <summary>
    /// What <paramref name="exception" /> is to the caller.
    /// </summary>
    /// <param name="exception">What was thrown.</param>
    /// <param name="includeStackTrace">
    /// Whether the carrier is in its debugging mode, which puts a fault's real message in the detail.
    /// </param>
    public static ProblemClassification Classify(Exception exception, bool includeStackTrace)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            // The operation catalogue's refusal of a malformed request, answered as the API's own is and
            // under the same name, so a client cannot tell which layer refused it.
            InvalidRequestException => new ProblemClassification(
                HttpStatusCode.BadRequest, WithInnerMessage(exception), HttpApiConstants.RequestRefusedExceptionType, ProblemKind.RequestRefused),

            JsonSerializationException => new ProblemClassification(
                HttpStatusCode.BadRequest, WithInnerMessage(exception), exception.GetType().Name, ProblemKind.Deserialization),

            NotFoundException => new ProblemClassification(
                HttpStatusCode.NotFound, WithInnerMessage(exception), exception.GetType().Name, ProblemKind.NotFound),

            NotServedException => new ProblemClassification(
                HttpStatusCode.NotImplemented, exception.Message, nameof(NotSupportedException), ProblemKind.NotServed),

            // A decision rather than a failure: no exception type, so a client is not invited to catch a
            // refusal as though it were a scheduler fault.
            ForbiddenException => new ProblemClassification(
                HttpStatusCode.Forbidden, exception.Message, ExceptionType: null, ProblemKind.Forbidden),

            SchedulerException => new ProblemClassification(
                HttpStatusCode.BadRequest, exception.Message, exception.GetType().Name, ProblemKind.SchedulerFault),

            _ => new ProblemClassification(
                HttpStatusCode.InternalServerError, includeStackTrace ? exception.Message : ServerFaultDetail, ExceptionType: null, ProblemKind.Fault),
        };
    }

    /// <summary>
    /// The body for <paramref name="classification" />, shaped as ASP.NET Core's <c>Results.Problem</c>
    /// shapes it: the RFC 9110 type and title for the status where there is one, and the Quartz members
    /// as extensions.
    /// </summary>
    public static ProblemDetailsDto Create(ProblemClassification classification, string? stackTrace = null)
    {
        ArgumentNullException.ThrowIfNull(classification);

        return Create(classification.Status, classification.Detail, classification.ExceptionType, stackTrace);
    }

    /// <summary>
    /// A body for a status and a detail, naming an exception type when one is given.
    /// </summary>
    public static ProblemDetailsDto Create(HttpStatusCode status, string detail, string? exceptionType = null, string? stackTrace = null)
    {
        (string? type, string? title) = Defaults((int) status);

        ProblemDetailsDto problem = new()
        {
            Type = type,
            Title = title,
            Status = (int) status,
            Detail = detail,
        };

        if (exceptionType is not null)
        {
            Extend(problem, HttpApiConstants.ProblemDetailsExceptionType, exceptionType);
        }

        if (stackTrace is not null)
        {
            Extend(problem, HttpApiConstants.ProblemDetailsStackTrace, stackTrace);
        }

        return problem;
    }

    /// <summary>
    /// The <c>type</c> and <c>title</c> ASP.NET Core writes for a status, which is what keeps the two
    /// carriers' bodies one body: its <c>ProblemDetailsDefaults</c> table, for the statuses this
    /// contract answers.
    /// </summary>
    internal static (string? Type, string? Title) Defaults(int status)
    {
        return status switch
        {
            400 => ("https://tools.ietf.org/html/rfc9110#section-15.5.1", "Bad Request"),
            401 => ("https://tools.ietf.org/html/rfc9110#section-15.5.2", "Unauthorized"),
            403 => ("https://tools.ietf.org/html/rfc9110#section-15.5.4", "Forbidden"),
            404 => ("https://tools.ietf.org/html/rfc9110#section-15.5.5", "Not Found"),
            405 => ("https://tools.ietf.org/html/rfc9110#section-15.5.6", "Method Not Allowed"),
            406 => ("https://tools.ietf.org/html/rfc9110#section-15.5.7", "Not Acceptable"),
            408 => ("https://tools.ietf.org/html/rfc9110#section-15.5.9", "Request Timeout"),
            409 => ("https://tools.ietf.org/html/rfc9110#section-15.5.10", "Conflict"),
            412 => ("https://tools.ietf.org/html/rfc9110#section-15.5.13", "Precondition Failed"),
            415 => ("https://tools.ietf.org/html/rfc9110#section-15.5.16", "Unsupported Media Type"),
            422 => ("https://tools.ietf.org/html/rfc4918#section-11.2", "Unprocessable Entity"),
            426 => ("https://tools.ietf.org/html/rfc9110#section-15.5.22", "Upgrade Required"),
            500 => ("https://tools.ietf.org/html/rfc9110#section-15.6.1", "An error occurred while processing your request."),
            502 => ("https://tools.ietf.org/html/rfc9110#section-15.6.3", "Bad Gateway"),
            503 => ("https://tools.ietf.org/html/rfc9110#section-15.6.4", "Service Unavailable"),
            504 => ("https://tools.ietf.org/html/rfc9110#section-15.6.5", "Gateway Timeout"),
            _ => (null, null),
        };
    }

    /// <summary>
    /// Adds one string member to the body's extensions, as a <see cref="JsonElement" /> and without the
    /// serializer: the extension data is read back as elements, and writing one through the reflection
    /// resolver is what a trimmed carrier cannot do.
    /// </summary>
    private static void Extend(ProblemDetailsDto problem, string name, string value)
    {
        using JsonDocument document = JsonDocument.Parse("\"" + JsonEncodedText.Encode(value) + "\"");
        (problem.Extensions ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal))[name] = document.RootElement.Clone();
    }

    private static string WithInnerMessage(Exception exception)
    {
        return exception.InnerException is not null ? $"{exception.Message} {exception.InnerException.Message}" : exception.Message;
    }
}
