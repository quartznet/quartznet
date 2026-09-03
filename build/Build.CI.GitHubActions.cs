using System;
using System.Collections.Generic;
using System.Linq;

using Fallout.Common.CI.GitHubActions;
using Fallout.Components;

using Quartz.Build;

[GitHubActions(
    "pr-tests-unit",
    GitHubActionsImage.WindowsLatest,
    GitHubActionsImage.UbuntuLatest,
    GitHubActionsImage.MacOsLatest,
    OnPullRequestBranches = ["main", "3.x"],
    OnPullRequestIncludePaths = ["**/*"],
    OnPullRequestExcludePaths = ["docs/**/*", "package.json", "package-lock.json", "readme.md"],
    PublishArtifacts = false,
    // PublishAot publishes the trim canary as a native executable and runs it, on every image this
    // workflow covers. macOS is included rather than assumed: the runner image ships the Xcode command
    // line tools ILCompiler links with, so there is nothing to install, and if that stops being true the
    // leg says so out loud rather than the claim going unchecked on a third platform.
    //
    // BenchmarkSmoke, WolverineSmoke and ExamplesSmoke are on this workflow alone. Every change reaches
    // main through a pull request, so this is where a broken benchmark or a broken example is caught
    // while somebody is still looking; the push and release legs have ten-minute budgets and nothing to
    // do with any of them. ExamplesSmoke runs the two applications PublishTrimmed only ever published,
    // on all three images, because a host that refuses to start does so per platform.
    InvokedTargets = [nameof(VerifyMigrations), nameof(VerifySchema), nameof(ICompile.Compile), nameof(UnitTest), nameof(BenchmarkSmoke), nameof(WolverineSmoke), nameof(ExamplesSmoke), nameof(PublishTrimmed), nameof(PublishAot)],
    CacheKeyFiles = [],
    // Generating native code is minutes rather than seconds, and it happens after everything else here.
    TimeoutMinutes = 20,
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
[DatabaseIntegrationGitHubActions("pr-integration-redis", "redis")]
// The push leg runs 'basic' — the container-free negation of every db-* category. Left unset the build
// defaults to "all", which starts six database containers inside a ten-minute job; per-database coverage
// is what the pr-integration-* workflows above are for.
[DatabaseGitHubActions(
    "build",
    "basic",
    GitHubActionsImage.WindowsLatest,
    GitHubActionsImage.UbuntuLatest,
    GitHubActionsImage.MacOsLatest,
    OnPushBranches = ["main", "3.x"],
    OnPushIncludePaths = ["**/*"],
    OnPushExcludePaths = ["docs/**/*", "package.json", "package-lock.json", "readme.md"],
    PublishArtifacts = true,
    PublishCondition = "${{ runner.os == 'Windows' }}",
    InvokedTargets = [nameof(ICompile.Compile), nameof(UnitTest), nameof(IntegrationTest), nameof(IPack.Pack), nameof(Publish)],
    ImportSecrets = ["FEEDZ_API_KEY"],
    CacheKeyFiles = [],
    TimeoutMinutes = 10,
    ReadPermissions = [GitHubActionsPermissions.Contents]
)]
// Releases live in their own workflow file because a nuget.org trusted publishing policy is scoped by
// workflow file name and offers no branch or tag filter — keeping this separate from 'build' is what
// stops every ordinary CI run from being able to mint a nuget.org API key.
//
// VerifyMigrations and VerifySchema lead, as they do on the pull request leg. Both are generated
// artefacts a release ships — database/migrations/ is what an upgrading deployment runs, and the
// embedded create-if-missing schema is what ProvisionSchema executes — and a tag is the last moment
// either can still be wrong. They cost seconds and they run before anything is built, so a stale
// script fails the release in the first minute rather than after the packages have been made.
[GitHubActions(
    "publish",
    GitHubActionsImage.WindowsLatest,
    OnPushTags = ["v*.*.*"],
    PublishArtifacts = true,
    InvokedTargets = [nameof(VerifyMigrations), nameof(VerifySchema), nameof(ICompile.Compile), nameof(UnitTest), nameof(IPack.Pack), nameof(Publish)],
    CacheKeyFiles = [],
    TimeoutMinutes = 20,
    EnvironmentName = "nuget",
    ReadPermissions = [GitHubActionsPermissions.Contents],
    WritePermissions = [GitHubActionsPermissions.IdToken]
)]
public partial class Build;

namespace Quartz.Build
{
    /// <summary>
    /// A workflow that pins the database its integration tests run against. The database is handed to the
    /// build as an <c>env:</c> entry on the generated run step, which Fallout resolves into the
    /// <c>Database</c> parameter — the same mechanism <see cref="GitHubActionsAttribute.ImportSecrets"/>
    /// uses, so no custom step needs to be written.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    internal class DatabaseGitHubActionsAttribute : GitHubActionsAttribute
    {
        readonly string database;

        public DatabaseGitHubActionsAttribute(
            string name,
            string database,
            GitHubActionsImage image,
            params GitHubActionsImage[] images)
            : base(name, image, images)
        {
            this.database = database;
        }

        protected override IEnumerable<(string Key, string Value)> GetImports()
        {
            return base.GetImports().Concat([("Database", database)]);
        }
    }

    /// <summary>
    /// Preset for the per-database integration workflows: one Ubuntu job per database, triggered by pull
    /// requests, running nothing but a compile and the integration tests for that one database.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    internal sealed class DatabaseIntegrationGitHubActionsAttribute : DatabaseGitHubActionsAttribute
    {
        public DatabaseIntegrationGitHubActionsAttribute(string name, string database)
            : base(name, database, GitHubActionsImage.UbuntuLatest)
        {
            OnPullRequestBranches = ["main", "3.x"];
            OnPullRequestIncludePaths = ["**/*"];
            OnPullRequestExcludePaths = ["docs/**/*", "package.json", "package-lock.json", "readme.md"];
            PublishArtifacts = false;
            InvokedTargets = [nameof(ICompile.Compile), "IntegrationTest"];
            CacheKeyFiles = [];
            TimeoutMinutes = 10;
            ConcurrencyCancelInProgress = true;
            ReadPermissions = [GitHubActionsPermissions.Contents];
        }
    }
}
