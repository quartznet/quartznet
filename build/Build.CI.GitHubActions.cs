using System;
using System.Collections.Generic;
using System.Linq;

using Fallout.Common.CI.GitHubActions;
using Fallout.Common.CI.GitHubActions.Configuration;

using Quartz.Build;

[GitHubActions(
    "pr-tests-unit",
    GitHubActionsImage.WindowsLatest,
    GitHubActionsImage.UbuntuLatest,
    GitHubActionsImage.MacOsLatest,
    OnPullRequestBranches = ["main", "v4", "3.x"],
    OnPullRequestIncludePaths = ["**/*"],
    OnPullRequestExcludePaths = ["docs/**/*", "package.json", "readme.md"],
    PublishArtifacts = false,
    InvokedTargets = [nameof(VerifyMigrations), nameof(Compile), nameof(UnitTest)],
    CacheKeyFiles = [],
    ConcurrencyCancelInProgress = true,
    ReadPermissions = [GitHubActionsPermissions.Contents]),
]
// .NET 11 users of 3.x run its net8.0, net9.0 and net10.0 assemblies on the 11 runtime, and nothing above
// does (#4211). This leg builds with the SDK global.json names and runs the unit tests with every test host
// rolled forward to the newest runtime installed, which UnitTestRuntime requires to be 11; ConfigureSteps
// installs it. Ubuntu alone, so net472 is not part of it, and on the same branches and paths as
// pr-tests-unit: the whole job is about five minutes.
[GitHubActions(
    "pr-tests-unit-net11",
    GitHubActionsImage.UbuntuLatest,
    OnPullRequestBranches = ["main", "v4", "3.x"],
    OnPullRequestIncludePaths = ["**/*"],
    OnPullRequestExcludePaths = ["docs/**/*", "package.json", "readme.md"],
    PublishArtifacts = false,
    InvokedTargets = [nameof(Compile), nameof(UnitTest)],
    CacheKeyFiles = [],
    TimeoutMinutes = 20,
    ConcurrencyCancelInProgress = true,
    ReadPermissions = [GitHubActionsPermissions.Contents],
    Env = ["UnitTestRuntime: 11"])
]
[DatabaseIntegrationGitHubActions("pr-integration-basic", "basic")]
[DatabaseIntegrationGitHubActions("pr-integration-postgres", "postgres")]
[DatabaseIntegrationGitHubActions("pr-integration-sqlserver", "sqlserver")]
[DatabaseIntegrationGitHubActions("pr-integration-mysql", "mysql")]
[DatabaseIntegrationGitHubActions("pr-integration-oracle", "oracle")]
[DatabaseIntegrationGitHubActions("pr-integration-firebird", "firebird")]
[DatabaseIntegrationGitHubActions("pr-integration-sqlite", "sqlite")]
[DatabaseIntegrationGitHubActions("pr-integration-redis", "redis")]
[GitHubActions(
    "build",
    GitHubActionsImage.WindowsLatest,
    GitHubActionsImage.UbuntuLatest,
    GitHubActionsImage.MacOsLatest,
    OnPushBranches = ["main", "3.x"],
    OnPushIncludePaths = ["**/*"],
    OnPushExcludePaths = ["docs/**/*", "package.json", "readme.md"],
    PublishArtifacts = true,
    PublishCondition = "runner.os == 'Windows'",
    InvokedTargets = [nameof(Compile), nameof(UnitTest), nameof(IntegrationTest), nameof(Pack), nameof(Publish)],
    ImportSecrets = ["FEEDZ_API_KEY"],
    CacheKeyFiles = [],
    ReadPermissions = [GitHubActionsPermissions.Contents])
]
// Releases live in their own workflow file because a nuget.org trusted publishing policy is scoped by
// workflow file name and offers no branch or tag filter — keeping this separate from 'build' is what
// stops every ordinary CI run from being able to mint a nuget.org API key.
//
// DraftRelease is last, and it is why 'contents: write' sits next to 'id-token: write' here — the one
// job that can mint a nuget.org API key can also write to the repository. That was weighed rather than
// assumed: the archive it attaches is already on disk in this job, so a second workflow would have to
// fetch it back across runs through workflow_run, which resolves against the default branch's workflow
// file rather than the tag's. The job is reachable only by pushing a v-tag, it is gated by the 'nuget'
// environment, and it already runs the tagged commit's own build code. DraftRelease is named here for
// the reader even though Publish triggers it — the generated yml is what somebody reads when a release
// goes wrong, and it should say what the release does.
[GitHubActions(
    "publish",
    GitHubActionsImage.WindowsLatest,
    OnPushTags = ["v*.*.*"],
    PublishArtifacts = true,
    InvokedTargets = [nameof(Compile), nameof(UnitTest), nameof(Pack), nameof(Publish), nameof(DraftRelease)],
    CacheKeyFiles = [],
    TimeoutMinutes = 20,
    EnvironmentName = "nuget",
    EnableGitHubToken = true,
    // Contents is write here, so it is not repeated as a read: a permission key can only appear once.
    WritePermissions = [GitHubActionsPermissions.IdToken, GitHubActionsPermissions.Contents])
]
public partial class Build : IConfigureGitHubActions
{
    /// <summary>
    /// Installs the .NET 11 SDK, which carries the runtime, on the leg that runs the unit tests on it.
    /// </summary>
    public void ConfigureSteps(GitHubActionsStepPipeline pipeline)
    {
        if (pipeline.WorkflowName != "pr-tests-unit-net11")
        {
            return;
        }

        pipeline.Insert(GitHubActionsStepPosition.PreRun, new GitHubActionsCustomStep
        {
            Name = "Setup: .NET 11 SDK",
            // Beside the SDK global.json names, which the generated step after this one installs and the
            // build keeps using: global.json takes no part in choosing a runtime. Preview quality until
            // 11.0.0 ships, then drop dotnet-quality for the release. The release the generator pins for
            // its own setup-dotnet step.
            Uses = "actions/setup-dotnet@v6",
            With =
            {
                ["dotnet-version"] = "11.0.x",
                ["dotnet-quality"] = "preview"
            }
        });
    }
}

namespace Quartz.Build
{
    /// <summary>
    /// Preset for the per-database integration workflows. The database under test is handed to the build
    /// as an <c>env:</c> entry on the generated run step, which Fallout resolves into the <c>Database</c>
    /// parameter — the same mechanism <see cref="GitHubActionsAttribute.ImportSecrets"/> uses, so no
    /// custom step needs to be written.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    internal sealed class DatabaseIntegrationGitHubActionsAttribute : GitHubActionsAttribute
    {
        readonly string database;

        public DatabaseIntegrationGitHubActionsAttribute(string name, string database)
            : base(name, GitHubActionsImage.UbuntuLatest)
        {
            this.database = database;

            OnPullRequestBranches = ["main", "v4", "3.x"];
            OnPullRequestIncludePaths = ["**/*"];
            OnPullRequestExcludePaths = ["docs/**/*", "package.json", "package-lock.json", "readme.md"];
            PublishArtifacts = false;
            InvokedTargets = ["Compile", "IntegrationTest"];
            CacheKeyFiles = [];
            ConcurrencyCancelInProgress = true;
            ReadPermissions = [GitHubActionsPermissions.Contents];
        }

        protected override IEnumerable<(string Key, string Value)> GetImports()
        {
            return base.GetImports().Concat([("Database", database)]);
        }
    }
}
