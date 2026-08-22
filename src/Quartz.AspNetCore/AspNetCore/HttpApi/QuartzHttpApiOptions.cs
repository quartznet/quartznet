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

using Microsoft.Extensions.Options;

namespace Quartz;

/// <summary>
/// How the HTTP API is served.
/// </summary>
/// <remarks>
/// There is one set of these per process, not one per scheduler. The API serves every scheduler in the
/// container through one set of endpoints — a request names the scheduler it is for — so what is
/// configured here describes the endpoints rather than any scheduler. Calling
/// <c>AddQuartzHttpApi(configure)</c> from inside two <c>AddQuartz</c> callbacks therefore configures the
/// same options twice, and the last callback registered wins for any setting both of them touch.
/// </remarks>
public sealed class QuartzHttpApiOptions
{
    internal const string DefaultApiPath = "/quartz-api";

    /// <summary>
    /// The path the API is served under. It is a property of the process, not of a scheduler: every
    /// scheduler is reached under this one path.
    /// </summary>
    /// <remarks>
    /// <c>MapQuartzHttpApi(pattern)</c> says the same thing where the endpoints are mapped, which is
    /// where the rest of an application's routes are written, and a pattern given there wins over this.
    /// </remarks>
    public string ApiPath { get; set; } = DefaultApiPath;

    /// <summary>
    /// Whether a failure's stack trace is included in the problem details returned to the caller.
    /// </summary>
    public bool IncludeStackTraceInProblemDetails { get; set; }

    internal string TrimmedApiPath => ApiPath.TrimEnd('/');

    /// <summary>
    /// Whether a value is usable as the path the API is served under.
    /// </summary>
    /// <remarks>
    /// Shared with <c>MapQuartzHttpApi(pattern)</c>, so a pattern given at the map site is held to the
    /// same rule as one configured at registration — the options validator has already run by then.
    /// </remarks>
    internal static bool IsRoutableApiPath(string? path) => !string.IsNullOrWhiteSpace(path) && path.StartsWith('/');
}

/// <summary>
/// Validates <see cref="QuartzHttpApiOptions"/>.
/// </summary>
/// <remarks>
/// An <see cref="IValidateOptions{TOptions}"/> rather than an <c>AddOptions().Validate(lambda)</c>, so
/// every Quartz configuration mistake produces one exception type from one place — see the core
/// validators in <c>Quartz.Configuration</c>.
/// </remarks>
internal sealed class QuartzHttpApiOptionsValidator : IValidateOptions<QuartzHttpApiOptions>
{
    public ValidateOptionsResult Validate(string? name, QuartzHttpApiOptions options)
    {
        if (!QuartzHttpApiOptions.IsRoutableApiPath(options.ApiPath))
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(QuartzHttpApiOptions.ApiPath)} is required and must start with '/', was '{options.ApiPath}'.");
        }

        return ValidateOptionsResult.Success;
    }
}