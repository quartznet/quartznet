namespace Quartz.Tests.Unit;

/// <summary>
/// What the documentation comments under <c>src/</c> are allowed to contain.
/// </summary>
/// <remarks>
/// A documentation comment is compiled, not rendered here, so a tag the compiler does not know is
/// copied into the XML file verbatim and then dropped by every reader of it. That failure is silent:
/// the promise looks written down in the source and reaches nobody.
/// </remarks>
public class XmlDocumentationTest
{
    /// <summary>
    /// Build output and tool caches: not ours, and large enough that walking them is a waste.
    /// </summary>
    private static readonly string[] NotSearched = ["bin", "obj", "node_modules", "TestResults", ".vs"];

    /// <summary>
    /// The forbidden element, assembled rather than written out, because this file has to be able to
    /// name it in the failure message without becoming the thing it reports.
    /// </summary>
    private static readonly string JavadocThrowsTag = "<" + "throws>";

    /// <summary>
    /// Java's Javadoc spells a thrown exception <c>@throws</c>, and eight members had carried the tag
    /// across as the element this test forbids — including <c>IJobStore.ScheduleJob</c> and
    /// <c>IJobStore.AddCalendar</c>, whose only statement of what a duplicate key does it was. C#
    /// spells it <c>&lt;exception cref="…"&gt;</c>; anything else is an unknown element that the
    /// compiler copies into <c>Quartz.xml</c> and no documentation renderer and no IDE ever shows.
    /// </summary>
    [Test]
    public void NoDocumentationCommentUsesTheJavadocThrowsTag()
    {
        DirectoryInfo root = RepositoryRoot.Find();
        DirectoryInfo source = new(Path.Combine(root.FullName, "src"));

        source.Exists.Should().BeTrue("the tree this test walks is found from the repository root, and that walk must reach it");

        List<string> offenders = [];

        foreach (FileInfo file in Discover(source))
        {
            string[] lines = File.ReadAllLines(file.FullName);

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(JavadocThrowsTag, StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetRelativePath(root.FullName, file.FullName).Replace('\\', '/')}({i + 1})");
                }
            }
        }

        offenders.Should().BeEmpty(
            $"{JavadocThrowsTag} is Javadoc's tag, not C#'s. The compiler passes it through into the XML "
            + "documentation file as an unknown element, so the promise it carries reaches no renderer and no "
            + "IDE. Write <exception cref=\"…\">…</exception> instead. These lines have one:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders.Select(x => "    " + x)));
    }

    private static IEnumerable<FileInfo> Discover(DirectoryInfo directory)
    {
        foreach (FileInfo file in directory.EnumerateFiles("*.cs"))
        {
            yield return file;
        }

        foreach (DirectoryInfo child in directory.EnumerateDirectories())
        {
            if (NotSearched.Contains(child.Name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (FileInfo file in Discover(child))
            {
                yield return file;
            }
        }
    }
}
