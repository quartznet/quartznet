using Nuke.Common.CI.GitHubActions;
using Nuke.Components;

[GitHubActions(
    "pr-tests-unit",
    GitHubActionsImage.WindowsLatest,
    GitHubActionsImage.UbuntuLatest,
    GitHubActionsImage.MacOsLatest,
    OnPullRequestBranches = ["main", "3.x"],
    OnPullRequestIncludePaths = ["**/*"],
    OnPullRequestExcludePaths = ["docs/**/*", "package.json", "package-lock.json", "readme.md"],
    PublishArtifacts = false,
    InvokedTargets = [nameof(ICompile.Compile), nameof(UnitTest), nameof(PublishAot)],
    CacheKeyFiles = []
)]
[GitHubActions(
    "pr-tests-integration-postgres",
    GitHubActionsImage.UbuntuLatest,
    OnPullRequestBranches = ["main", "3.x"],
    OnPullRequestIncludePaths = ["**/*"],
    OnPullRequestExcludePaths = ["docs/**/*", "package.json", "package-lock.json", "readme.md"],
    PublishArtifacts = false,
    InvokedTargets = [nameof(ICompile.Compile), nameof(IntegrationTest)],
    CacheKeyFiles = []
)]
[GitHubActions(
    "build",
    GitHubActionsImage.WindowsLatest,
    GitHubActionsImage.UbuntuLatest,
    GitHubActionsImage.MacOsLatest,
    OnPushBranches = ["main", "3.x"],
    OnPushTags = ["v*.*.*"],
    OnPushIncludePaths = ["**/*"],
    OnPushExcludePaths = ["docs/**/*", "package.json", "package-lock.json", "readme.md"],
    PublishArtifacts = true,
    InvokedTargets = [nameof(ICompile.Compile), nameof(UnitTest), nameof(IntegrationTest), nameof(IPack.Pack), nameof(Publish)],
    ImportSecrets = ["NUGET_API_KEY", "FEEDZ_API_KEY"],
    CacheKeyFiles = []
)]
public partial class Build;