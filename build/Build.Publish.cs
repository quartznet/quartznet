using System.Collections.Generic;

using Nuke.Common;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;

using static Nuke.Common.Tools.DotNet.DotNetTasks;

public partial class Build
{
    [Parameter] string NuGetSource => "https://api.nuget.org/v3/index.json";
    [Parameter] [Secret] string NuGetApiKey;

    Target Publish => _ => _
        .OnlyWhenDynamic(() => GitRepository.IsOnMainBranch() && !string.IsNullOrWhiteSpace(TagVersion))
        .DependsOn(Pack)
        .Requires(() => NuGetApiKey)
        .Executes(() =>
        {
            DotNetNuGetPush(_ => _
                    .Apply(PushSettingsBase)
                    .Apply(PushSettings)
                    .CombineWith(PushPackageFiles, (_, v) => _
                        .SetTargetPath(v))
                    .Apply(PackagePushSettings),
                PushDegreeOfParallelism,
                PushCompleteOnFailure);
        });

    Configure<DotNetNuGetPushSettings> PushSettingsBase => _ => _
        .SetSource(NuGetSource)
        .SetApiKey(NuGetApiKey)
        .SetSkipDuplicate(true);

    Configure<DotNetNuGetPushSettings> PushSettings => _ => _;
    Configure<DotNetNuGetPushSettings> PackagePushSettings => _ => _;

    IEnumerable<AbsolutePath> PushPackageFiles => ArtifactsDirectory.GlobFiles("*.nupkg");

    bool PushCompleteOnFailure => true;
    int PushDegreeOfParallelism => 2;

}