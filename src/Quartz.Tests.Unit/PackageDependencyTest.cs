using System.Xml.Linq;

namespace Quartz.Tests.Unit;

/// <summary>
/// What a shipped package is allowed to depend on.
/// </summary>
/// <remarks>
/// <para>
/// NuGet refuses a stable release that depends on a prerelease — NU5104, which
/// <c>TreatWarningsAsErrors</c> makes fatal — and this repository has never produced a stable version:
/// a local build carries a <c>dev-</c> suffix unconditionally, an untagged CI build a <c>preview-</c>
/// one, and every tag so far has been a prerelease. So the rule has never been evaluated, and the only
/// build that would evaluate it is the tag build that publishes to nuget.org (#3679).
/// </para>
/// <para>
/// Central package management is what makes it checkable from source instead: a version is written in
/// <c>Directory.Packages.props</c> and nowhere else, so a project's <c>PackageReference</c> plus that
/// file is the dependency its nuspec will carry. <c>Directory.Packages.props</c> does pin a preview on
/// purpose — <c>Microsoft.Data.SQLite</c>, for the tests and the trim canary — and this test is what
/// says that pin may not reach a package that ships.
/// </para>
/// <para>
/// The one door it does not walk is transitive pinning, which can add a dependency no project names:
/// that needs a restore graph rather than a file, and the stable-version packing dry run recorded on
/// #3679 is what covers it.
/// </para>
/// <para>
/// The same reading answers a second question, which 4.0.0 got wrong: not whether a dependency is
/// stable but how low it is. Every version here is copied into a nuspec as a *minimum*, and NuGet
/// resolves a minimum by raising everything else to meet it, so a floor at the newest servicing patch
/// stops an application pinning the one before it from restoring at all (#3717). The floors live in
/// their own item group in <c>Directory.Packages.props</c>, under a comment saying why, and
/// <see cref="MicrosoftFloorIsTheMajorItself" /> is what holds them there.
/// </para>
/// </remarks>
public class PackageDependencyTest
{
    [TestCaseSource(nameof(ShippedPackageReferences))]
    public void ShippedPackageTakesNoPrereleaseDependency(string project, string package, string version)
    {
        version.Should().NotBeNullOrEmpty(
            $"{project} references {package}, and central package management is the only place its version may be written");

        IsPrerelease(version).Should().BeFalse(
            $"{project} ships, and NuGet refuses a stable release with a prerelease dependency (NU5104) — {package} {version} would fail the tag build rather than this one");
    }

    /// <summary>
    /// A <c>Microsoft.*</c> floor is the major itself — <c>x.0.0</c> — and never a servicing patch of it.
    /// </summary>
    /// <remarks>
    /// This is the rule 4.0.0 broke. Its nuspecs floored six framework extensions at 10.0.11, the
    /// central version on the day it was built, so an application pinning
    /// <c>Microsoft.Extensions.Options</c> at 10.0.10 — which is what a .NET 10 project or SDK image
    /// carries — was told "detected package downgrade" and stopped restoring (#3717). Nothing in Quartz
    /// needs a servicing patch of the framework extensions, and a floor is a demand made of every
    /// consumer, so the floor is the major.
    /// </remarks>
    [TestCaseSource(nameof(MicrosoftFloors))]
    public void MicrosoftFloorIsTheMajorItself(string package, string version)
    {
        version.Should().MatchRegex(@"^\d+\.0\.0$",
            $"{package} is a floor a consumer inherits from a shipped nuspec, and {version} would make "
            + "every application pinning an earlier patch of it fail to restore with NU1605. See "
            + "\"The floors a consumer inherits\" in Directory.Packages.props: a floor moves for a "
            + "security advisory and nothing else, and then to the version that fixed it");
    }

    /// <summary>
    /// Every <c>Microsoft.*</c> a shipped project references is declared with the floors, so the list
    /// this file checks, the comment that explains them and the Dependabot ignore entries are one set.
    /// </summary>
    /// <remarks>
    /// Without this a new package reference would land in the alphabetical group at whatever version
    /// Dependabot last proposed, pass the test above because that test never sees it, and ship a floor
    /// nobody chose. Transitive pinning makes the reach wider than the reference: <c>Quartz.Jobs</c>
    /// names no framework extension at all and carries six of them in its nuspec, inherited from
    /// <c>Quartz</c>.
    /// </remarks>
    [TestCaseSource(nameof(ShippedMicrosoftReferences))]
    public void ShippedMicrosoftDependencyIsDeclaredAsAFloor(string project, string package)
    {
        Floors().Keys.Should().Contain(package,
            $"{project} ships, so the version it resolves {package} at becomes a floor in its nuspec. "
            + $"Move the PackageVersion into the \"{FloorsLabel}\" group in Directory.Packages.props, "
            + "at the lowest version of its major that still compiles, and add an ignore entry for it "
            + "in .github/dependabot.yml");
    }

    /// <summary>
    /// A shipped project takes its versions from the central file and nowhere else.
    /// </summary>
    /// <remarks>
    /// <c>VersionOverride</c> is how a project that ships nothing steps over a floor — the Aspire
    /// example does it for nine ids, because Aspire asks for the newest patch of each and an AppHost
    /// inherits nothing to anybody. On a shipped project the same attribute would write that version
    /// into a nuspec, where it becomes a demand made of every consumer, and it would do it out of sight
    /// of both this file's floor list and Dependabot's ignore entries.
    /// </remarks>
    [TestCaseSource(nameof(PackableProjects))]
    public void ShippedProjectOverridesNoVersion(FileInfo project)
    {
        IEnumerable<string> overridden = XDocument.Load(project.FullName)
            .Descendants("PackageReference")
            .Where(x => x.Attribute("VersionOverride") is not null)
            .Select(x => (string) x.Attribute("Include"));

        overridden.Should().BeEmpty(
            $"{project.Name} ships, so every version it resolves becomes a floor in its nuspec — and a "
            + "VersionOverride is a floor written where neither Directory.Packages.props nor "
            + ".github/dependabot.yml can see it");
    }

    /// <summary>
    /// A shim carries exactly one dependency, on the package that replaced it, at its own version.
    /// </summary>
    /// <remarks>
    /// That single dependency is the entire payload of the four empty packages 4.0.1 publishes under the
    /// ids 4.0 folded away, and both halves of it matter. A second dependency, or one on something other
    /// than the replacement, and a consumer who takes the shim by accident gets a graph nobody designed;
    /// a version that is not this build's, and the group a dependency bot resolves stops being pinned to
    /// the release the shim was published with, which is the whole reason the package exists (#3717). A
    /// <c>ProjectReference</c> gives the version for free, so this test is really about keeping it one.
    /// </remarks>
    [TestCaseSource(nameof(ShimProjects))]
    public void ShimDependsOnItsReplacementAndNothingElse(FileInfo project)
    {
        XDocument document = XDocument.Load(project.FullName);

        document.Descendants("PackageReference").Should().BeEmpty(
            $"{project.Name} is a shim, and a PackageReference would be a version resolved from "
            + "Directory.Packages.props rather than this build's own — the shim has to move with the "
            + "release it is published beside");

        List<string> replacements = document
            .Descendants("ProjectReference")
            .Select(x => Path.GetFileNameWithoutExtension(((string) x.Attribute("Include")).Replace('\\', '/')))
            .ToList();

        replacements.Should().ContainSingle(
            $"{project.Name} ships one dependency and nothing else: the package that replaced it")
            .Which.Should().BeOneOf(["Quartz", "Quartz.Serialization.Newtonsoft"],
                "those are the two packages the four folded ids were folded into");

        ShimProps().Descendants("CentralPackageTransitivePinningEnabled")
            .Select(x => x.Value.Trim())
            .Should().Equal(["false"],
                "transitive pinning writes every centrally versioned id in the graph into the nuspec as a "
                + "dependency of its own, which turned this package into one with seven of them the first "
                + "time it was packed. A shim has no assembly to pin anything for");
    }

    /// <summary>
    /// A shim packs no assembly, which is what keeps it clear of the types it used to hold.
    /// </summary>
    /// <remarks>
    /// Eight type names exist in both a 3.x assembly under one of these ids and <c>Quartz</c> 4.x, and the
    /// migration guide documents the <c>CS0433</c> a consumer gets when both are referenced. A shim that
    /// packed its (empty) build output would put an assembly with that identity back on the compiler's
    /// reference list; <c>IncludeBuildOutput=false</c> in <c>src/QuartzShimPackage.props</c> is what does
    /// not, and the import is how a project gets it.
    /// </remarks>
    [TestCaseSource(nameof(ShimProjects))]
    public void ShimPacksNoAssembly(FileInfo project)
    {
        IEnumerable<string> imports = XDocument.Load(project.FullName)
            .Descendants("Import")
            .Select(x => ((string) x.Attribute("Project"))?.Replace('\\', '/'))
            .Where(x => x is not null);

        imports.Should().Contain(x => x.EndsWith(ShimPropsFileName, StringComparison.Ordinal),
            $"{project.Name} is a shim, and {ShimPropsFileName} is what turns IncludeBuildOutput off — a "
            + "shim that packed an assembly would reintroduce the CS0433 the migration guide documents");

        ShimProps().Descendants("IncludeBuildOutput")
            .Select(x => x.Value.Trim())
            .Should().Equal(["false"],
                $"{ShimPropsFileName} is the one place that decides a shim carries no assembly");
    }

    /// <summary>
    /// The shared properties the four shims import, which is where what makes a shim a shim is written.
    /// </summary>
    private const string ShimPropsFileName = "QuartzShimPackage.props";

    private static XDocument ShimProps()
    {
        FileInfo file = new(Path.Combine(RepositoryRoot.Find().FullName, "src", ShimPropsFileName));
        file.Exists.Should().BeTrue("the four shim projects import it, and it is where they say what they are");

        return XDocument.Load(file.FullName);
    }

    public static IEnumerable<TestCaseData> ShimProjects()
    {
        List<FileInfo> shims = ShippedProjects.Find().Where(ShippedProjects.IsShim).ToList();

        shims.Should().HaveCount(4,
            "the four ids 4.0 folded away are each published as an empty package at every 4.x version, and "
            + "a shim that stops being one takes its whole guard with it rather than failing anything");

        return shims.Select(x => new TestCaseData(x).SetArgDisplayNames(x.Directory!.Name));
    }

    public static IEnumerable<TestCaseData> PackableProjects() => ShippedProjects.Find()
        .Select(x => new TestCaseData(x).SetArgDisplayNames(x.Directory!.Name));

    public static IEnumerable<TestCaseData> MicrosoftFloors() => Floors()
        .Where(x => IsMicrosoft(x.Key))
        .OrderBy(x => x.Key, StringComparer.Ordinal)
        .Select(x => new TestCaseData(x.Key, x.Value).SetArgDisplayNames(x.Key));

    public static IEnumerable<TestCaseData> ShippedMicrosoftReferences() => ShippedProjects.Find()
        .SelectMany(project => PackageReferences(project)
            .Where(IsMicrosoft)
            .Select(package => new TestCaseData(project.Directory!.Name, package)
                .SetArgDisplayNames(project.Directory!.Name, package)));

    /// <summary>
    /// The ids that come out of <c>dotnet/runtime</c> and <c>dotnet/aspnetcore</c>, which are the ones
    /// versioned in lockstep with the framework a consumer is already on.
    /// </summary>
    private static bool IsMicrosoft(string package) =>
        package.StartsWith("Microsoft.", StringComparison.Ordinal);

    /// <summary>
    /// The <c>Label</c> on the item group holding the floors, which is what makes the set readable from
    /// here rather than guessed at by prefix.
    /// </summary>
    private const string FloorsLabel = "Dependency floors";

    /// <summary>
    /// The floors, read out of the labelled item group in <c>Directory.Packages.props</c>.
    /// </summary>
    private static IReadOnlyDictionary<string, string> Floors()
    {
        XElement group = PackagesProps()
            .Descendants("ItemGroup")
            .SingleOrDefault(x => string.Equals((string) x.Attribute("Label"), FloorsLabel, StringComparison.Ordinal));

        group.Should().NotBeNull(
            $"the floors are read from the item group labelled \"{FloorsLabel}\" in Directory.Packages.props, "
            + "and this test cannot check a set it cannot find");

        return group.Elements("PackageVersion").ToDictionary(
            x => (string) x.Attribute("Include"),
            x => (string) x.Attribute("Version"),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Every (project, package) pair that becomes a dependency in a shipped package's nuspec.
    /// </summary>
    /// <remarks>
    /// A reference marked <c>PrivateAssets="all"</c> is a build-time tool rather than a dependency, so
    /// it is left out — the analyzer every shipped project gets from <c>Directory.Build.targets</c> is
    /// exactly that, and it is why the nuspecs list no analyzer.
    /// </remarks>
    public static IEnumerable<TestCaseData> ShippedPackageReferences()
    {
        IReadOnlyDictionary<string, string> versions = CentralPackageVersions();

        List<TestCaseData> cases = ShippedProjects.Find()
            .SelectMany(project => PackageReferences(project)
                .Select(package => new TestCaseData(project.Directory!.Name, package, versions.GetValueOrDefault(package))
                    .SetArgDisplayNames(project.Directory!.Name, package)))
            .ToList();

        cases.Should().NotBeEmpty(
            "the pairs are found by reading the shipped projects, and a walk that reaches none of them would pass anything");

        return cases;
    }

    /// <summary>
    /// Whether a version string names a prerelease: everything after the first <c>-</c> and before the
    /// build metadata, which is what NuGet reads to decide that NU5104 applies.
    /// </summary>
    private static bool IsPrerelease(string version) =>
        version.Split('+')[0].Contains('-', StringComparison.Ordinal);

    private static IEnumerable<string> PackageReferences(FileInfo project) => XDocument
        .Load(project.FullName)
        .Descendants("PackageReference")
        .Where(x => !string.Equals((string) x.Attribute("PrivateAssets"), "all", StringComparison.OrdinalIgnoreCase))
        .Select(x => (string) x.Attribute("Include"))
        .Where(x => !string.IsNullOrEmpty(x));

    /// <summary>
    /// Every <c>PackageVersion</c> in <c>Directory.Packages.props</c>, with the property references a
    /// few of them use resolved against the same file's own properties.
    /// </summary>
    private static IReadOnlyDictionary<string, string> CentralPackageVersions()
    {
        XDocument document = PackagesProps();

        Dictionary<string, string> properties = document
            .Descendants("PropertyGroup")
            .Elements()
            .ToDictionary(x => x.Name.LocalName, x => x.Value.Trim(), StringComparer.OrdinalIgnoreCase);

        return document
            .Descendants("PackageVersion")
            .ToDictionary(
                x => (string) x.Attribute("Include"),
                x => Resolve((string) x.Attribute("Version"), properties),
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <c>Directory.Packages.props</c>, which is where every version in this repository is written.
    /// </summary>
    private static XDocument PackagesProps()
    {
        FileInfo file = new(Path.Combine(RepositoryRoot.Find().FullName, "Directory.Packages.props"));
        file.Exists.Should().BeTrue("every version in this repository is centrally managed, so this file is where they all are");

        return XDocument.Load(file.FullName);
    }

    /// <summary>
    /// Substitutes a single <c>$(Name)</c> property reference, which is the only form the file uses —
    /// Aspire ships its packages and its MSBuild SDK as one version, so they are written as a property.
    /// </summary>
    private static string Resolve(string version, IReadOnlyDictionary<string, string> properties)
    {
        if (version is null || !version.StartsWith("$(", StringComparison.Ordinal) || !version.EndsWith(')'))
        {
            return version;
        }

        return properties.GetValueOrDefault(version[2..^1], version);
    }
}
