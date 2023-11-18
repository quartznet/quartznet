using Nuke.Common.CI.GitHubActions;

[GitHubActions(
    "pr-tests-unit",
    GitHubActionsImage.WindowsLatest,
    GitHubActionsImage.UbuntuLatest,
    GitHubActionsImage.MacOsLatest,
    OnPullRequestBranches = new [] { "main", "v4", "3.x" },
    OnPullRequestIncludePaths = new[] { "**/*" },
    OnPullRequestExcludePaths = new[] { "docs/**/*", "package.json", "readme.md" },
    PublishArtifacts = false,
    InvokedTargets = new[] { nameof(Compile), nameof(UnitTest) },
    CacheKeyFiles = new[] { "global.json", "src/**/*.csproj", "src/**/package.json" }),
]
[GitHubActions(
    "pr-tests-integration-postgres",
    GitHubActionsImage.UbuntuLatest,
    OnPullRequestBranches = new [] { "main", "v4", "3.x" },
    OnPullRequestIncludePaths = new[] { "**/*" },
    OnPullRequestExcludePaths = new[] { "docs/**/*", "package.json", "package-lock.json", "readme.md" },
    PublishArtifacts = false,
    InvokedTargets = new[] { nameof(Compile), nameof(IntegrationTest) },
    CacheKeyFiles = new[] { "global.json", "src/**/*.csproj", "src/**/package.json" }),
]
[GitHubActions(
    "build",
    GitHubActionsImage.WindowsLatest,
    GitHubActionsImage.UbuntuLatest,
    GitHubActionsImage.MacOsLatest,
    OnPushBranches = new [] { "main", "3.x" },
    OnPushTags = new[] { "v*.*.*" },
    OnPushIncludePaths = new[] { "**/*" },
    OnPushExcludePaths = new[] { "docs/**/*", "package.json", "readme.md" },
    PublishArtifacts = true,
    InvokedTargets = new[] { nameof(Compile), nameof(UnitTest), nameof(IntegrationTest), nameof(Pack), nameof(Publish) },
    ImportSecrets = new [] { "NUGET_API_KEY", "FEEDZ_API_KEY" },
    CacheKeyFiles = new[] { "global.json", "src/**/*.csproj", "src/**/package.json" })
]
public partial class Build
{
}
