using System.Xml.Linq;

namespace Quartz.Tests.Unit;

/// <summary>
/// The projects under <c>src</c> that produce a package, for the tests that assert something about
/// what ships rather than about the code in it.
/// </summary>
internal static class ShippedProjects
{
    /// <summary>
    /// Every project under <c>src</c> that produces a package. <c>Directory.Build.props</c> turns packing
    /// on for the repository, so the packable ones are the ones that have not turned it back off.
    /// </summary>
    /// <remarks>
    /// Only the project file at the top of each project directory. A recursive search would also find the
    /// projects BenchmarkDotNet generates under <c>bin</c>, which are packable by default and belong to
    /// no package.
    /// </remarks>
    public static List<FileInfo> Find()
    {
        List<FileInfo> projects = RepositoryRoot.Find()
            .GetDirectories("src")
            .Single()
            .GetDirectories()
            .SelectMany(x => x.GetFiles("*.csproj", SearchOption.TopDirectoryOnly))
            .Where(x => !IsOptedOut(x))
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .ToList();

        projects.Should().NotBeEmpty("the packable projects are found by walking the repository, and that walk must reach them");
        return projects;
    }

    private static bool IsOptedOut(FileInfo project) => XDocument.Load(project.FullName)
        .Descendants("IsPackable")
        .Any(x => string.Equals(x.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase));
}
