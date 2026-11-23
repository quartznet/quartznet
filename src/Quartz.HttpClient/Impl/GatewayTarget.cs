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

using Quartz.HttpApiContract;

namespace Quartz.Impl;

/// <summary>
/// What every request of a registration with <see cref="HttpClientOptions.GatewayTarget" /> does: names the
/// target in the query, and refuses a success that does not say it was the target's.
/// </summary>
/// <remarks>
/// One place, for the three things that talk to a host — the scheduler's transport, its history store and
/// its event reader — so that none of them can be asked for a target and answer for the host's bare name.
/// </remarks>
internal static class GatewayTarget
{
    /// <summary>
    /// <paramref name="path" /> with <c>target=</c> appended, or as it was when there is no gateway target.
    /// </summary>
    public static string Append(string path, string? gatewayTarget)
    {
        if (gatewayTarget is null)
        {
            return path;
        }

        char separator = path.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return string.Concat(path, separator.ToString(), HttpApiConstants.TargetQueryParameter, "=", Uri.EscapeDataString(gatewayTarget));
    }

    /// <summary>
    /// Refuses a success that does not carry <see cref="HttpApiConstants.TargetHeader" /> naming
    /// <paramref name="gatewayTarget" />.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A host older than 4.9 ignores <c>?target=</c> and answers for the scheduler of its own that has the
    /// bare name. In the dashboard host that is a <c>404</c>, because a bare key never reaches a target; in an
    /// application with a scheduler of that name it would be the wrong scheduler's answer, or the wrong
    /// scheduler changed. A success is what could mislead, so a success is what is checked.
    /// </para>
    /// <para>
    /// A failure is left to the ordinary error mapping. A 4.9 host's own <c>401</c> or <c>403</c> from its
    /// authentication, or the <c>404</c> of a route it does not map, is written before anything of Quartz's
    /// runs, so it cannot carry the header, and reading it as "an older host" would hide the real answer.
    /// </para>
    /// </remarks>
    /// <exception cref="HttpClientException">A success without the header, or naming another target.</exception>
    public static void EnsureEchoed(HttpResponseMessage response, string? gatewayTarget)
    {
        if (gatewayTarget is null || !response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.Headers.TryGetValues(HttpApiConstants.TargetHeader, out IEnumerable<string>? echoed)
            && echoed.Any(value => string.Equals(value, gatewayTarget, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        throw new HttpClientException(NotServed(gatewayTarget));
    }

    internal static string NotServed(string gatewayTarget)
    {
        return $"The host answered a request for target '{gatewayTarget}' without the {HttpApiConstants.TargetHeader} header, "
               + "so it does not serve ?target=: a Quartz HTTP API older than 4.9 ignores it and answers for its own "
               + "scheduler of that name. Upgrade the host to 4.9 or later, or address the target's own API.";
    }
}
