using System.Xml.Linq;

namespace Quartz.Tests.Unit;

/// <summary>
/// What tells a reader which package they are looking at.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PackageReadmeTest" /> covers the page body; this covers the row above it. A shared default
/// in <c>Directory.Build.props</c> is invisible on nuget.org, which renders the id when it has nothing
/// better, and very visible in Visual Studio's package manager and <c>dotnet package search</c>, which
/// render the title: six of the ten packages shipped through beta.1 titled "Quartz.NET", and two of
/// them described as the framework rather than as themselves (#3679).
/// </para>
/// <para>
/// Every packable project therefore writes its own title, description and tags, and this test is what
/// notices a new one that does not. It reads the project files rather than a package, so it costs
/// nothing and runs beside the rest of the unit suite.
/// </para>
/// </remarks>
public class PackageMetadataTest
{
    /// <summary>
    /// The properties a package page shows, which every shipped project has to answer for itself.
    /// </summary>
    private static readonly string[] OwnProperties = ["Title", "Description", "PackageTags"];

    [TestCaseSource(nameof(ShippedProjectProperties))]
    public void ShippedProjectDescribesItself(FileInfo project, string property)
    {
        Declared(project, property).Should().NotBeNullOrWhiteSpace(
            $"nuget.org, Visual Studio's package manager and 'dotnet package search' all show {property} per package, "
            + $"and {project.Name} would otherwise be shown as whatever Directory.Build.props happens to say");
    }

    [TestCaseSource(nameof(OwnPropertyNames))]
    public void NoTwoShippedProjectsShareThem(string property)
    {
        IEnumerable<IGrouping<string, string>> shared = ShippedProjects.Find()
            .Select(x => (Project: x.Directory!.Name, Value: Declared(x, property)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .GroupBy(x => x.Value, x => x.Project, StringComparer.Ordinal)
            .Where(x => x.Count() > 1);

        shared.Should().BeEmpty(
            $"two packages with the same {property} are two rows a reader cannot tell apart, which is the whole reason the property is written per package");
    }

    /// <summary>
    /// <c>PackageIconUrl</c> is NuGet's deprecated icon, and it is not set anywhere.
    /// </summary>
    /// <remarks>
    /// The icon ships inside the package as <c>PackageIcon</c>, so the URL adds nothing but a second
    /// answer to the same question — and the one that was here pointed into a <c>master</c> branch this
    /// repository does not have. NuGet warns about the property (NU5048) only when <c>PackageIcon</c> is
    /// absent, so nothing was going to report it.
    /// </remarks>
    [Test]
    public void NothingSetsTheDeprecatedIconUrl()
    {
        DirectoryInfo root = RepositoryRoot.Find();

        List<FileInfo> files = [new(Path.Combine(root.FullName, "Directory.Build.props")), .. ShippedProjects.Find()];

        IEnumerable<string> setting = files
            .Where(x => XDocument.Load(x.FullName).Descendants("PackageIconUrl").Any())
            .Select(x => x.Name);

        setting.Should().BeEmpty(
            "PackageIconUrl is deprecated and PackageIcon already ships the icon inside the package");
    }

    public static IEnumerable<TestCaseData> ShippedProjectProperties() => ShippedProjects.Find()
        .SelectMany(project => OwnProperties
            .Select(property => new TestCaseData(project, property).SetArgDisplayNames(project.Directory!.Name, property)));

    public static IEnumerable<TestCaseData> OwnPropertyNames() => OwnProperties.Select(x => new TestCaseData(x));

    /// <summary>
    /// What the project file itself says, which is the only place that answers for one package —
    /// anything inherited answers for all of them.
    /// </summary>
    private static string Declared(FileInfo project, string property) => XDocument
        .Load(project.FullName)
        .Descendants(property)
        .Select(x => x.Value.Trim())
        .SingleOrDefault();
}
