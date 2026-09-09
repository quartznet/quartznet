using Microsoft.Data.Sqlite;

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
    /// The replacement keeps the promise the rule above is built on: no pool, and no file left behind.
    /// </summary>
    /// <remarks>
    /// <c>Pooling</c> is asserted through the builder rather than by looking for a substring, because
    /// what matters is the value the driver reads — a keyword the builder decided was a default and
    /// dropped would leave every fixture pooling again with nothing to say so.
    /// </remarks>
    [Test]
    public void ASqliteTestDatabaseTurnsPoolingOffAndTakesItsFileWithIt()
    {
        string file;

        using (SqliteTestDatabase database = new("pool-clearing"))
        {
            SqliteConnectionStringBuilder parsed = new(database.ConnectionString);

            parsed.Pooling.Should().BeFalse(
                "the fixtures stopped clearing pools because they no longer create one, so a connection "
                + "string that quietly pooled would put the race back with nothing left to notice it");

            file = database.DatabaseFile;
            parsed.DataSource.Should().Be(file,
                "the path a fixture is handed has to be the database its connection string opens");

            using SqliteConnection connection = new(database.ConnectionString);
            connection.Open();

            File.Exists(file).Should().BeTrue("opening a connection is what creates the file");
        }

        File.Exists(file).Should().BeFalse(
            "a fixture that owns its database leaves nothing behind in the temporary directory — which "
            + "is what the ClearAllPools() call was there to make possible");
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
