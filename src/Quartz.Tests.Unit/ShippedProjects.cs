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

    /// <summary>
    /// Whether a project produces a package that carries no assembly — one of the four empty packages
    /// published under the ids 4.0 folded away, so that a grouped dependency update can resolve to 4.x
    /// instead of re-anchoring on 3.20.1 (#3717).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A test that asks something of an assembly has nothing to ask here: there is no public surface to
    /// snapshot and no call site to route through a <c>[LoggerMessage]</c> method. Each such test skips a
    /// shim by this property, with the reason written down, rather than by name or by an empty baseline
    /// file that would look like a real one. Everything asked of a package *page* — its readme, its title,
    /// its description — still applies, because an empty package is still a page somebody lands on.
    /// </para>
    /// <para>
    /// The four say <c>QuartzShimPackage</c> in their own project files rather than inheriting it from
    /// <c>src/QuartzShimPackage.props</c>, which holds the rest of what they share: it is the one property
    /// that changes what other tests do with the project, so it is written where a reader of that project
    /// sees it. Read here from the file rather than through MSBuild, the way the rest of this class reads.
    /// </para>
    /// </remarks>
    public static bool IsShim(FileInfo project) => XDocument.Load(project.FullName)
        .Descendants("QuartzShimPackage")
        .Any(x => string.Equals(x.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase));

    private static bool IsOptedOut(FileInfo project) => XDocument.Load(project.FullName)
        .Descendants("IsPackable")
        .Any(x => string.Equals(x.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase));
}
