using System;
using System.Net.Http;

using Fallout.Common;
using Fallout.Common.CI.GitHubActions;
using Fallout.Common.Git;
using Fallout.Common.IO;
using Fallout.Common.Tooling;
using Fallout.Common.Tools.DotNet;
using Fallout.Common.Utilities.Net;
using Fallout.Components;

using Serilog;

using static Fallout.Common.Tools.DotNet.DotNetTasks;

public partial class Build
{
    const string NuGetSource = "https://api.nuget.org/v3/index.json";

    // Trusted publishing (https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing): the
    // audience and token exchange endpoint nuget.org expects, and the nuget.org profile name of the
    // account that created the trusted publishing policy — not the organization owning the packages.
    const string NuGetAudience = "https://www.nuget.org";
    const string NuGetTokenServiceUrl = "https://www.nuget.org/api/v2/token";
    const string NuGetUser = "lahma";

    string FeedzSource => "https://f.feedz.io/quartznet/quartznet/nuget/index.json";
    [Parameter] [Secret] readonly string FeedzApiKey;

    Target Publish => _ => _
        .OnlyWhenDynamic(() => IsRunningOnWindows && (GitRepository.IsOnMainBranch() || IsTaggedBuild))
        .DependsOn<IPack>()
        .DependsOn(UnitTest)
        .Executes(() =>
        {
            var (source, apiKey) = IsTaggedBuild
                ? (NuGetSource, GetTrustedPublishingApiKey())
                : (FeedzSource, Assert.NotNullOrWhiteSpace(FeedzApiKey, "Feedz API key is required for preview pushes"));

            DotNetNuGetPush(_ => _
                    .SetSource(source)
                    .SetApiKey(apiKey)
                    .EnableSkipDuplicate()
                    .CombineWith(((IPack) this).PackagesDirectory.GlobFiles("*.nupkg"), (_, v) => _.SetTargetPath(v)),
                degreeOfParallelism: 2,
                completeOnFailure: true);
        });

    /// <summary>
    /// Exchanges the job's GitHub OIDC token for a short-lived nuget.org API key, so no long-lived
    /// key has to be stored anywhere. Does what <c>NuGet/login@v1</c> does, without taking a
    /// dependency on the marketplace action.
    /// </summary>
    static string GetTrustedPublishingApiKey()
    {
        const string missingOidc = "GitHub OIDC is unavailable — the job needs 'permissions: id-token: write'";
        var requestUrl = Assert.NotNullOrWhiteSpace(Environment.GetEnvironmentVariable("ACTIONS_ID_TOKEN_REQUEST_URL"), missingOidc);
        var requestToken = Assert.NotNullOrWhiteSpace(Environment.GetEnvironmentVariable("ACTIONS_ID_TOKEN_REQUEST_TOKEN"), missingOidc);

        using var client = new HttpClient();
        // nuget.org's token endpoint returns HTTP 400 for a request with no User-Agent, and HttpClient
        // sends none by default. Any non-empty value satisfies it.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("quartznet-build/1.0");

        var idToken = client
            .CreateRequest(HttpMethod.Get, $"{requestUrl}&audience={Uri.EscapeDataString(NuGetAudience)}")
            .WithBearerAuthentication(requestToken)
            .GetResponse()
            .AssertSuccessfulStatusCode()
            .GetBodyAsJsonObject().GetAwaiter().GetResult()["value"].GetValue<string>();

        var body = client
            .CreateRequest(HttpMethod.Post, NuGetTokenServiceUrl)
            .WithBearerAuthentication(idToken)
            .WithJsonContent(new { username = NuGetUser, tokenType = "ApiKey" })
            .GetResponse()
            .AssertResponse(x => x.IsSuccessStatusCode
                ? null
                : $"nuget.org token exchange failed ({(int) x.StatusCode}): {x.Content.ReadAsStringAsync().GetAwaiter().GetResult()}. "
                + $"Check that a trusted publishing policy exists for this repository and workflow file, and that '{NuGetUser}' created it.")
            .GetBodyAsJsonObject().GetAwaiter().GetResult();

        // The action reads 'apiKey', the original design document says 'api_key' — accept either.
        var apiKey = (body["apiKey"] ?? body["api_key"]).GetValue<string>();
        GitHubActions.Instance?.WriteCommand("add-mask", apiKey);
        Log.Information("Obtained short-lived nuget.org API key, expires {Expires}", body["expires"]?.GetValue<string>());
        return apiKey;
    }
}
