using System;
using System.Collections.Generic;
using System.Linq;

using Fallout.Common.CI.GitHubActions;

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
    InvokedTargets = [nameof(Compile), nameof(UnitTest)],
    CacheKeyFiles = [],
    ConcurrencyCancelInProgress = true,
    ReadPermissions = [GitHubActionsPermissions.Contents]),
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
[GitHubActions(
    "publish",
    GitHubActionsImage.WindowsLatest,
    OnPushTags = ["v*.*.*"],
    PublishArtifacts = true,
    InvokedTargets = [nameof(Compile), nameof(UnitTest), nameof(Pack), nameof(Publish)],
    CacheKeyFiles = [],
    TimeoutMinutes = 20,
    EnvironmentName = "nuget",
    ReadPermissions = [GitHubActionsPermissions.Contents],
    WritePermissions = [GitHubActionsPermissions.IdToken])
]
public partial class Build;

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
