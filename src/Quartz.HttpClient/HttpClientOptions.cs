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

using System.Text.Json;

using Microsoft.Extensions.Options;

namespace Quartz;

/// <summary>
/// What <c>AddQuartzHttpClient</c> needs to build an <see cref="HttpScheduler" />: which remote
/// scheduler to address, and which <see cref="System.Net.Http.HttpClient" /> to reach it with.
/// </summary>
/// <remarks>
/// The client is named or built, never held: exactly one of <see cref="HttpClientName" /> and
/// <see cref="CreateHttpClient" /> is given, and the options are validated when the scheduler is first
/// resolved rather than when they are bound.
/// </remarks>
public sealed class HttpClientOptions
{
    /// <summary>
    /// Name of the scheduler, must be same as the remote scheduler.
    /// </summary>
    public string SchedulerName { get; set; } = null!;

    /// <summary>
    /// The name the client is registered under with <c>AddHttpClient</c>, resolved through
    /// <see cref="System.Net.Http.IHttpClientFactory"/>.
    /// </summary>
    /// <remarks>
    /// Either this or <see cref="CreateHttpClient"/> must be given, and not both. This is the shape to
    /// reach for: the factory pools and recycles handlers, which is what keeps a long-lived client from
    /// pinning stale DNS.
    /// </remarks>
    public string? HttpClientName { get; set; }

    /// <summary>
    /// Builds the client to call the remote scheduler with, for a client that is not registered by name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Either this or <see cref="HttpClientName"/> must be given, and not both. It runs once, when the
    /// scheduler is first resolved, and is handed the container so that a client assembled from other
    /// services — a handler, a token provider — can be built here.
    /// </para>
    /// <para>
    /// The client it returns is not disposed by the scheduler: whoever created it owns it. This is a
    /// factory rather than a client for that reason — an options object is bound, cached and shared, and
    /// a live <see cref="System.Net.Http.HttpClient"/> sitting in one is a disposable resource with no
    /// owner and no way to bind it from configuration.
    /// </para>
    /// </remarks>
    public Func<IServiceProvider, System.Net.Http.HttpClient>? CreateHttpClient { get; set; }

    /// <summary>
    /// Optional json serializer options to be used by the HTTP scheduler
    /// </summary>
    public JsonSerializerOptions? JsonSerializerOptions { get; set; }

}

/// <summary>
/// Validates <see cref="HttpClientOptions"/>.
/// </summary>
/// <remarks>
/// An <see cref="IValidateOptions{TOptions}"/> rather than a private <c>AssertValid</c> throwing
/// <see cref="InvalidOperationException"/>, so a misconfigured HTTP client reports itself the same way
/// every other Quartz options type does. It is run where the options are built, since
/// <c>AddQuartzHttpClient</c> constructs them at registration rather than resolving them from the
/// container.
/// </remarks>
internal sealed class HttpClientOptionsValidator : IValidateOptions<HttpClientOptions>
{
    public ValidateOptionsResult Validate(string? name, HttpClientOptions options)
    {
        List<string>? failures = null;

        if (string.IsNullOrWhiteSpace(options.SchedulerName))
        {
            (failures ??= []).Add($"{nameof(HttpClientOptions.SchedulerName)} is required, and must match the remote scheduler's name.");
        }

        var hasName = !string.IsNullOrWhiteSpace(options.HttpClientName);
        if (!hasName && options.CreateHttpClient is null)
        {
            (failures ??= []).Add(
                $"Either {nameof(HttpClientOptions.HttpClientName)} or {nameof(HttpClientOptions.CreateHttpClient)} is required.");
        }
        else if (hasName && options.CreateHttpClient is not null)
        {
            (failures ??= []).Add(
                $"{nameof(HttpClientOptions.HttpClientName)} and {nameof(HttpClientOptions.CreateHttpClient)} are both set, and only one can be.");
        }

        return failures is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// Validates the options and throws the same exception the options pattern would.
    /// </summary>
    internal static void ThrowIfInvalid(HttpClientOptions options)
    {
        var result = new HttpClientOptionsValidator().Validate(Options.DefaultName, options);
        if (result.Failed)
        {
            throw new OptionsValidationException(
                Options.DefaultName,
                typeof(HttpClientOptions),
                result.Failures ?? []);
        }
    }
}