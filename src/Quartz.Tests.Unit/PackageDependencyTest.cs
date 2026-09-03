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
        FileInfo file = new(Path.Combine(RepositoryRoot.Find().FullName, "Directory.Packages.props"));
        file.Exists.Should().BeTrue("every version in this repository is centrally managed, so this file is where they all are");

        XDocument document = XDocument.Load(file.FullName);

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
