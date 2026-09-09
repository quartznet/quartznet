namespace Quartz.Tests.Unit;

/// <summary>
/// What a test fixture may do with Microsoft.Data.Sqlite's connection pool, which is: nothing global.
/// </summary>
/// <remarks>
/// <para>
/// <c>SqliteConnection.ClearAllPools()</c> clears every pool in the process, and the pool hands out the
/// underlying handle rather than a copy — so with <c>[assembly: Parallelizable(ParallelScope.Fixtures)]</c>
/// one fixture's teardown disposes a connection another fixture is in the middle of using. What that
/// looks like from the outside is <c>ObjectDisposedException: SQLitePCL.sqlite3</c> thrown from a setup
/// that did nothing wrong, in a different fixture each time, and never twice in a row (#3755). Twenty-four
/// fixtures across three test projects called it, so the fix was not to reason about which of them could
/// collide.
/// </para>
/// <para>
/// A grep rather than an analyzer, because the rule is about who may call one method and the whole
/// audience is this repository's own test projects. <c>Quartz.Trimming.Canary</c> still calls it and is
/// deliberately out of scope: it is a single-purpose executable that owns its process and its one
/// database, which is the only arrangement in which the global call means what it says.
/// </para>
/// </remarks>
public class SqlitePoolClearingTest
{
    /// <summary>
    /// The call, spelled as a call — <c>&lt;see cref="..." /&gt;</c> and prose mentions carry no
    /// parenthesis, and this test's own file is skipped anyway.
    /// </summary>
    private const string GlobalPoolClear = "SqliteConnection.ClearAllPools(";

    /// <summary>The test projects this rule is about, matched as a prefix of the directory name.</summary>
    private const string TestProjectPrefix = "Quartz.Tests.";

    /// <summary>
    /// Build output and tool caches: not ours, and large enough that walking them is a waste.
    /// </summary>
    private static readonly string[] NotSearched = ["bin", "obj", "TestResults", ".vs"];

    [Test]
    public void NoTestFixtureClearsEveryConnectionPoolInTheProcess()
    {
        DirectoryInfo root = RepositoryRoot.Find();
        DirectoryInfo source = new(Path.Combine(root.FullName, "src"));

        List<DirectoryInfo> projects = source.EnumerateDirectories(TestProjectPrefix + "*").ToList();

        projects.Should().NotBeEmpty(
            "this test walks the test projects by name, so a rename that leaves nothing to walk has to fail "
            + "here rather than pass by finding nothing");

        List<string> offenders = projects
            .SelectMany(Discover)
            .Where(x => !string.Equals(x.Name, ThisFile, StringComparison.Ordinal))
            .Where(x => File.ReadAllText(x.FullName).Contains(GlobalPoolClear, StringComparison.Ordinal))
            .Select(x => Path.GetRelativePath(root.FullName, x.FullName).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty(
            $"{GlobalPoolClear}) is process-global and the fixtures run in parallel, so it disposes "
            + "connections other fixtures are still using. A fixture that owns a SQLite file takes a "
            + "SqliteTestDatabase instead: it names a temporary path of its own, connects with Pooling=False "
            + "so there is no pool to clear, and deletes the file when it is disposed. These files still "
            + "call it:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders.Select(x => "    " + x))
            + Environment.NewLine
            + "See https://github.com/quartznet/quartznet/issues/3755. A fixture that genuinely needs a pool "
            + "clears its own with SqliteConnection.ClearPool(connection) and says here why it is exempt");
    }

    /// <summary>
    /// This file, which spells the call it is banning and would otherwise report itself.
    /// </summary>
    private static string ThisFile => nameof(SqlitePoolClearingTest) + ".cs";

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
