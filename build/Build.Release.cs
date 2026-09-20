using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Fallout.Common;
using Fallout.Common.CI.GitHubActions;
using Fallout.Common.Git;
using Fallout.Common.IO;
using Fallout.Common.Tooling;
using Fallout.Common.Utilities.Net;

using Serilog;

public partial class Build
{
    const string GitHubApiUrl = "https://api.github.com";

    // The REST API version GitHub asks every caller to pin, so a future default cannot move under us.
    const string GitHubApiVersion = "2022-11-28";

    const int ReleasePageSize = 100;

    // Far more releases than this repository has; the walk below stops at the first short page anyway,
    // and the bound is only there so a malformed answer cannot loop forever.
    const int ReleasePageLimit = 10;

    [Parameter("GitHub token the release is drafted with — defaults to the publish workflow's GITHUB_TOKEN")] [Secret]
    readonly string GitHubToken;

    [Parameter("Repository to draft the release in, as owner/name. Set by hand only, to rehearse DraftRelease against a scratch repository")]
    readonly string GitHubRepository;

    string ReleaseToken => GitHubToken ?? GitHubActions.Instance?.Token;

    string ReleaseRepository => GitHubRepository ?? GitHubActions.Instance?.Repository ?? GitRepository?.Identifier;

    // FullVersion is parsed back out of the tag on a tagged build, so this reconstructs the tag name
    // without going near the property that produced it — which is what lets this file land on 3.x
    // unchanged, where the tag is read a different way.
    string ReleaseTagName => $"v{FullVersion}";

    // PackZip writes the archive to artifacts/packages here and to artifacts on 3.x, so both shapes are
    // spelled out rather than resting on what '**' does with zero directories. Naming the version keeps
    // an earlier build's archive out of the release.
    IEnumerable<AbsolutePath> ReleaseAssetFiles =>
        Directory.Exists(ArtifactsDirectory)
            ? ArtifactsDirectory
                .GlobFiles($"Quartz.NET-{FullVersion}.zip", $"**/Quartz.NET-{FullVersion}.zip")
                .Distinct()
            : [];

    /// <summary>
    /// Opens the GitHub release for the tag as a draft, with the notes GitHub generates from
    /// <c>.github/release.yml</c>, and attaches the archive <see cref="PackZip"/> built. Nothing
    /// becomes public: a human edits the notes and presses Publish.
    /// </summary>
    /// <remarks>
    /// Triggered by <see cref="Publish"/> rather than depending on it — the idiom <see cref="PackZip"/>
    /// already uses — so that running this target by hand cannot drag a package push along behind it.
    /// Ordered after <see cref="PackZip"/> explicitly, because a trigger only says "after Pack", not
    /// "before the next invoked target": the v4.1.1 tag build ran PackZip last of all, after Publish,
    /// and the planner puts an invoked target ahead of a merely triggered one, so without this the
    /// archive would not exist yet when this runs. After, not DependsOn, so that running this by hand
    /// to recover a release does not rebuild the archive it is about to attach. Every step is written
    /// to be re-runnable, because re-running a failed publish workflow is the recovery path for a
    /// release that went wrong halfway.
    /// </remarks>
    Target DraftRelease => _ => _
        .TriggeredBy(Publish)
        .After(PackZip)
        .OnlyWhenDynamic(() => IsTaggedBuild)
        .Executes(async () =>
        {
            var token = Assert.NotNullOrWhiteSpace(ReleaseToken,
                "A GitHub token is required to draft a release — the publish workflow declares 'contents: write' and hands GITHUB_TOKEN to the build step");
            var repository = Assert.NotNullOrWhiteSpace(ReleaseRepository,
                "Could not work out which repository to draft the release in — pass --github-repository owner/name");

            var assets = ReleaseAssetFiles.ToList();
            Assert.NotEmpty(assets,
                $"No Quartz.NET-{FullVersion}.zip under {ArtifactsDirectory} — DraftRelease is ordered after PackZip, which Pack triggers; run Pack first");

            using var client = CreateGitHubClient(token);

            var release = await GetReleaseForTag(client, repository, ReleaseTagName);
            if (release is null)
            {
                release = await CreateDraftRelease(client, repository);
            }
            else
            {
                await FillEmptyDraftNotes(client, repository, release);
            }

            foreach (var asset in assets)
            {
                await AttachAsset(client, repository, release, asset);
            }

            Log.Information("Release {Tag:l} is ready to edit and publish: {Url:l}",
                ReleaseTagName, release["html_url"]?.GetValue<string>());
        });

    static HttpClient CreateGitHubClient(string token)
    {
        HttpClient client = new()
        {
            BaseAddress = new Uri(GitHubApiUrl),
            // The default is 100 seconds, and the archive is tens of megabytes.
            Timeout = TimeSpan.FromMinutes(10)
        };

        // GitHub refuses a request with no User-Agent and HttpClient sends none by default — the same
        // trap GetTrustedPublishingApiKey documents for nuget.org.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("quartznet-build/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", GitHubApiVersion);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// The release carrying <paramref name="tag"/>, or null when the repository has none yet. It takes
    /// two lookups: <c>releases/tags/{tag}</c> answers with published releases only — GitHub documents
    /// it as getting a <em>published</em> release, and it 404s on a draft — so a miss is followed by a
    /// walk of the release list, which is the only place a draft shows up.
    /// </summary>
    static async Task<JsonObject> GetReleaseForTag(HttpClient client, string repository, string tag)
    {
        var published = await client
            .CreateRequest(HttpMethod.Get, $"/repos/{repository}/releases/tags/{Uri.EscapeDataString(tag)}")
            .GetResponseAsync();

        if (published.Response.IsSuccessStatusCode)
        {
            return await published.GetBodyAsJsonObject();
        }

        // Anything but "no published release with that tag" is a real failure and should say so.
        if (published.Response.StatusCode != HttpStatusCode.NotFound)
        {
            published.AssertSuccessfulStatusCode();
        }

        for (var page = 1; page <= ReleasePageLimit; page++)
        {
            var listing = (await client
                    .CreateRequest(HttpMethod.Get, $"/repos/{repository}/releases?per_page={ReleasePageSize}&page={page}")
                    .GetResponseAsync())
                .AssertSuccessfulStatusCode();

            var releases = JsonNode.Parse(await listing.GetBodyAsync()).AsArray();

            var match = releases
                .OfType<JsonObject>()
                .FirstOrDefault(x => x["tag_name"]?.GetValue<string>() == tag);

            if (match is not null)
            {
                return match;
            }

            if (releases.Count < ReleasePageSize)
            {
                return null;
            }
        }

        return null;
    }

    async Task<JsonObject> CreateDraftRelease(HttpClient client, string repository)
    {
        var prerelease = !string.IsNullOrWhiteSpace(VersionSuffix);

        var release = await (await client
                .CreateRequest(HttpMethod.Post, $"/repos/{repository}/releases")
                .WithJsonContent(new
                {
                    tag_name = ReleaseTagName,
                    // The releases here are titled with the tag, and nothing else.
                    name = ReleaseTagName,
                    draft = true,
                    prerelease,
                    // What fills the body from the categories in .github/release.yml — the same thing
                    // the "Generate release notes" button does, so the draft starts where a hand-made
                    // one would have.
                    generate_release_notes = true,
                    // 'legacy' settles latest by semantic version and creation date, so a 3.x
                    // maintenance release cannot take "Latest" away from 4.x. The publish dialog's own
                    // checkbox is what finally decides it; this is the sane default sitting behind it.
                    make_latest = "legacy"
                })
                .GetResponseAsync())
            .AssertResponse(x => x.IsSuccessStatusCode
                ? null
                : $"Could not draft the release for {ReleaseTagName} ({(int) x.StatusCode}): {x.Content.ReadAsStringAsync().GetAwaiter().GetResult()}")
            .GetBodyAsJsonObject();

        Log.Information("Drafted {Tag:l}{Prerelease:l} with generated release notes", ReleaseTagName, prerelease ? " as a prerelease" : "");
        return release;
    }

    /// <summary>
    /// Seeds a draft that has no notes yet. A draft somebody has already written into is left exactly
    /// as it is — notes accumulating in a draft ahead of the tag is how this repository works, and the
    /// tag build must not undo that — and a release that is already published is never rewritten.
    /// </summary>
    async Task FillEmptyDraftNotes(HttpClient client, string repository, JsonObject release)
    {
        if (release["draft"]?.GetValue<bool>() != true)
        {
            Log.Information("{Tag:l} is already published — attaching the archive and leaving its notes alone", ReleaseTagName);
            return;
        }

        if (!string.IsNullOrWhiteSpace(release["body"]?.GetValue<string>()))
        {
            Log.Information("{Tag:l} already has a draft with notes written — attaching the archive and leaving them alone", ReleaseTagName);
            return;
        }

        var notes = await (await client
                .CreateRequest(HttpMethod.Post, $"/repos/{repository}/releases/generate-notes")
                .WithJsonContent(new { tag_name = ReleaseTagName })
                .GetResponseAsync())
            .AssertResponse(x => x.IsSuccessStatusCode
                ? null
                : $"Could not generate release notes for {ReleaseTagName} ({(int) x.StatusCode}): {x.Content.ReadAsStringAsync().GetAwaiter().GetResult()}")
            .GetBodyAsJsonObject();

        (await client
                .CreateRequest(HttpMethod.Patch, $"/repos/{repository}/releases/{release["id"].GetValue<long>()}")
                // tag_name goes back with every patch of a draft: a body-only patch clears it.
                .WithJsonContent(new { tag_name = ReleaseTagName, body = notes["body"].GetValue<string>() })
                .GetResponseAsync())
            .AssertResponse(x => x.IsSuccessStatusCode
                ? null
                : $"Could not write the generated notes into the draft for {ReleaseTagName} ({(int) x.StatusCode}): {x.Content.ReadAsStringAsync().GetAwaiter().GetResult()}");

        Log.Information("Filled the empty draft for {Tag:l} with generated release notes", ReleaseTagName);
    }

    /// <summary>
    /// Attaches one file, replacing an asset of the same name the release already carries — GitHub
    /// answers a duplicate name with 422, and a re-run has to be able to get past it.
    /// </summary>
    async Task AttachAsset(HttpClient client, string repository, JsonObject release, AbsolutePath file)
    {
        var existing = release["assets"]?.AsArray()
            .OfType<JsonObject>()
            .FirstOrDefault(x => x["name"]?.GetValue<string>() == file.Name);

        if (existing is not null)
        {
            (await client
                    .CreateRequest(HttpMethod.Delete, $"/repos/{repository}/releases/assets/{existing["id"].GetValue<long>()}")
                    .GetResponseAsync())
                .AssertSuccessfulStatusCode();

            Log.Information("Removed the {Name:l} already attached to {Tag:l}", file.Name, ReleaseTagName);
        }

        // upload_url arrives as an RFC 6570 template on a different host, pointing at
        // 'https://uploads.github.com/repos/{owner}/{repo}/releases/{id}/assets{?name,label}'.
        var uploadUrl = release["upload_url"].GetValue<string>();
        var template = uploadUrl.IndexOf('{');
        if (template >= 0)
        {
            uploadUrl = uploadUrl[..template];
        }

        await using var content = File.OpenRead(file);

        var request = client.CreateRequest(HttpMethod.Post, $"{uploadUrl}?name={Uri.EscapeDataString(file.Name)}");
        request.Request.Content = new StreamContent(content);
        request.Request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");

        (await request.GetResponseAsync())
            .AssertResponse(x => x.IsSuccessStatusCode
                ? null
                : $"Could not attach {file.Name} to {ReleaseTagName} ({(int) x.StatusCode}): {x.Content.ReadAsStringAsync().GetAwaiter().GetResult()}");

        Log.Information("Attached {Name:l} ({Size:N0} bytes) to {Tag:l}", file.Name, content.Length, ReleaseTagName);
    }
}
