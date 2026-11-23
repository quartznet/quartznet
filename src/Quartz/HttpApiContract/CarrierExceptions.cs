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

namespace Quartz.HttpApiContract;

/// <summary>
/// A read whose subject is absent, raised by a carrier so that <see cref="ProblemDetailsFactory" />
/// answers <c>404</c> in its own terms.
/// </summary>
/// <remarks>
/// In core beside the operation catalogue, because every carrier of the contract — the HTTP API and the
/// dashboard agent — raises the same three refusals and answers them with one body. The messages are the
/// ones the HTTP API has always written, and <c>HttpClientExtensions.EnsureSuccess</c> reads one of them.
/// </remarks>
internal sealed class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }

    public NotFoundException(string? message, Exception? innerException) : base(message, innerException)
    {
    }

    // Keep in sync with Quartz.HttpClientExtensions.EnsureSuccess
    public static NotFoundException ForScheduler(string schedulerName) => new($"Unknown scheduler {schedulerName}");

    /// <summary>
    /// The refusal for a bare name that only targets hold, which names them: the scheduler exists in
    /// this process's listing, under keys a request reaches by naming the target with <c>?target=</c>.
    /// </summary>
    /// <remarks>
    /// Still <c>Unknown scheduler</c> first, which is what <c>HttpClientExtensions.EnsureSuccess</c> reads:
    /// a bare name never reaches a targeted scheduler, so no request to it can succeed.
    /// </remarks>
    public static NotFoundException ForScheduler(string schedulerName, IReadOnlyList<string> targetsHolding)
    {
        if (targetsHolding.Count == 0)
        {
            return ForScheduler(schedulerName);
        }

        return new NotFoundException(
            $"Unknown scheduler {schedulerName}: the name is held only by "
            + $"{(targetsHolding.Count == 1 ? "target" : "targets")} {string.Join(", ", targetsHolding)}. "
            + $"Name one with ?{HttpApiConstants.TargetQueryParameter}={targetsHolding[0]}.");
    }

    public static NotFoundException ForCalendar(string calendarName) => new($"Unknown calendar {calendarName}");

    public static NotFoundException ForJob(JobKey key) => new($"Unknown job {key}");

    public static NotFoundException ForTrigger(TriggerKey key) => new($"Unknown trigger {key}");

    public static NotFoundException ForExecution(string entryId) => new($"Unknown execution {entryId}");

    public static NotFoundException ForJobRunStatus(JobKey key) => new($"No recorded run of job {key}");
}

/// <summary>
/// A read the server cannot answer because what serves it keeps no such thing: a per-job run status,
/// asked of a history store that keeps rows only.
/// </summary>
/// <remarks>
/// <see cref="ProblemDetailsFactory" /> answers it <c>501</c> with problem details naming
/// <see cref="NotSupportedException" />, which the HTTP client raises again as that. It is not the
/// <c>404</c> without problem details a host older than the route answers, and not a server fault: the
/// store's own <see cref="NotSupportedException" /> is the message.
/// </remarks>
internal sealed class NotServedException : Exception
{
    public NotServedException(NotSupportedException innerException) : base(innerException?.Message, innerException)
    {
    }
}

/// <summary>
/// A decision to refuse, raised from inside a handler rather than in front of one.
/// </summary>
/// <remarks>
/// The sibling of <see cref="NotFoundException" />: <see cref="ProblemDetailsFactory" /> turns it into
/// <c>403</c> problem details, and it carries no <c>Quartz-ExceptionType</c> for the same reason the
/// per-scheduler authorization's refusal does not — nothing failed, a rule said no. A scheduler named in
/// a route is checked in front of the endpoint; a job type is named in the body, so it can only be
/// refused once the body has been read.
/// </remarks>
internal sealed class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message)
    {
    }

    /// <summary>
    /// The refusal of a job type an <c>IsJobTypeAllowed</c> predicate did not allow.
    /// </summary>
    /// <remarks>
    /// The detail names the type and nothing else. That name is the caller's own input, so repeating it
    /// tells them which of the jobs they sent was refused and tells them nothing they did not write.
    /// </remarks>
    public static ForbiddenException ForJobType(string jobType) => new($"Job type {jobType} is not allowed");

    /// <summary>
    /// The refusal of a mutating route while <c>QuartzHttpApiOptions.ReadOnly</c> is set.
    /// </summary>
    /// <remarks>
    /// Raised in front of the handler rather than inside one: nothing about the request decides it, so
    /// there is no reason to read a body or look a scheduler up before saying no. The detail is the
    /// operator's configuration and nothing about the caller.
    /// </remarks>
    public static ForbiddenException ForReadOnlyApi() => new(ReadOnlyDetail);

    /// <summary>
    /// What a read-only refusal says, as one constant: a client matching on the detail matches on
    /// something fixed, and the test that pins the wire shape names it rather than repeating it.
    /// </summary>
    public const string ReadOnlyDetail = "The Quartz HTTP API is configured as read-only.";
}

/// <summary>
/// Why a target a request was relayed to did not serve it, which decides the status the carrier answers.
/// </summary>
internal enum TargetFault
{
    /// <summary>Nothing answered: an agent not connected, a refused connection, a name that did not resolve. <c>503</c>.</summary>
    Unavailable,

    /// <summary>No answer within the transport's timeout, which is not the caller giving up. <c>504</c>.</summary>
    TimedOut,

    /// <summary>The target refused the credential this host reaches it with: its <c>401</c>. <c>502</c>.</summary>
    CredentialRefused,

    /// <summary>The target refused the operation: its <c>403</c>, an agent's <c>MaxAccess</c> or its own policy. <c>403</c>.</summary>
    Refused,

    /// <summary>The target answered a fault, or a body this host could not read. <c>502</c>.</summary>
    Failed,
}

/// <summary>
/// A request for a scheduler behind a target that the target did not serve, raised by the carrier that
/// relayed it so that <see cref="ProblemDetailsFactory" /> answers with the status the fault is.
/// </summary>
/// <remarks>
/// <para>
/// Without it a target's <c>403</c> reached the caller as the <c>400</c> a scheduler's own refusal is, and an
/// unreachable target as a <c>500</c>. The status says whose problem it is: <c>503</c> and <c>504</c> the
/// target's reachability, <c>502</c> what the target answered, <c>403</c> a rule at the target.
/// </para>
/// <para>
/// The detail is fixed text naming the target, and never the target's address, which a transport's own
/// exception carries and which is the host's business; the one thing repeated from the target is the
/// detail of its <c>403</c>, which names the operation and the level it needs. It carries no
/// <c>Quartz-ExceptionType</c>, for the reason a refusal does not: nothing the caller can rethrow failed.
/// </para>
/// </remarks>
internal sealed class TargetFaultException : Exception
{
    /// <param name="target">The target the request named.</param>
    /// <param name="kind">Why the target did not serve it.</param>
    /// <param name="detail">What the caller is told.</param>
    /// <param name="mayHaveApplied">
    /// Whether a change may have reached the target although no answer did — a mutation that timed out —
    /// which the detail then says, so a caller reads before retrying.
    /// </param>
    /// <param name="reason">
    /// What failed, as a class rather than a message — <c>HttpRequestException (ConnectionError)</c>,
    /// <c>HTTP 401</c> — for the host's log, which is never told the target's address either.
    /// </param>
    public TargetFaultException(string target, TargetFault kind, string detail, bool mayHaveApplied, string? reason = null)
        : base(mayHaveApplied ? detail + " " + MayHaveAppliedSentence : detail)
    {
        Target = target;
        Kind = kind;
        MayHaveApplied = mayHaveApplied;
        Reason = reason ?? kind.ToString();
    }

    /// <summary>
    /// What a mutation that timed out adds to its detail.
    /// </summary>
    public const string MayHaveAppliedSentence = "The change may or may not have been applied; read before retrying.";

    public string Target { get; }

    public TargetFault Kind { get; }

    public bool MayHaveApplied { get; }

    public string Reason { get; }

    /// <summary>
    /// The status the fault is answered with.
    /// </summary>
    public HttpStatusCode Status => Kind switch
    {
        TargetFault.Unavailable => HttpStatusCode.ServiceUnavailable,
        TargetFault.TimedOut => HttpStatusCode.GatewayTimeout,
        TargetFault.Refused => HttpStatusCode.Forbidden,
        _ => HttpStatusCode.BadGateway,
    };
}
