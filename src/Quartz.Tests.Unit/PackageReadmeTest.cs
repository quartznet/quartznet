using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Quartz.Tests.Unit;

/// <summary>
/// What nuget.org shows on a package page.
/// </summary>
/// <remarks>
/// Every packable project carries its own <c>README.md</c>. They used to pack a VuePress documentation
/// page instead, and nuget.org renders CommonMark with none of VuePress's extensions: the frontmatter
/// came out as a horizontal rule followed by <c>title:</c>, every <c>::: tip</c> container came out as
/// literal text, and every relative link 404'd (#3370). Nothing failed, because nothing looked.
/// </remarks>
public class PackageReadmeTest
{
    private const string ReadmeFileName = "README.md";

    /// <summary>
    /// Inline link destinations — <c>[text](destination)</c>, and the image form.
    /// </summary>
    private static readonly Regex InlineLink = new(@"\]\((?<destination>[^)\s]+)", RegexOptions.None, TimeSpan.FromSeconds(5));

    /// <summary>
    /// Fenced code blocks, which are sample code rather than prose and are not scanned for links.
    /// </summary>
    private static readonly Regex FencedCode = new("^```.*?^```", RegexOptions.Multiline | RegexOptions.Singleline, TimeSpan.FromSeconds(5));

    [TestCaseSource(nameof(PackableProjects))]
    public void PackableProjectDeclaresItsOwnReadme(FileInfo project)
    {
        XDocument document = XDocument.Load(project.FullName);

        string declared = document.Descendants("PackageReadmeFile").Select(x => x.Value.Trim()).SingleOrDefault();
        declared.Should().Be(ReadmeFileName,
            $"{project.Name} is packable, and a package with no readme shows an empty page on nuget.org");

        FileInfo readme = new(Path.Combine(project.DirectoryName!, ReadmeFileName));
        readme.Exists.Should().BeTrue($"{project.Name} declares {ReadmeFileName} as its package readme");

        XElement packed = document
            .Descendants("None")
            .SingleOrDefault(x => string.Equals((string) x.Attribute("Include"), ReadmeFileName, StringComparison.Ordinal));

        packed.Should().NotBeNull(
            $"declaring PackageReadmeFile does not pack the file, so {project.Name} also needs a None item for it");
        ((string) packed.Attribute("Pack")).Should().Be("true", "otherwise the file is not in the package at all");
        ((string) packed.Attribute("PackagePath")).Should().NotBeNullOrEmpty(
            "the readme has to land at the package root, where PackageReadmeFile looks for it");
    }

    [TestCaseSource(nameof(PackableProjects))]
    public void PackableProjectDoesNotPackADocumentationPage(FileInfo project)
    {
        IEnumerable<string> packedFromDocs = XDocument.Load(project.FullName)
            .Descendants()
            .Where(x => x.Name.LocalName is "None" or "Content")
            .Where(x => string.Equals((string) x.Attribute("Pack"), "true", StringComparison.OrdinalIgnoreCase))
            .Select(x => (string) x.Attribute("Include"))
            .Where(x => x is not null && x.Replace('\\', '/').Contains("docs/", StringComparison.OrdinalIgnoreCase));

        packedFromDocs.Should().BeEmpty(
            $"{project.Name} must not ship a documentation page as package content — the docs site renders VuePress markup and nuget.org does not");
    }

    [TestCaseSource(nameof(PackageReadmes))]
    public void ReadmeHasNoFrontMatter(FileInfo readme)
    {
        File.ReadAllText(readme.FullName).Should().StartWith("# ",
            $"{readme.Directory!.Name}'s readme opens the nuget.org page, and a leading '---' renders there as a horizontal rule followed by literal 'title:' rather than as frontmatter");
    }

    [TestCaseSource(nameof(PackageReadmes))]
    public void ReadmeHasNoVuePressContainer(FileInfo readme)
    {
        IEnumerable<string> containers = File.ReadAllLines(readme.FullName)
            .Where(x => x.TrimStart().StartsWith(":::", StringComparison.Ordinal));

        containers.Should().BeEmpty(
            $"::: containers are a VuePress extension, and nuget.org renders {readme.Directory!.Name}'s readme as literal text instead");
    }

    [TestCaseSource(nameof(PackageReadmes))]
    public void ReadmeLinksAreAbsolute(FileInfo readme)
    {
        IEnumerable<string> relative = InlineLink
            .Matches(FencedCode.Replace(File.ReadAllText(readme.FullName), string.Empty))
            .Select(x => x.Groups["destination"].Value)
            .Where(x => !x.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                        && !x.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                        && !x.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                        && !x.StartsWith('#'));

        relative.Should().BeEmpty(
            $"a link in {readme.Directory!.Name}'s readme is resolved by nuget.org against nuget.org, so anything but an absolute URL is a 404 from a package page");
    }

    public static IEnumerable<TestCaseData> PackableProjects() =>
        ShippedProjects.Find().Select(x => new TestCaseData(x).SetArgDisplayNames(x.Directory!.Name));

    public static IEnumerable<TestCaseData> PackageReadmes() =>
        ShippedProjects.Find()
            .Select(x => new FileInfo(Path.Combine(x.DirectoryName!, ReadmeFileName)))
            .Where(x => x.Exists)
            .Select(x => new TestCaseData(x).SetArgDisplayNames(x.Directory!.Name));
}
