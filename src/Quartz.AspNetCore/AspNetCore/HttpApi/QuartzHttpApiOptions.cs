using Microsoft.Extensions.Options;

namespace Quartz;

public sealed class QuartzHttpApiOptions
{
    public string ApiPath { get; set; } = "/quartz-api";
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