using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using Fallout.Common;
using Fallout.Common.IO;
using Fallout.Common.Tooling;
using Fallout.Common.Tools.DotNet;
using Fallout.Common.Utilities;
using Fallout.Components;

using Serilog;

using static Fallout.Common.Tools.DotNet.DotNetTasks;

partial class Build
{
    /// <summary>
    /// The newer runtime <c>pr-tests-unit-net11</c> runs the unit tests on, and the one its consumer
    /// application targets.
    /// </summary>
    const int Net11 = 11;

    /// <summary>
    /// The lowest SDK the consumer's <c>global.json</c> accepts. A prerelease floor, so that a release
    /// candidate satisfies it until 11.0 ships and the released SDK after.
    /// </summary>
    const string Net11SdkFloor = "11.0.100-rc.1";

    /// <summary>
    /// Where the consumer's dashboard is mounted: a custom path, the shape #4224 broke.
    /// </summary>
    /// <remarks>Spelled again in <c>build/net11-consumer/Quartz.Net11Consumer/Program.cs</c>.</remarks>
    const string Net11ConsumerDashboardPath = "/ops/scheduler";

    AbsolutePath Net11ConsumerSources => RootDirectory / "build" / "net11-consumer";

    /// <summary>
    /// What every consumer build and publish is run with. The .NET 11 SDK keeps an MSBuild server and a
    /// compiler server alive after a build by default, and one holding <c>ILCompiler.Build.Tasks.dll</c>
    /// out of the consumer's packages folder stops the next run from cleaning it; nothing of the .NET 11
    /// SDK's is left running on the machine afterwards either.
    /// </summary>
    const string DisableBuildServers = "--disable-build-servers";

    /// <summary>
    /// Builds a <c>net11.0</c> application against the packages <see cref="IPack.Pack" /> made, with the
    /// .NET 11 SDK, runs it, and publishes both trim canaries from the same packages trimmed and natively.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every .NET 11 user runs Quartz's <c>net10.0</c> assemblies on the 11 runtime, and gets them the way
    /// this target does: as packages, restored by the .NET 11 SDK into a <c>net11.0</c> application. NuGet
    /// then prunes the <c>Microsoft.Extensions.*</c> packages the 11 shared framework carries, ASP.NET Core
    /// 11 hosts the dashboard's Razor class library, and the SDK adds what it adds to a Debug build. None of
    /// it happens to the repository's own build, which is <c>net10.0</c> on the SDK <c>global.json</c> names;
    /// #4210 and #4224 were both found by hand.
    /// </para>
    /// <para>
    /// The consumer is <c>build/net11-consumer</c>, copied to a directory outside the repository so that no
    /// <c>global.json</c>, <c>Directory.Build.props</c>, <c>Directory.Packages.props</c> or
    /// <c>NuGet.config</c> of the repository's reaches it; it gets a <c>global.json</c> naming the .NET 11 SDK,
    /// a <c>NuGet.config</c> naming the packages this build made and nuget.org, and a packages folder of its
    /// own, so that a package from an earlier run with the same version is never what it restores.
    /// </para>
    /// <para>
    /// <c>Quartz.Net11Consumer</c> is built in Debug and run with the dashboard at a custom path. It says
    /// which runtime it is on, reads a <see langword="double" /> in job data back as a
    /// <see langword="decimal" /> (#4210), and runs a job from <c>Quartz.Jobs</c>; then the dashboard's page
    /// is asked for, and every module its <c>Blazor-Web-Initializers</c> lists, the SDK's hot-reload
    /// initializer among them, has to answer under the dashboard's path (#4224).
    /// </para>
    /// <para>
    /// Each of <see cref="TrimCanaries" /> is then published from the same packages as a <c>net11.0</c>
    /// application, trimmed and natively, and run, exactly as <see cref="PublishTrimmed" /> and
    /// <see cref="PublishAot" /> run it from the repository; its report is held to the same rule. Built
    /// this way, <c>Quartz.Trimming.Canary</c> is also where the source generator meets the C# 15 compiler
    /// of a <c>net11.0</c> project, and its store check is what says the delegate job was bound at compile
    /// time.
    /// </para>
    /// </remarks>
    Target Net11ConsumerSmoke => _ => _
        .DependsOn<IPack>()
        .After(UnitTest)
        .Executes(() =>
        {
            // The temporary directory, which on a runner and on a developer's machine is outside every
            // repository; asserted, because inside this one the repository's global.json would win. Short,
            // too: a native link on Windows fails on a path over MAX_PATH, and the packages folder is in it.
            AbsolutePath root = (AbsolutePath) Path.GetTempPath() / "quartz-net11";
            Assert.True(!root.ToString().StartsWith(RootDirectory.ToString(), StringComparison.OrdinalIgnoreCase),
                $"The net11.0 consumer has to be built outside the repository, and {root} is inside it.");

            root.CreateOrCleanDirectory();
            foreach (AbsolutePath project in Net11ConsumerSources.GetDirectories())
            {
                project.Copy(root / project.Name);
            }

            (root / "global.json").WriteAllText(
                $$"""
                {
                  "sdk": {
                    "version": "{{Net11SdkFloor}}",
                    "rollForward": "latestFeature",
                    "allowPrerelease": true
                  }
                }
                """);

            (root / "NuGet.config").WriteAllText(
                $"""
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="quartz" value="{System.Security.SecurityElement.Escape(((IPack) this).PackagesDirectory)}" />
                    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                  </packageSources>
                </configuration>
                """);

            foreach (AbsolutePath package in ((IPack) this).PackagesDirectory.GlobFiles($"Quartz*.{FullVersion}.nupkg"))
            {
                AssertTheStaticWebAssetsArePacked(package);
            }

            Dictionary<string, string> environment = Net11ConsumerEnvironment(root);

            string sdk = ProcessTasks.StartProcess(DotNetPath, "--version", root, environment, logOutput: false)
                .AssertZeroExitCode()
                .Output.Select(x => x.Text.Trim()).First(x => x.Length > 0);
            Assert.True(sdk.StartsWith($"{Net11}.", StringComparison.Ordinal),
                $"The net11.0 consumer resolved .NET SDK {sdk} from {root / "global.json"}, which needs a .NET {Net11} SDK of {Net11SdkFloor} or later.");
            Log.Information("The net11.0 consumer builds with .NET SDK {Sdk} from the {Version} packages", sdk, FullVersion);

            AbsolutePath consumer = root / "Quartz.Net11Consumer";
            DotNetBuild(s => s
                .SetProjectFile(consumer / "Quartz.Net11Consumer.csproj")
                .SetConfiguration(Configuration.Debug)
                .SetProperty("QuartzVersion", FullVersion)
                .AddProcessAdditionalArguments(DisableBuildServers)
                .SetProcessWorkingDirectory(root)
                .SetProcessEnvironmentVariables(environment));

            AbsolutePath assembly = consumer / "bin" / "Debug" / $"net{Net11}.0" / "Quartz.Net11Consumer.dll";
            Assert.FileExists(assembly);

            string url = $"http://127.0.0.1:{FreeLoopbackPort()}";
            RunUntilItSays(
                "Quartz.Net11Consumer",
                assembly,
                // From the project directory, as 'dotnet run' runs it: a Debug build's static web assets
                // are served from the packages through the manifest the SDK leaves beside the project.
                consumer,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["ASPNETCORE_ENVIRONMENT"] = "Development",
                    ["ASPNETCORE_URLS"] = url,
                },
                [
                    "Application started",
                    $"Net11Consumer: running on .NET {Net11}.",
                    "Net11Consumer: JobDataMap.Get<decimal> read 12.34d back as 12.34",
                    "Net11Consumer: job no-op ran",
                ],
                () => UnansweredInitializers(url, Net11ConsumerDashboardPath));

            foreach (string canary in TrimCanaries)
            {
                PublishAndRunNet11Canary(root, environment, canary, "trimmed", aot: false);
                PublishAndRunNet11Canary(root, environment, canary, "native AOT", aot: true);
            }
        });

    /// <summary>
    /// The version <c>Directory.Packages.props</c> pins for <paramref name="id" />, which the consumer's
    /// copy of a canary references outright, as the repository's copy does through central package
    /// management.
    /// </summary>
    string PinnedPackageVersion(string id)
    {
        string version = XDocument.Load(RootDirectory / "Directory.Packages.props")
            .Descendants("PackageVersion")
            .FirstOrDefault(x => string.Equals((string) x.Attribute("Include"), id, StringComparison.OrdinalIgnoreCase))
            ?.Attribute("Version")?.Value;

        Assert.True(!string.IsNullOrEmpty(version), $"Directory.Packages.props pins no version of {id}.");
        return version;
    }

    /// <summary>
    /// The environment every process the consumer's SDK runs is given: this build's own, less any MSBuild
    /// setting, with a packages folder inside <paramref name="root" />.
    /// </summary>
    /// <remarks>
    /// A build started from inside another MSBuild process — <c>dotnet run</c>, an IDE's terminal — can
    /// inherit <c>MSBuildExtensionsPath</c>, <c>MSBuildSDKsPath</c> and their kind, and a .NET 11 SDK handed
    /// them would import the pinned SDK's targets. Every <c>MSBuild*</c> variable is dropped, so the consumer
    /// is built by the .NET 11 SDK alone.
    /// </remarks>
    static Dictionary<string, string> Net11ConsumerEnvironment(AbsolutePath root)
    {
        Dictionary<string, string> environment = new(EnvironmentInfo.Variables);

        foreach (string key in environment.Keys.Where(x => x.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            Log.Debug("The net11.0 consumer's SDK is not given {Variable}", key);
            environment.Remove(key);
        }

        environment["NUGET_PACKAGES"] = root / "packages";
        environment["DOTNET_NOLOGO"] = "1";
        environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

        return environment;
    }

    /// <summary>
    /// Fails when a package's <c>build/Microsoft.AspNetCore.StaticWebAssets.props</c> names a file under
    /// <c>staticwebassets/</c> that the package does not carry.
    /// </summary>
    /// <remarks>
    /// A consumer's build stops on such a package with an error that names the missing file and nothing
    /// else, so this says what the package carries instead. The <c>Quartz.Dashboard</c> package packed on
    /// ubuntu-latest named a scoped CSS bundle it did not carry (#4211).
    /// </remarks>
    static void AssertTheStaticWebAssetsArePacked(AbsolutePath package)
    {
        using ZipArchive archive = ZipFile.OpenRead(package);

        ZipArchiveEntry props = archive.GetEntry("build/Microsoft.AspNetCore.StaticWebAssets.props");
        if (props is null)
        {
            return;
        }

        string text;
        using (StreamReader reader = new(props.Open()))
        {
            text = reader.ReadToEnd();
        }

        string[] carried = [.. archive.Entries.Select(x => x.FullName).Where(x => x.StartsWith("staticwebassets/", StringComparison.Ordinal)).Order(StringComparer.Ordinal)];
        // Each asset's path, as GetFullPath('$(MSBuildThisFileDirectory)..\staticwebassets\<file>') spells it;
        // the ContentRoot that ends in the directory itself is not one.
        string[] named = [.. Regex.Matches(text, @"\.\.[\\/]staticwebassets[\\/](?<file>[^'<>]+)'", RegexOptions.None, TimeSpan.FromSeconds(5))
            .Select(x => "staticwebassets/" + x.Groups["file"].Value.Replace('\\', '/'))
            .Distinct(StringComparer.Ordinal)];
        string[] missing = [.. named.Where(x => !carried.Contains(x, StringComparer.Ordinal))];

        Assert.True(missing.Length == 0,
            $"{package.Name} names static web assets it does not carry, so no application can build against it:"
            + Environment.NewLine + string.Join(Environment.NewLine, missing)
            + Environment.NewLine + "It carries:" + Environment.NewLine + string.Join(Environment.NewLine, carried));

        Log.Information("{Package} carries every one of its {Count} static web assets", package.Name, named.Length);
    }

    /// <summary>
    /// Publishes the consumer's copy of a canary for the runner's own RID, holds what the publish reported
    /// to <see cref="AssertOnlyTheTrimWitness" />, and starts it. A non-zero exit fails the leg.
    /// </summary>
    void PublishAndRunNet11Canary(AbsolutePath root, Dictionary<string, string> environment, string project, string kind, bool aot)
    {
        AbsolutePath output = root / "publish" / project / (aot ? "aot" : "trimmed");

        IReadOnlyCollection<Output> publish = DotNetPublish(s => s
            .SetProject(root / project / $"{project}.csproj")
            .SetConfiguration(Configuration.Release)
            .SetRuntime(RuntimeInformation.RuntimeIdentifier)
            .SetOutput(output)
            .SetProperty("QuartzVersion", FullVersion)
            .SetProperty("QuartzRepositoryRoot", RootDirectory)
            .SetProperty("MicrosoftDataSqliteVersion", PinnedPackageVersion("Microsoft.Data.SQLite"))
            .When(aot, _ => _.SetProperty("PublishAot", true))
            .AddProcessAdditionalArguments(DisableBuildServers)
            .SetProcessWorkingDirectory(root)
            .SetProcessEnvironmentVariables(environment));

        AssertOnlyTheTrimWitness(publish, $"the {kind} net{Net11}.0 {project} publish");

        AbsolutePath canary = output / (IsRunningOnWindows ? $"{project}.exe" : project);
        Log.Information("Running the {Kind} net{Runtime}.0 {Project}: {Canary}", kind, Net11, project, canary);

        ProcessTasks.StartProcess(canary, workingDirectory: output, logOutput: true).AssertZeroExitCode();
    }

    /// <summary>
    /// What the dashboard's page at <paramref name="dashboardPath" /> lists and its server does not answer:
    /// the framework script and every module of its <c>Blazor-Web-Initializers</c>, each resolved against
    /// the page's own <c>&lt;base href&gt;</c> as a browser resolves it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A module that fails to import stops <c>Blazor.start</c>, and the page renders and never becomes
    /// interactive, which no log line shows. With a custom path the base is the dashboard's, so a module the
    /// framework serves only at the root is what #4224 was: the .NET 11 SDK's hot-reload initializer,
    /// <c>_framework/Microsoft.NET.Sdk.BlazorWeb.DotNetWatch.&lt;hash&gt;.lib.module.js</c>, which a Debug
    /// build lists and a Release build does not.
    /// </para>
    /// <para>
    /// That initializer has to be among the modules: a page without it is not the shape the check is for,
    /// and passing it would say nothing. The list is a comment ASP.NET Core writes into the page, JSON up to
    /// .NET 10 and base64-encoded JSON from 11, and either is read.
    /// </para>
    /// </remarks>
    static List<string> UnansweredInitializers(string baseAddress, string dashboardPath)
    {
        using HttpClient client = new() { BaseAddress = new Uri(baseAddress), Timeout = TimeSpan.FromSeconds(30) };

        string page;
        try
        {
            using HttpResponseMessage response = client.GetAsync(dashboardPath).GetAwaiter().GetResult();
            page = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (response.StatusCode != HttpStatusCode.OK || page.Length == 0)
            {
                return [$"the dashboard's page {dashboardPath} answered {(int) response.StatusCode} with {page.Length} characters"];
            }
        }
        catch (Exception e) when (e is HttpRequestException or System.Threading.Tasks.TaskCanceledException)
        {
            return [$"the dashboard's page {dashboardPath} did not answer: {e.Message}"];
        }

        Match baseHref = Regex.Match(page, "<base href=\"(?<href>[^\"]*)\"", RegexOptions.None, TimeSpan.FromSeconds(5));
        Match script = Regex.Match(page, "<script src=\"(?<src>[^\"]*blazor\\.web\\.js)\"", RegexOptions.None, TimeSpan.FromSeconds(5));
        Match initializers = Regex.Match(page, "<!--Blazor-Web-Initializers:(?<list>.*?)-->", RegexOptions.Singleline, TimeSpan.FromSeconds(5));

        if (!baseHref.Success || !script.Success || !initializers.Success)
        {
            return [$"the dashboard's page {dashboardPath} has no <base href>, blazor.web.js script or Blazor-Web-Initializers comment to check"];
        }

        string[] modules;
        try
        {
            string list = initializers.Groups["list"].Value.Trim();
            string json = list.StartsWith('[') ? list : Encoding.UTF8.GetString(Convert.FromBase64String(list));
            modules = JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            return [$"the dashboard's page {dashboardPath} lists its Blazor-Web-Initializers in a form this check cannot read ({e.Message})"];
        }

        Log.Information("The dashboard's page lists Blazor-Web-Initializers {Modules} under <base href=\"{Base}\">", modules, baseHref.Groups["href"].Value);

        if (!modules.Any(x => x.Contains("DotNetWatch", StringComparison.Ordinal)))
        {
            return [$"the dashboard's page {dashboardPath} lists no hot-reload initializer among its Blazor-Web-Initializers ({string.Join(", ", modules)}), "
                + "which a Debug build with the .NET 11 SDK has, so whether it loads under the dashboard's path (#4224) was not checked"];
        }

        Uri pageBase = new(new Uri(baseAddress), baseHref.Groups["href"].Value);
        string[] paths = [.. modules.Prepend(script.Groups["src"].Value).Select(x => new Uri(pageBase, x).AbsolutePath)];

        return Unanswered(baseAddress, paths);
    }
}
