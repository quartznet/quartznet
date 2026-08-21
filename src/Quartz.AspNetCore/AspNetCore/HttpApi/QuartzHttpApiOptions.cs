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
    /// <summary>
    /// The path the API is served under. It is a property of the process, not of a scheduler: every
    /// scheduler is reached under this one path.
    /// </summary>
    public string ApiPath { get; set; } = "/quartz-api";

    /// <summary>
    /// Whether a failure's stack trace is included in the problem details returned to the caller.
    /// </summary>
    public bool IncludeStackTraceInProblemDetails { get; set; }

    internal string TrimmedApiPath => ApiPath.TrimEnd('/');
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
        if (string.IsNullOrWhiteSpace(options.ApiPath) || !options.ApiPath.StartsWith('/'))
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(QuartzHttpApiOptions.ApiPath)} is required and must start with '/', was '{options.ApiPath}'.");
        }

        return ValidateOptionsResult.Success;
    }
}