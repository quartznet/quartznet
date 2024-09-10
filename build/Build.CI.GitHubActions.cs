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
    CacheKeyFiles = [],
    TimeoutMinutes = 10,
    ConcurrencyCancelInProgress = true
)]
[GitHubActions(
    "pr-tests-integration-postgres",
    GitHubActionsImage.UbuntuLatest,
    OnPullRequestBranches = ["main", "3.x"],
    OnPullRequestIncludePaths = ["**/*"],
    OnPullRequestExcludePaths = ["docs/**/*", "package.json", "package-lock.json", "readme.md"],
    PublishArtifacts = false,
    InvokedTargets = [nameof(ICompile.Compile), nameof(IntegrationTest)],
    CacheKeyFiles = [],
    TimeoutMinutes = 10,
    ConcurrencyCancelInProgress = true
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
    PublishCondition = "${{ runner.os == 'Windows' }}",
    InvokedTargets = [nameof(ICompile.Compile), nameof(UnitTest), nameof(IntegrationTest), nameof(IPack.Pack), nameof(Publish)],
    ImportSecrets = ["NUGET_API_KEY", "FEEDZ_API_KEY"],
    CacheKeyFiles = [],
    TimeoutMinutes = 10
)]
public partial class Build;