using System.Collections.Generic;

using Nuke.Common.CI.GitHubActions;
using Nuke.Common.CI.GitHubActions.Configuration;
using Nuke.Common.Execution;
using Nuke.Common.Utilities;

[CustomGitHubActions(
    "pr-tests-unit",
    GitHubActionsImage.WindowsLatest,
    GitHubActionsImage.UbuntuLatest,
    GitHubActionsImage.MacOsLatest,
    OnPullRequestBranches = new[] { "main", "v4", "3.x" },
    OnPullRequestIncludePaths = new[] { "**/*" },
    OnPullRequestExcludePaths = new[] { "docs/**/*", "package.json", "package-lock.json", "readme.md" },
    PublishArtifacts = false,
    InvokedTargets = new[] { nameof(Compile), nameof(UnitTest) },
    CacheKeyFiles = new[] { "global.json", "src/**/*.csproj", "src/**/package.json" }),
]
[CustomGitHubActions(
    "pr-tests-integration-postgres",
    GitHubActionsImage.UbuntuLatest,
    OnPullRequestBranches = new[] { "main", "v4", "3.x" },
    OnPullRequestIncludePaths = new[] { "**/*" },
    OnPullRequestExcludePaths = new[] { "docs/**/*", "package.json", "package-lock.json", "readme.md" },
    PublishArtifacts = false,
    InvokedTargets = new[] { nameof(Compile), nameof(IntegrationTest) },
    CacheKeyFiles = new[] { "global.json", "src/**/*.csproj", "src/**/package.json" }),
]
[CustomGitHubActions(
    "build",
    GitHubActionsImage.WindowsLatest,
    GitHubActionsImage.UbuntuLatest,
    GitHubActionsImage.MacOsLatest,
    OnPushBranches = new[] { "main", "3.x" },
    OnPushTags = new[] { "v*.*.*" },
    OnPushIncludePaths = new[] { "**/*" },
    OnPushExcludePaths = new[] { "docs/**/*", "package.json", "package-lock.json", "readme.md" },
    PublishArtifacts = true,
    InvokedTargets = new[] { nameof(Compile), nameof(UnitTest), nameof(IntegrationTest), nameof(Pack), nameof(Publish) },
    ImportSecrets = new[] { "NUGET_API_KEY", "MYGET_API_KEY" },
    CacheKeyFiles = new[] { "global.json", "src/**/*.csproj", "src/**/package.json" })
]
public partial class Build
{
}

class CustomGitHubActionsAttribute : GitHubActionsAttribute
{
    public CustomGitHubActionsAttribute(string name, GitHubActionsImage image, params GitHubActionsImage[] images) : base(name, image, images)
    {
    }

    protected override GitHubActionsJob GetJobs(GitHubActionsImage image, IReadOnlyCollection<ExecutableTarget> relevantTargets)
    {
        var job = base.GetJobs(image, relevantTargets);

        var newSteps = new List<GitHubActionsStep>(job.Steps);
        newSteps.Insert(0, new GitHubActionsUseGnuTarStep());

        job.Steps = newSteps.ToArray();
        return job;
    }
}

class GitHubActionsUseGnuTarStep : GitHubActionsStep
{
    public override void Write(CustomFileWriter writer)
    {
        writer.WriteLine("- if: ${{ runner.os == 'Windows' }}");
        using (writer.Indent())
        {
            writer.WriteLine("name: 'Use GNU tar'");
            writer.WriteLine("shell: cmd");
            writer.WriteLine("run: |");
            using (writer.Indent())
            {
                writer.WriteLine("echo \"Adding GNU tar to PATH\"");
                writer.WriteLine("echo C:\\Program Files\\Git\\usr\\bin>>\"%GITHUB_PATH%\"");
            }
        }
    }
}