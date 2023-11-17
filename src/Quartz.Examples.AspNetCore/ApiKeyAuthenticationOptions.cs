using Microsoft.AspNetCore.Authentication;

namespace Quartz.Examples.AspNetCore;

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string Scheme = "api-key";

    public string? AllowedApiKey { get; set; }
}