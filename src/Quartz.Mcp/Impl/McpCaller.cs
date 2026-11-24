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

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.Http;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Quartz.Mcp.Impl;

/// <summary>
/// Who called a tool, as everything the endpoint decides about the call needs to know it: the principal a
/// policy is asked about, the name a record carries, the identity a confirmation is bound to and the window a
/// rate limit counts in, and the client's own name for itself.
/// </summary>
/// <param name="User">The authenticated principal, or an anonymous one.</param>
/// <param name="Name">The principal's name, or <c>(anonymous)</c>: what the log line and the Action Log say.</param>
/// <param name="StableId">
/// The first of <c>NameIdentifier</c>, <c>sub</c>, <c>upn</c> and <c>Name</c>, for an authenticated principal that
/// has one; <see langword="null" /> for a caller a confirmation cannot be bound to.
/// </param>
/// <param name="Digest">
/// A one-way digest of the authentication type, the stable id and its issuer: what a confirmation token and a
/// rate-limit window are keyed by, so neither carries the identity itself.
/// </param>
/// <param name="Client">
/// The client's name for itself — MCP <c>clientInfo</c> when the request carries it, otherwise the product token
/// of its <c>User-Agent</c> — sanitized and at most 64 characters, or <see langword="null" />. Shown as the
/// client stated it, and never used to decide anything.
/// </param>
internal sealed record McpCaller(ClaimsPrincipal User, string Name, string? StableId, string Digest, string? Client)
{
    internal const string Anonymous = "(anonymous)";

    internal const int MaxClientLength = 64;

    /// <summary>Whether a confirmation can be bound to the caller: authenticated, with an identity of its own.</summary>
    public bool HasStableIdentity => StableId is not null;

    /// <summary>What a record names the client as.</summary>
    public string ClientOrUnknown => Client ?? "unknown client";

    /// <summary>
    /// The caller of <paramref name="request" />.
    /// </summary>
    public static McpCaller Of<TParams>(RequestContext<TParams> request, HttpContext? httpContext)
    {
        ArgumentNullException.ThrowIfNull(request);

        ClaimsPrincipal user = request.User ?? httpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());
        Implementation? clientInfo = request.JsonRpcRequest.Context?.ClientInfo ?? request.Server.ClientInfo;
        return From(user, clientInfo, httpContext?.Request.Headers.UserAgent.ToString());
    }

    /// <summary>
    /// The caller a principal and a client's statements make.
    /// </summary>
    internal static McpCaller From(ClaimsPrincipal user, Implementation? clientInfo, string? userAgent)
    {
        ArgumentNullException.ThrowIfNull(user);

        ClaimsIdentity? identity = user.Identities.FirstOrDefault(static x => x.IsAuthenticated);
        string name = string.IsNullOrWhiteSpace(identity?.Name) ? Anonymous : identity!.Name!;

        Claim? id = null;
        if (identity is not null)
        {
            id = identity.FindFirst(ClaimTypes.NameIdentifier)
                 ?? identity.FindFirst("sub")
                 ?? identity.FindFirst(ClaimTypes.Upn)
                 ?? identity.FindFirst(identity.NameClaimType);
            if (string.IsNullOrWhiteSpace(id?.Value))
            {
                id = null;
            }
        }

        string digest = Hash(identity is null || id is null
            ? Anonymous
            : string.Join('\n', identity.AuthenticationType ?? "", id.Value, id.Issuer ?? ""));

        return new McpCaller(user, name, id?.Value, digest, ClientName(clientInfo, userAgent));
    }

    /// <summary>
    /// The client's name: <c>clientInfo</c>'s name and version, or the first product token of the
    /// <c>User-Agent</c>, with control characters taken out and cut to <see cref="MaxClientLength" />.
    /// </summary>
    internal static string? ClientName(Implementation? clientInfo, string? userAgent)
    {
        string? stated = clientInfo is { Name: { Length: > 0 } clientName }
            ? string.IsNullOrWhiteSpace(clientInfo.Version) ? clientName : clientName + "/" + clientInfo.Version
            : FirstProduct(userAgent);

        return Sanitize(stated);
    }

    private static string? FirstProduct(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return null;
        }

        string trimmed = userAgent.Trim();
        int space = trimmed.IndexOf(' ', StringComparison.Ordinal);
        return space < 0 ? trimmed : trimmed[..space];
    }

    private static string? Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        StringBuilder clean = new(Math.Min(value.Length, MaxClientLength));
        foreach (char c in value)
        {
            if (clean.Length == MaxClientLength)
            {
                break;
            }

            if (!char.IsControl(c) && char.GetUnicodeCategory(c) is not (System.Globalization.UnicodeCategory.LineSeparator or System.Globalization.UnicodeCategory.ParagraphSeparator))
            {
                clean.Append(c);
            }
        }

        string result = clean.ToString().Trim();
        return result.Length == 0 ? null : result;
    }

    internal static string Hash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
