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

using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Quartz.Mcp.Impl;

/// <summary>
/// The tokens a destructive tool's preview answers with, and the reading of one when the tool is called again.
/// </summary>
/// <remarks>
/// <para>
/// A token is a <see cref="ConfirmationPayload" /> protected by Data Protection's time-limited protector, base64url.
/// It binds the confirmation to everything the preview was about: the tool, the scheduler's key, the subject, a
/// digest of the arguments without the token, a digest of the caller's identity and a digest of the subject's state
/// as previewed. It also carries a nonce. Nothing in it is a value the caller did not send or could not read, and
/// the digests are one-way.
/// </para>
/// <para>
/// Single use holds without shared state. The state digest is read again before a confirmed change is applied, so
/// a reschedule replayed after it was applied meets a trigger in another state, and an unschedule replayed finds no
/// trigger. The nonce set refuses a second confirmation in this process before either reaches the scheduler.
/// </para>
/// <para>
/// Expiry is checked twice: by the protector, on the wall clock, and against the payload's own expiry on this
/// process's <see cref="TimeProvider" />. Tokens are never logged.
/// </para>
/// </remarks>
internal sealed class ConfirmationTokens
{
    internal const string Purpose = "Quartz.Mcp.Confirmation.v1";

    private const int Version = 1;

    private readonly ITimeLimitedDataProtector protector;
    private readonly TimeProvider timeProvider;
    private readonly IOptions<QuartzMcpOptions> options;
    private readonly ConcurrentDictionary<string, DateTimeOffset> used = new(StringComparer.Ordinal);

    public ConfirmationTokens(IDataProtectionProvider dataProtection, TimeProvider timeProvider, IOptions<QuartzMcpOptions> options)
    {
        ArgumentNullException.ThrowIfNull(dataProtection);

        protector = dataProtection.CreateProtector(Purpose).ToTimeLimitedDataProtector();
        this.timeProvider = timeProvider;
        this.options = options;
    }

    /// <summary>
    /// A token for the preview of <paramref name="tool" /> on <paramref name="subject" />, and when it expires.
    /// </summary>
    public (string Token, DateTimeOffset ExpiresAtUtc) Issue(string tool, string schedulerKey, string subject, string arguments, string caller, string state)
    {
        DateTimeOffset expires = timeProvider.GetUtcNow() + options.Value.ConfirmationLifetime;
        ConfirmationPayload payload = new(
            Version,
            tool,
            schedulerKey,
            subject,
            arguments,
            caller,
            state,
            Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
            expires);

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload, McpJsonContext.Default.ConfirmationPayload);
        return (Base64Url.EncodeToString(protector.Protect(bytes, expires)), expires);
    }

    /// <summary>
    /// What <paramref name="token" /> says, or <see langword="null" /> with <paramref name="refusal" /> saying why
    /// it cannot be read: expired, tampered with, or protected by a key this process does not hold.
    /// </summary>
    public ConfirmationPayload? Read(string token, out string? refusal)
    {
        refusal = null;
        ConfirmationPayload? payload;
        try
        {
            byte[] bytes = protector.Unprotect(Base64Url.DecodeFromChars(token), out _);
            payload = JsonSerializer.Deserialize(bytes, McpJsonContext.Default.ConfirmationPayload);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException)
        {
            payload = null;
        }

        if (payload is null || payload.V != Version || payload.ExpiresAtUtc <= timeProvider.GetUtcNow())
        {
            refusal = "expired or not valid";
            return null;
        }

        return payload;
    }

    /// <summary>
    /// What of the confirmation does not match the call — <c>tool</c>, <c>scheduler</c>, <c>arguments</c> or
    /// <c>caller</c> — or <see langword="null" /> when everything does.
    /// </summary>
    public static string? Mismatch(ConfirmationPayload payload, string tool, string schedulerKey, string arguments, string caller)
    {
        if (!string.Equals(payload.Tool, tool, StringComparison.Ordinal))
        {
            return "tool";
        }

        if (!string.Equals(payload.Scheduler, schedulerKey, StringComparison.Ordinal))
        {
            return "scheduler";
        }

        if (!string.Equals(payload.Args, arguments, StringComparison.Ordinal))
        {
            return "arguments";
        }

        if (!string.Equals(payload.Caller, caller, StringComparison.Ordinal))
        {
            return "caller";
        }

        return null;
    }

    /// <summary>
    /// Spends the confirmation's nonce: <see langword="false" /> when it was spent before in this process.
    /// </summary>
    public bool TrySpend(ConfirmationPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        DateTimeOffset now = timeProvider.GetUtcNow();

        // A spent nonce is kept as long as its token could still be read, and no longer.
        foreach (KeyValuePair<string, DateTimeOffset> entry in used)
        {
            if (entry.Value <= now)
            {
                used.TryRemove(entry);
            }
        }

        return used.TryAdd(payload.Nonce, payload.ExpiresAtUtc);
    }
}
