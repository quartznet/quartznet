using Nuke.Common.CI.GitHubActions;

[GitHubActionsAttribute(
    "pr",
    GitHubActionsImage.WindowsLatest,
    GitHubActionsImage.UbuntuLatest,
    GitHubActionsImage.MacOsLatest,
    OnPullRequestBranches = new [] { "main", "v4", "3.x" },
    OnPullRequestIncludePaths = new[] { "**/*" },
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
    OnPushBranches = new [] { "main", "3.x" },
    OnPushIncludePaths = new[] { "**/*" },
    OnPushExcludePaths = new[] { "docs/**/*", "package.json", "readme.md" },
    PublishArtifacts = true,
    InvokedTargets = new[] { nameof(Compile), nameof(Test), nameof(Pack), nameof(Publish) },
    ImportSecrets = new [] { "NUGET_API_KEY", "MYGET_API_KEY" },
    CacheKeyFiles = new[] { "global.json", "src/**/*.csproj", "src/**/package.json" })
]
public partial class Build
{
}
