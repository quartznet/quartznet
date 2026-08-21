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

public sealed class HttpClientOptions
{
    /// <summary>
    /// Name of the scheduler, must be same as the remote scheduler.
    /// </summary>
    public string SchedulerName { get; set; } = null!;

    /// <summary>
    /// If given, IHttpClientFactory is used to fetch HttpClient with this name.
    /// </summary>
    /// <remarks>
    /// Either this or HttpClient must be given
    /// </remarks>
    public string? HttpClientName { get; set; }

    /// <summary>
    /// If given this HttpClient will be used
    /// </summary>
    /// <remarks>
    /// Either this or HttpClientName must be given
    /// </remarks>
    public System.Net.Http.HttpClient? HttpClient { get; set; }

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
        if (!hasName && options.HttpClient is null)
        {
            (failures ??= []).Add(
                $"Either {nameof(HttpClientOptions.HttpClientName)} or {nameof(HttpClientOptions.HttpClient)} is required.");
        }
        else if (hasName && options.HttpClient is not null)
        {
            (failures ??= []).Add(
                $"{nameof(HttpClientOptions.HttpClientName)} and {nameof(HttpClientOptions.HttpClient)} are both set, and only one can be.");
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