using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Fallout.Common;
using Fallout.Common.CI.GitHubActions;
using Fallout.Common.IO;
using Fallout.Common.Tooling;
using Fallout.Components;

using Semver;

using Serilog;

using static Fallout.Common.Tools.DotNet.DotNetTasks;

/// <summary>
/// The dashboard host's container image: built and exercised on the Linux unit leg, published from a
/// release tag and nowhere else.
/// </summary>
/// <remarks>
/// <para>
/// One project, <c>src/Quartz.Dashboard.Host</c>, is both the <c>quartz-dashboard</c> tool and the image
/// <c>ghcr.io/quartznet/quartz-dashboard</c>. The SDK builds the image itself (<c>-t:PublishContainer</c>):
/// no Dockerfile, the base image inferred from the project and its <c>ContainerFamily</c>.
/// </para>
/// <para>
/// <see cref="PublishContainer" /> runs from <c>.github/workflows/publish-container.yml</c>, which is
/// hand-written: the build provenance attestation it ends with needs <c>attestations: write</c>, which
/// Fallout's generator has no permission for, and the digest this target writes as a step output.
/// </para>
/// </remarks>
public partial class Build
{
    const string ContainerRegistry = "ghcr.io";
    const string ContainerRepository = "quartznet/quartz-dashboard";

    /// <summary>
    /// The architectures a release publishes as one index: each one's runtime identifier, and the platform
    /// the index names it by.
    /// </summary>
    static readonly (string RuntimeIdentifier, string Platform)[] ContainerArchitectures =
    [
        ("linux-x64", "linux/amd64"),
        ("linux-arm64", "linux/arm64"),
    ];

    static string[] ContainerRuntimeIdentifiers => [.. ContainerArchitectures.Select(x => x.RuntimeIdentifier)];

    AbsolutePath DashboardHostProject => SourceDirectory / "Quartz.Dashboard.Host" / "Quartz.Dashboard.Host.csproj";

    static readonly TimeSpan ContainerStartTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Builds the image into the local daemon, starts it from environment variables alone, and checks what
    /// a deployment relies on: the health probe, the sign-in, the dashboard's pages and assets, a non-root
    /// user, and a refusal to start with no authentication mode. Then publishes every architecture a
    /// release does to a registry of its own, and checks the index lists each one. The image and the
    /// registry are removed afterwards.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On the <c>pr-tests-unit</c> workflow it runs on the Ubuntu leg, the only runner image with a Linux
    /// container engine; the Windows and macOS legs skip it, and say so. Locally it runs wherever
    /// <c>docker info</c> reports a Linux engine, and is skipped with a warning naming why where none
    /// answers.
    /// </para>
    /// <para>
    /// The image it runs is the runner's own architecture: running a foreign one would be a test of QEMU.
    /// The publish of every architecture goes through <see cref="PublishDashboardImage" />, the command
    /// <see cref="PublishContainer" /> runs, into a <c>registry:2</c> started for it. The 4.8.0 image failed
    /// in that command alone, which a build of one architecture never runs: each library was built once per
    /// architecture, and the compiler could not load the analyzer built for linux-arm64.
    /// </para>
    /// </remarks>
    Target ContainerSmoke => _ => _
        .DependsOn<ICompile>()
        .After(UnitTest)
        .Before<IPack>()
        .OnlyWhenDynamic(() => CanRunLinuxContainers())
        .Executes(() =>
        {
            string configuration = ((ICompile) this).Configuration;
            string rid = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
            string suffix = Guid.NewGuid().ToString("N")[..12];
            string image = $"quartz-dashboard-smoke:{suffix}";
            string container = $"quartz-dashboard-smoke-{suffix}";
            string refusing = $"quartz-dashboard-smoke-refusal-{suffix}";
            string registry = $"quartz-dashboard-smoke-registry-{suffix}";
            string token = $"smoke-token-{Guid.NewGuid():N}";

            try
            {
                PublishDashboardImage(configuration, [rid], registry: null, "quartz-dashboard-smoke", [suffix]);

                AssertNonRootUser(image);
                AssertRefusesToStartWithoutAMode(image, refusing);

                Run("docker",
                    $"run -d --name {container} -p 127.0.0.1::8080 "
                    + "-e Quartz__Dashboard__Authentication__Mode=Token "
                    + $"-e Quartz__Dashboard__Authentication__Tokens__Administrator={token} {image}",
                    secret: token);

                AssertServes(container, new Uri($"http://127.0.0.1:{PublishedLoopbackPort(container, 8080)}"), token);

                Stopwatch publishing = Stopwatch.StartNew();
                string address = StartRegistry(registry);
                PublishDashboardImage(configuration, ContainerRuntimeIdentifiers, address, "quartz-dashboard-smoke", [suffix]);
                AssertIndexListsEveryArchitecture($"{address}/quartz-dashboard-smoke:{suffix}");
                Log.Information("Every architecture published as one index in {Elapsed:F0}s", publishing.Elapsed.TotalSeconds);
            }
            finally
            {
                Docker($"rm -f {container}");
                Docker($"rm -f {refusing}");
                // -v: registry:2 declares /var/lib/registry a volume, so without it every run would leave the
                // images it pushed behind in an anonymous volume.
                Docker($"rm -f -v {registry}");
                Docker($"rmi -f {image}");
            }
        });

    /// <summary>
    /// Publishes the image for <see cref="ContainerArchitectures" /> as one index, tagged for the release,
    /// checks the index lists each one, and writes its digest as the step output <c>digest</c> for the
    /// attestation that follows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A release tag is the only thing that publishes: a stable version is tagged <c>X.Y.Z</c>, and
    /// <c>X.Y</c>, <c>X</c> and <c>latest</c> when it is the newest release of its line; a prerelease, its
    /// exact version only (#3775). "The newest of its line" is read from the repository's tags, so a patch
    /// of an older line never moves <c>latest</c> back.
    /// </para>
    /// <para>
    /// The registry credentials are the workflow's: it logs Docker in to ghcr.io with <c>GITHUB_TOKEN</c>,
    /// and the SDK reads the same credential store.
    /// </para>
    /// </remarks>
    Target PublishContainer => _ => _
        .Executes(() =>
        {
            Assert.True(IsTaggedBuild && TagSemVersion is not null,
                "The dashboard image is published from a release tag and nothing else: a v-tag on GitHub Actions. "
                + "Run ContainerSmoke to build one locally.");

            SemVersion version = TagSemVersion;
            string[] tags = ContainerTags(version, ReleasedVersions());
            Log.Information("Publishing {Repository} as {Tags}", $"{ContainerRegistry}/{ContainerRepository}", string.Join(", ", tags));

            PublishDashboardImage(Configuration.Release, ContainerRuntimeIdentifiers, ContainerRegistry, ContainerRepository, tags);

            string reference = $"{ContainerRegistry}/{ContainerRepository}:{FullVersion}";
            string digest = AssertIndexListsEveryArchitecture(reference);
            Log.Information("Published {Reference}@{Digest}", reference, digest);

            if (Environment.GetEnvironmentVariable("GITHUB_OUTPUT") is { Length: > 0 } output)
            {
                File.AppendAllText(output, $"digest={digest}{Environment.NewLine}");
            }
        });

    /// <summary>
    /// Publishes the dashboard host's image with <c>dotnet publish -t:PublishContainer</c>: the one command
    /// <see cref="PublishContainer" /> and both publishes of <see cref="ContainerSmoke" /> run.
    /// </summary>
    /// <param name="configuration">The build configuration.</param>
    /// <param name="runtimeIdentifiers">One architecture, or several to publish as one index.</param>
    /// <param name="registry">
    /// The registry to push to, or <see langword="null" /> to load the image into the local Docker daemon.
    /// </param>
    /// <param name="repository">The image's repository.</param>
    /// <param name="tags">The tags to publish it under.</param>
    void PublishDashboardImage(string configuration, IReadOnlyList<string> runtimeIdentifiers, string registry, string repository, IReadOnlyCollection<string> tags)
    {
        // Several architectures are named by ContainerRuntimeIdentifiers alone, which the project derives its
        // RuntimeIdentifiers from. Passed here, RuntimeIdentifiers would be a global property, and every
        // project in the graph would be built once per architecture: the 4.8.0 image failed because the
        // compiler, an x64 process, could not load the analyzer built for linux-arm64.
        string architectures = runtimeIdentifiers.Count == 1
            ? $"-p:ContainerRuntimeIdentifier={runtimeIdentifiers[0]} -p:RuntimeIdentifier={runtimeIdentifiers[0]}"
            : $"-p:ContainerRuntimeIdentifiers=\\\"{string.Join(';', runtimeIdentifiers)}\\\"";

        // Into Docker by name when there is no registry, because whatever follows asks Docker for the image.
        // From the .NET 11 SDK on, a publish that names no LocalRegistry loads the image into wslc on Windows,
        // or Apple's container CLI on macOS, whenever one is installed and running, and Docker then answers
        // "No such image".
        string destination = registry is null ? "-p:LocalRegistry=Docker" : $"-p:ContainerRegistry={registry}";

        Run(DotNetPath,
            $"publish \"{DashboardHostProject}\" -c {configuration} -t:PublishContainer {architectures} {destination} "
            + $"-p:ContainerRepository={repository} -p:ContainerImageTags=\\\"{string.Join(';', tags)}\\\" "
            + $"-p:VersionPrefix={VersionPrefix} -p:VersionSuffix={VersionSuffix} "
            + $"-p:AssemblyVersion={VersionPrefix} -p:FileVersion={VersionPrefix} "
            + $"-p:ContinuousIntegrationBuild={IsContinuousIntegrationBuild.ToString().ToLowerInvariant()}");
    }

    /// <summary>
    /// Reads an image index from its registry, and fails unless it lists every architecture in
    /// <see cref="ContainerArchitectures" />. Returns the index's digest.
    /// </summary>
    static string AssertIndexListsEveryArchitecture(string reference)
    {
        IProcess inspect = ProcessTasks.StartProcess("docker", $"buildx imagetools inspect {reference} --format \"{{{{json .Manifest}}}}\"", logOutput: false);
        inspect.AssertZeroExitCode();

        using JsonDocument index = JsonDocument.Parse(string.Join('\n', inspect.Output.Where(x => x.Type == OutputType.Std).Select(x => x.Text)));
        string digest = index.RootElement.TryGetProperty("digest", out JsonElement value) ? value.GetString() : null;
        List<string> platforms = index.RootElement.TryGetProperty("manifests", out JsonElement manifests)
            ? [.. manifests.EnumerateArray()
                .Where(x => x.TryGetProperty("platform", out _))
                .Select(x => x.GetProperty("platform"))
                .Select(x => $"{x.GetProperty("os").GetString()}/{x.GetProperty("architecture").GetString()}")]
            : [];

        string[] missing = [.. ContainerArchitectures.Select(x => x.Platform).Except(platforms, StringComparer.Ordinal)];
        Assert.True(missing.Length == 0,
            $"{reference} lists [{string.Join(", ", platforms)}], so {string.Join(" and ", missing)} is missing: "
            + $"a release publishes {string.Join(" and ", ContainerArchitectures.Select(x => x.Platform))} as one index");
        Assert.True(digest is not null && Regex.IsMatch(digest, "^sha256:[0-9a-f]{64}$", RegexOptions.None, TimeSpan.FromSeconds(5)),
            $"{reference} was pushed, but 'docker buildx imagetools inspect' reported no digest for it, only '{digest}'");

        Log.Information("{Reference} is one index of {Platforms}", reference, string.Join(", ", platforms));
        return digest;
    }

    /// <summary>
    /// Starts a throwaway <c>registry:2</c> on a loopback port, waits until it answers, and returns its
    /// address.
    /// </summary>
    static string StartRegistry(string container)
    {
        // --quiet: a pull writes its progress to standard error, which Fallout logs as errors and GitHub
        // Actions turns into an error annotation for each line, on a run that passed.
        Run("docker", $"run -d --quiet --name {container} -p 127.0.0.1::5000 registry:2");
        int port = PublishedLoopbackPort(container, 5000);

        using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(5) };
        Stopwatch waiting = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                using HttpResponseMessage response = client.GetAsync($"http://127.0.0.1:{port}/v2/").GetAwaiter().GetResult();
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    break;
                }
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                // Not listening yet.
            }

            if (waiting.Elapsed > ContainerStartTimeout)
            {
                Assert.Fail($"{container} did not answer /v2/ within {ContainerStartTimeout.TotalSeconds:F0}s.{Environment.NewLine}{ContainerLogs(container)}");
            }

            System.Threading.Thread.Sleep(250);
        }

        // localhost rather than 127.0.0.1: the SDK pushes to it over plain HTTP, as Docker does.
        return $"localhost:{port}";
    }

    /// <summary>
    /// The loopback port Docker published a container's port on. The container is started with
    /// <c>-p 127.0.0.1::port</c>, so Docker picks a free port as it binds it, and nothing can take the port
    /// between the choice and the bind.
    /// </summary>
    static int PublishedLoopbackPort(string container, int containerPort)
    {
        const string loopback = "127.0.0.1:";
        string binding = Run("docker", $"port {container} {containerPort}/tcp")
            .Select(x => x.Trim())
            .FirstOrDefault(x => x.StartsWith(loopback, StringComparison.Ordinal));

        int port = 0;
        Assert.True(binding is not null && int.TryParse(binding[loopback.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out port),
            $"'docker port {container} {containerPort}/tcp' reported no loopback binding, only '{binding}'");
        return port;
    }

    /// <summary>
    /// The tags one release is published under.
    /// </summary>
    internal static string[] ContainerTags(SemVersion version, IReadOnlyCollection<SemVersion> released)
    {
        string exact = version.ToString();
        if (version.IsPrerelease)
        {
            return [exact];
        }

        List<SemVersion> stable = released.Where(x => !x.IsPrerelease).Append(version).ToList();
        bool newestOfMinor = stable.Where(x => x.Major == version.Major && x.Minor == version.Minor).All(x => x.ComparePrecedenceTo(version) <= 0);
        bool newestOfMajor = stable.Where(x => x.Major == version.Major).All(x => x.ComparePrecedenceTo(version) <= 0);
        bool newest = stable.All(x => x.ComparePrecedenceTo(version) <= 0);

        List<string> tags = [exact];
        if (newestOfMinor)
        {
            tags.Add($"{version.Major}.{version.Minor}");
        }

        if (newestOfMajor)
        {
            tags.Add($"{version.Major}");
        }

        if (newest)
        {
            tags.Add("latest");
        }

        return [.. tags];
    }

    /// <summary>
    /// Every release this repository has tagged. The workflow checks out with every tag for this.
    /// </summary>
    List<SemVersion> ReleasedVersions()
    {
        return Run("git", "tag --list v*")
            .Select(x => x.Trim())
            .Where(x => x.Length > 1)
            .Select(x => SemVersion.TryParse(x[1..], SemVersionStyles.Strict, out SemVersion parsed) ? parsed : null)
            .Where(x => x is not null)
            .ToList();
    }

    /// <summary>
    /// Whether this machine can run a Linux container: on GitHub Actions, a Linux runner; elsewhere, a
    /// Docker engine that says it is Linux.
    /// </summary>
    bool CanRunLinuxContainers()
    {
        if (Host is GitHubActions)
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        }

        try
        {
            IProcess info = ProcessTasks.StartProcess("docker", "info --format {{.OSType}}", logOutput: false, logInvocation: false);
            info.WaitForExit();
            if (info.ExitCode == 0 && info.Output.Any(x => x.Text.Trim() == "linux"))
            {
                return true;
            }
        }
        catch (Exception e)
        {
            // No docker on the path, which Fallout reports by throwing: said below like an engine that is
            // not Linux.
            Log.Debug(e, "docker info could not be run");
        }

        Log.Warning("ContainerSmoke is skipped: no Docker engine running Linux containers answers 'docker info' here");
        return false;
    }

    static void AssertNonRootUser(string image)
    {
        string user = Run("docker", $"image inspect --format {{{{.Config.User}}}} {image}").FirstOrDefault()?.Trim();
        Assert.True(!string.IsNullOrEmpty(user) && user != "root" && user != "0",
            $"{image} runs as '{user}': the chiseled base image's non-root app user is what it should run as");
        Log.Information("{Image} runs as user {User}", image, user);
    }

    /// <summary>
    /// A container given no authentication mode exits non-zero, having logged event 9402 saying why.
    /// </summary>
    static void AssertRefusesToStartWithoutAMode(string image, string container)
    {
        Run("docker", $"create --name {container} {image}");

        IProcess started = ProcessTasks.StartProcess("docker", $"start --attach {container}", timeout: (int) ContainerStartTimeout.TotalMilliseconds, logOutput: true);
        Assert.True(started.WaitForExit(),
            $"{image} with no Quartz:Dashboard:Authentication:Mode was still running after {ContainerStartTimeout.TotalSeconds:F0}s; it must refuse to start");

        string logs = string.Join(Environment.NewLine, started.Output.Select(x => x.Text));
        // Exactly 1, the refusal's own exit code: a crash (139) or a kill is not a refusal.
        Assert.True(started.ExitCode == 1, $"{image} with no authentication mode exited {started.ExitCode}; it must refuse to start with exit code 1.{Environment.NewLine}{logs}");
        Assert.True(logs.Contains("[9402]", StringComparison.Ordinal) && logs.Contains("Authentication:Mode is required", StringComparison.Ordinal),
            $"{image} refused to start without saying why as event 9402:{Environment.NewLine}{logs}");

        Log.Information("{Image} refuses to start without an authentication mode, exit code {ExitCode}", image, started.ExitCode);
    }

    /// <summary>
    /// Waits for the probe, then signs in with the administrator's token and reads the dashboard, its
    /// framework script and its stylesheet.
    /// </summary>
    static void AssertServes(string container, Uri address, string token)
    {
        using HttpClientHandler handler = new() { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
        using HttpClient client = new(handler) { BaseAddress = address, Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html");

        Stopwatch waiting = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                using HttpResponseMessage health = client.GetAsync("/healthz").GetAwaiter().GetResult();
                if (health.StatusCode == HttpStatusCode.OK)
                {
                    break;
                }
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }

            if (waiting.Elapsed > ContainerStartTimeout)
            {
                Assert.Fail($"{container} did not answer /healthz within {ContainerStartTimeout.TotalSeconds:F0}s.{Environment.NewLine}{ContainerLogs(container)}");
            }

            System.Threading.Thread.Sleep(500);
        }

        Log.Information("{Container} answers /healthz after {Elapsed:F1}s", container, waiting.Elapsed.TotalSeconds);

        using HttpResponseMessage anonymous = client.GetAsync("/quartz").GetAwaiter().GetResult();
        Assert.True(anonymous.StatusCode == HttpStatusCode.Redirect && anonymous.Headers.Location?.AbsolutePath == "/login",
            $"an anonymous visitor to /quartz got {(int) anonymous.StatusCode} {anonymous.Headers.Location} rather than the sign-in page");

        string form = client.GetStringAsync("/login").GetAwaiter().GetResult();
        string antiforgery = WebUtility.HtmlDecode(Regex.Match(form, "name=\"__RequestVerificationToken\" value=\"(?<value>[^\"]*)\"", RegexOptions.None, TimeSpan.FromSeconds(5)).Groups["value"].Value);
        Assert.NotNullOrWhiteSpace(antiforgery, "the sign-in form carries no antiforgery token");

        using HttpResponseMessage signIn = client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = token,
            ["__RequestVerificationToken"] = antiforgery,
        })).GetAwaiter().GetResult();
        Assert.True(signIn.StatusCode == HttpStatusCode.Redirect, $"signing in answered {(int) signIn.StatusCode} rather than a redirect to the dashboard");

        foreach (string path in new[] { "/quartz", "/_framework/blazor.web.js", "/_content/Quartz.Dashboard/css/quartz-dashboard.css" })
        {
            using HttpResponseMessage response = client.GetAsync(path).GetAwaiter().GetResult();
            byte[] body = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            Assert.True(response.StatusCode == HttpStatusCode.OK && body.Length > 0,
                $"{path} answered {(int) response.StatusCode} with {body.Length} bytes to a signed-in administrator.{Environment.NewLine}{ContainerLogs(container)}");
            Log.Information("{Path} answered 200 with {Length} bytes", path, body.Length);
        }

        string logs = ContainerLogs(container);
        Assert.True(!logs.Contains(token, StringComparison.Ordinal), "the container's log carries the sign-in token");
        Assert.True(logs.Contains("[9400]", StringComparison.Ordinal), $"the container never logged event 9400, the host's start:{Environment.NewLine}{logs}");
    }

    static string ContainerLogs(string container)
    {
        IProcess logs = ProcessTasks.StartProcess("docker", $"logs {container}", logOutput: false);
        logs.WaitForExit();
        return "The container's log:" + Environment.NewLine + string.Join(Environment.NewLine, logs.Output.Select(x => x.Text).TakeLast(80));
    }

    /// <summary>
    /// Runs a command to completion and returns what it printed; a non-zero exit fails the target.
    /// </summary>
    static List<string> Run(string tool, string arguments, string secret = null)
    {
        IProcess process = ProcessTasks.StartProcess(
            tool,
            arguments,
            logOutput: true,
            logInvocation: secret is null);

        if (secret is not null)
        {
            Log.Information("> {Tool} {Arguments}", tool, arguments.Replace(secret, "[secret]", StringComparison.Ordinal));
        }

        process.AssertZeroExitCode();
        return process.Output.Select(x => x.Text).ToList();
    }

    /// <summary>
    /// A cleanup step: runs, and never fails the target, because what it removes may never have been made.
    /// </summary>
    static void Docker(string arguments)
    {
        IProcess process = ProcessTasks.StartProcess("docker", arguments, logOutput: false);
        process.WaitForExit();
    }
}
