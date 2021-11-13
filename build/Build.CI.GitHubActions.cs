using Nuke.Common.CI.GitHubActions;

[GitHubActionsAttribute(
    "pr",
    GitHubActionsImage.WindowsLatest,
    GitHubActionsImage.UbuntuLatest,
    GitHubActionsImage.MacOsLatest,
    OnPullRequestBranches = new [] { "main", "v4" },
    OnPullRequestExcludePaths = new[] { "docs/**/*", "package.json", "readme.md" },
    PublishArtifacts = false,
    InvokedTargets = new[] { nameof(Compile), nameof(Test), nameof(Pack) },
    CacheKeyFiles = new[] { "global.json", "src/**/*.csproj", "src/**/package.json" }),
]
[GitHubActionsAttribute(
    "build",
    GitHubActionsImage.WindowsLatest,
    GitHubActionsImage.UbuntuLatest,
    GitHubActionsImage.MacOsLatest,
    OnPushBranches = new [] { "main" },
    OnPushExcludePaths = new[] { "docs/**/*", "package.json", "readme.md" },
    PublishArtifacts = true,
    InvokedTargets = new[] { nameof(Compile), nameof(Test), nameof(Pack), nameof(Publish) },
    CacheKeyFiles = new[] { "global.json", "src/**/*.csproj", "src/**/package.json" })
]
public partial class Build
{
}
