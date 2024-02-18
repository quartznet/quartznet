using Nuke.Common;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Components;

using static Nuke.Common.Tools.DotNet.DotNetTasks;

public partial class Build
{
    string NuGetSource => "https://api.nuget.org/v3/index.json";
    [Parameter] [Secret] readonly string NuGetApiKey;

    string FeedzSource => "https://f.feedz.io/quartznet/quartznet/nuget/index.json";
    [Parameter] [Secret] readonly string FeedzApiKey;

    string ApiKeyToUse => !string.IsNullOrWhiteSpace(TagVersion) ? NuGetApiKey : FeedzApiKey;
    string SourceToUse => !string.IsNullOrWhiteSpace(TagVersion) ? NuGetSource : FeedzSource;

    Target Publish => _ => _
        .OnlyWhenDynamic(() => IsRunningOnWindows && (GitRepository.IsOnMainBranch() || IsTaggedBuild))
        .DependsOn<IPack>()
        .DependsOn(UnitTest)
        .Requires(() => NuGetApiKey)
        .Executes(() =>
        {
            DotNetNuGetPush(_ => _
                    .SetSource(SourceToUse)
                    .SetApiKey(ApiKeyToUse)
                    .EnableSkipDuplicate()
                    .CombineWith(((IPack) this).PackagesDirectory.GlobFiles("*.nupkg"), (_, v) => _.SetTargetPath(v)),
                degreeOfParallelism: 2,
                completeOnFailure: true);
        });
}