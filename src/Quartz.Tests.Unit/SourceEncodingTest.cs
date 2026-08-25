namespace Quartz.Tests.Unit;

/// <summary>
/// How the files under <c>src/</c> are encoded.
/// </summary>
/// <remarks>
/// <c>.editorconfig</c> asks for <c>charset = utf-8</c>, which means no byte-order mark, and nothing
/// checked: 78 files had collected one from editors and from scripts writing <c>utf-8-sig</c>, and
/// three pull requests in a row had to strip fresh ones by hand (#3391, #3393, #3396). A BOM is
/// invisible in every diff and every editor, so it comes back until something says so out loud.
/// </remarks>
public class SourceEncodingTest
{
    /// <summary>
    /// The UTF-8 byte-order mark. UTF-8 has no byte order to mark, which is why nothing here wants it.
    /// </summary>
    private static ReadOnlySpan<byte> ByteOrderMark => [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// Build output and tool caches: not ours, and large enough that walking them is a waste.
    /// </summary>
    private static readonly string[] NotSearched = ["bin", "obj", "node_modules", "TestResults", ".vs"];

    /// <summary>
    /// Verify writes its snapshots with a byte-order mark. They are its output rather than ours, so
    /// stripping one only means the next regeneration puts it back and this test fights the tool.
    /// </summary>
    private const string SnapshotMarker = ".verified.";

    [Test]
    public void NoFileUnderSourceStartsWithAByteOrderMark()
    {
        DirectoryInfo root = RepositoryRoot.Find();
        DirectoryInfo source = new(Path.Combine(root.FullName, "src"));

        source.Exists.Should().BeTrue("the tree this test walks is found from the repository root, and that walk must reach it");

        List<string> offenders = Discover(source)
            .Where(StartsWithByteOrderMark)
            .Select(x => Path.GetRelativePath(root.FullName, x.FullName).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty(
            "a byte-order mark is not what .editorconfig's charset = utf-8 asks for, and it is invisible in "
            + "every diff and every editor that put it there. These files start with one:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders.Select(x => "    " + x))
            + Environment.NewLine
            + "Save the file as \"UTF-8\" rather than \"UTF-8 with signature\"; from Python write it with "
            + "encoding=\"utf-8\", never utf-8-sig; from PowerShell with -Encoding utf8NoBOM. Files named "
            + $"*{SnapshotMarker}* are exempt, because Verify writes the mark and would put it straight back");
    }

    private static bool StartsWithByteOrderMark(FileInfo file)
    {
        using FileStream stream = file.OpenRead();

        Span<byte> head = stackalloc byte[3];
        int read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);

        return read == head.Length && head.SequenceEqual(ByteOrderMark);
    }

    private static IEnumerable<FileInfo> Discover(DirectoryInfo directory)
    {
        foreach (FileInfo file in directory.EnumerateFiles())
        {
            if (!file.Name.Contains(SnapshotMarker, StringComparison.Ordinal))
            {
                yield return file;
            }
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
