using System;
using System.Collections.Generic;

using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;

using static Nuke.Common.Tools.DotNet.DotNetTasks;

public partial class Build
{
    string NuGetSource => "https://api.nuget.org/v3/index.json";
    [Parameter] [Secret] string NuGetApiKey;

    string FeedzSource => "https://f.feedz.io/quartznet/quartznet/nuget/index.json";
    [Parameter] [Secret] string FeedzApiKey;

    string ApiKeyToUse => !string.IsNullOrWhiteSpace(TagVersion) ? NuGetApiKey : FeedzApiKey;
    string SourceToUse => !string.IsNullOrWhiteSpace(TagVersion) ? NuGetSource : FeedzSource;

    Target Publish => _ => _
        .OnlyWhenDynamic(() => IsRunningOnWindows && (string.Equals("3.x", GitRepository.Branch, StringComparison.OrdinalIgnoreCase) || IsTaggedBuild))
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
        .SetSource(SourceToUse)
        .SetApiKey(ApiKeyToUse)
        .SetSkipDuplicate(true);

    Configure<DotNetNuGetPushSettings> PushSettings => _ => _;
    Configure<DotNetNuGetPushSettings> PackagePushSettings => _ => _;

    IEnumerable<AbsolutePath> PushPackageFiles => ArtifactsDirectory.GlobFiles("*.nupkg");

    bool PushCompleteOnFailure => true;
    int PushDegreeOfParallelism => 2;

}