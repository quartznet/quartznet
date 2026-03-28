using System.Collections.Generic;

using Nuke.Common.CI.GitHubActions;
using Nuke.Common.CI.GitHubActions.Configuration;
using Nuke.Common.Execution;
using Nuke.Common.Utilities;
using Nuke.Components;

[CustomGitHubActions(
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
    ConcurrencyCancelInProgress = true,
    ReadPermissions = [GitHubActionsPermissions.Contents]
)]
[DatabaseIntegrationGitHubActions("pr-integration-basic", "basic")]
[DatabaseIntegrationGitHubActions("pr-integration-postgres", "postgres")]
[DatabaseIntegrationGitHubActions("pr-integration-sqlserver", "sqlserver")]
[DatabaseIntegrationGitHubActions("pr-integration-mysql", "mysql")]
[DatabaseIntegrationGitHubActions("pr-integration-oracle", "oracle")]
[DatabaseIntegrationGitHubActions("pr-integration-firebird", "firebird")]
[DatabaseIntegrationGitHubActions("pr-integration-sqlite", "sqlite")]
[CustomGitHubActions(
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
    TimeoutMinutes = 10,
    ReadPermissions = [GitHubActionsPermissions.Contents]
)]
public partial class Build;

class CustomGitHubActionsAttribute : GitHubActionsAttribute
{
    public CustomGitHubActionsAttribute(string name, GitHubActionsImage image, params GitHubActionsImage[] images) : base(name, image, images)
    {
    }

    protected override GitHubActionsJob GetJobs(GitHubActionsImage image, IReadOnlyCollection<ExecutableTarget> relevantTargets)
    {
        var job = base.GetJobs(image, relevantTargets);

        var newSteps = new List<GitHubActionsStep>(job.Steps);
        newSteps.Insert(0, new GitHubActionsSetupDotNetStep(["10.0"]));
        job.Steps = newSteps.ToArray();

        return job;
    }
}

class DatabaseIntegrationGitHubActionsAttribute : CustomGitHubActionsAttribute
{
    readonly string _database;

    public DatabaseIntegrationGitHubActionsAttribute(string name, string database)
        : base(name, GitHubActionsImage.UbuntuLatest)
    {
        _database = database;
        OnPullRequestBranches = ["main", "3.x"];
        OnPullRequestIncludePaths = ["**/*"];
        OnPullRequestExcludePaths = ["docs/**/*", "package.json", "package-lock.json", "readme.md"];
        PublishArtifacts = false;
        InvokedTargets = ["Compile", "IntegrationTest"];
        CacheKeyFiles = [];
        TimeoutMinutes = 10;
        ConcurrencyCancelInProgress = true;
        ReadPermissions = [GitHubActionsPermissions.Contents];
    }

    protected override GitHubActionsJob GetJobs(GitHubActionsImage image, IReadOnlyCollection<ExecutableTarget> relevantTargets)
    {
        var job = base.GetJobs(image, relevantTargets);
        var newSteps = new List<GitHubActionsStep>(job.Steps);

        for (int i = 0; i < newSteps.Count; i++)
        {
            if (newSteps[i] is GitHubActionsRunStep)
            {
                newSteps[i] = new DatabaseIntegrationRunStep(_database);
            }
        }

        job.Steps = newSteps.ToArray();
        return job;
    }
}

class DatabaseIntegrationRunStep : GitHubActionsStep
{
    readonly string _database;

    public DatabaseIntegrationRunStep(string database)
    {
        _database = database;
    }

    public override void Write(CustomFileWriter writer)
    {
        writer.WriteLine($"- name: 'Run: Compile, IntegrationTest ({_database})'");
        using (writer.Indent())
        {
            writer.WriteLine($"run: ./build.cmd Compile IntegrationTest --database {_database}");
            writer.WriteLine("env:");
            using (writer.Indent())
            {
                writer.WriteLine($"QUARTZ_TEST_DATABASE: {_database}");
            }
        }
    }
}

class GitHubActionsSetupDotNetStep : GitHubActionsStep
{
    public GitHubActionsSetupDotNetStep(string[] versions)
    {
        Versions = versions;
    }

    string[] Versions { get; }

    public override void Write(CustomFileWriter writer)
    {
        writer.WriteLine("- uses: actions/setup-dotnet@v5");

        using (writer.Indent())
        {
            writer.WriteLine("with:");
            using (writer.Indent())
            {
                writer.WriteLine("dotnet-version: |");
                using (writer.Indent())
                {
                    foreach (var version in Versions)
                    {
                        writer.WriteLine(version);
                    }
                }
            }
        }
    }
}
