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

using Quartz.HttpApiContract;

namespace Quartz.AspNetCore.HttpApi.Util;

/// <summary>
/// Turns what a remote scheduler's transport raised into the <see cref="TargetFaultException" /> the
/// answer is, for a request that named a target.
/// </summary>
/// <remarks>
/// <para>
/// No timeout of its own: the transport's applies — an agent's <c>OperationTimeout</c>, an HTTP target's
/// client timeout — and the caller's cancellation flows to the target, so a second layer would only race
/// the first. What it decides is whose problem a failure is, which the status says.
/// </para>
/// <para>
/// It reads the BCL's types only. <c>HttpScheduler</c> raises a status it was answered with as an
/// <see cref="HttpRequestException" /> — on its own, or inside the <c>HttpClientException</c> it raises for
/// a body with problem details — and an agent that is not connected raises one with no status at all.
/// </para>
/// </remarks>
internal static class TargetFaults
{
    /// <summary>
    /// What to answer <paramref name="exception" /> as — a <see cref="TargetFaultException" />, or the
    /// <see cref="NotServedException" /> a route the target does not serve is — or <see langword="null" /> for
    /// one that is answered as it always was: a target's <c>400</c> naming a scheduler exception, its
    /// <c>404</c> with problem details, anything this process raised itself, and the caller going away.
    /// </summary>
    /// <param name="exception">What the action against the remote scheduler raised.</param>
    /// <param name="key">The key the request resolved, whose target is named in the answer.</param>
    /// <param name="mutation">Whether the route changes something, which a timeout then may have done.</param>
    /// <param name="requestAborted">The caller's own token, which tells their cancellation from a timeout.</param>
    public static Exception? Translate(Exception exception, SchedulerRef key, bool mutation, CancellationToken requestAborted)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(key);

        string target = key.Target ?? key.SchedulerName;

        return exception switch
        {
            OperationCanceledException when !requestAborted.IsCancellationRequested => new TargetFaultException(
                target, TargetFault.TimedOut, $"Target '{target}' did not answer in time.", mayHaveApplied: mutation, ReasonOf(exception)),

            // Nothing was sent: the connection, the name or the TLS handshake failed first, or an agent is not
            // connected. Only that is "not reachable"; a retry cannot run anything twice.
            HttpRequestException { StatusCode: null } unreachable when NothingWasSent(unreachable) => new TargetFaultException(
                target, TargetFault.Unavailable, $"Target '{target}' is not reachable.", mayHaveApplied: false, ReasonOf(unreachable)),

            // Everything else without a status failed after the request went: the answer ended early, was not
            // HTTP, or broke the protocol. The target may have run it, so a change says it may have been applied.
            HttpRequestException { StatusCode: null } broken => new TargetFaultException(
                target, TargetFault.Failed, Failed(target), mayHaveApplied: mutation, ReasonOf(broken)),

            // A 404 with no problem details is a target older than the route, which HttpScheduler raises as
            // it is when no version check of its own came first; a NotSupportedException is one that did. Both
            // say the target does not serve this, which is the 501 the caller's own client reads as that.
            HttpRequestException { StatusCode: HttpStatusCode.NotFound } => new NotServedException(new NotSupportedException(
                $"Target '{target}' does not serve this route: it answered 404 without problem details, which a Quartz HTTP API "
                + "older than the route does. Upgrade the target's host.")),

            NotSupportedException unserved => new NotServedException(unserved),

            HttpRequestException { StatusCode: { } status } => FromStatus(target, status, targetDetail: null),

            SchedulerException { InnerException: HttpRequestException { StatusCode: { } status } answered } => FromStatus(target, status, answered.Message),

            JsonException => new TargetFaultException(
                target, TargetFault.Failed, Failed(target), mayHaveApplied: false, "unreadable answer"),

            _ => null,
        };
    }

    private static TargetFaultException? FromStatus(string target, HttpStatusCode status, string? targetDetail)
    {
        string reason = "HTTP " + ((int) status).ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (status == HttpStatusCode.Unauthorized)
        {
            return new TargetFaultException(target, TargetFault.CredentialRefused, $"Target '{target}' refused this host's credential.", mayHaveApplied: false, reason);
        }

        if (status == HttpStatusCode.Forbidden)
        {
            // The target's detail names the operation and the level it needs, which is what a caller refused
            // there has to know; it says nothing about where the target is.
            string detail = string.IsNullOrWhiteSpace(targetDetail)
                ? $"Target '{target}' refused the request."
                : $"Target '{target}' refused: {targetDetail}";
            return new TargetFaultException(target, TargetFault.Refused, detail, mayHaveApplied: false, reason);
        }

        if ((int) status >= 500)
        {
            // Not the target's text: a fault's detail may carry the target's internals.
            return new TargetFaultException(target, TargetFault.Failed, Failed(target), mayHaveApplied: false, reason);
        }

        return null;
    }

    private static string Failed(string target) => $"Target '{target}' failed.";

    /// <summary>
    /// Whether the failure came before the request was sent: a refused or failed connection, a name that did not
    /// resolve, a TLS handshake that failed. The dashboard's agent transport raises a connection error for an
    /// agent that is not connected.
    /// </summary>
    private static bool NothingWasSent(HttpRequestException exception)
    {
        return exception.HttpRequestError is HttpRequestError.ConnectionError
            or HttpRequestError.NameResolutionError
            or HttpRequestError.SecureConnectionError;
    }

    /// <summary>
    /// What failed, as a class rather than a message: a transport's message names the address it could not
    /// reach, and the host's log is told the target's name instead.
    /// </summary>
    private static string ReasonOf(Exception exception)
    {
        return exception is HttpRequestException { HttpRequestError: not HttpRequestError.Unknown } request
            ? $"{nameof(HttpRequestException)} ({request.HttpRequestError})"
            : exception.GetType().Name;
    }
}
