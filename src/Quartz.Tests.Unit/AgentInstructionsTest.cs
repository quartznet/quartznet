namespace Quartz.Tests.Unit;

/// <summary>
/// The files AI coding agents read before they touch anything.
/// </summary>
/// <remarks>
/// <c>AGENTS.md</c> is the single source of truth and every other file is a pointer to it (#3369). Two
/// things quietly break that: a pointer that grows instructions of its own and drifts, and a root file
/// that outgrows what a tool will read. Codex's <c>project_doc_max_bytes</c> is 32,768 and is a running
/// budget over the whole root-to-working-directory chain, which makes it the cap that binds here.
/// </remarks>
public class AgentInstructionsTest
{
    /// <summary>
    /// Codex's <c>project_doc_max_bytes</c> default, and the smallest documented budget of the tools
    /// this repository is written for. Past it, the file is truncated mid-sentence without a word.
    /// </summary>
    private const int MaxInstructionBytes = 32 * 1024;

    /// <summary>
    /// A pointer is a paragraph saying where the instructions are. Anything longer is a copy.
    /// </summary>
    private const int MaxPointerBytes = 2 * 1024;

    /// <summary>
    /// Every instruction file this repository has, root-relative. Adding one means adding it here,
    /// which is what keeps <see cref="EveryInstructionFileIsOneTheRootAccountsFor"/> honest.
    /// </summary>
    private static readonly string[] InstructionFiles =
    [
        "AGENTS.md",
        "CLAUDE.md",
        ".github/copilot-instructions.md",
    ];

    /// <summary>
    /// The files that exist only to route a tool to <c>AGENTS.md</c>, root-relative.
    /// </summary>
    private static readonly string[] PointerFiles =
    [
        "CLAUDE.md",
        ".github/copilot-instructions.md",
        ".aider.conf.yml",
        ".gemini/settings.json",
    ];

    /// <summary>
    /// The names a tool discovers on its own. A file with one of these names is loaded whether or not
    /// anyone remembered it exists, so the set of them has to match what the root declares.
    /// </summary>
    private static readonly string[] DiscoveredNames = ["AGENTS.md", "CLAUDE.md", "GEMINI.md", "copilot-instructions.md"];

    private static readonly string[] NotSearched = ["bin", "obj", "node_modules", ".git", ".vs", ".vuepress", "dist", "packages", "TestResults"];

    [TestCaseSource(nameof(InstructionFileCases))]
    public void InstructionFileFitsTheSmallestDocumentedBudget(FileInfo file)
    {
        file.Exists.Should().BeTrue($"{file.Name} is one of the instruction files the repository declares");
        file.Length.Should().BeLessThanOrEqualTo(MaxInstructionBytes,
            $"{file.Name} is truncated without warning past Codex's project_doc_max_bytes, and the budget is spent across every instruction file it loads, not per file");
    }

    [TestCaseSource(nameof(PointerFileCases))]
    public void PointerFileNamesTheInstructionsItPointsAt(FileInfo file)
    {
        File.ReadAllText(file.FullName).Should().Contain("AGENTS.md",
            $"{file.Name} exists to route a tool to the instructions, and a pointer that stops naming them routes nowhere");
    }

    [TestCaseSource(nameof(PointerFileCases))]
    public void PointerFileStaysAPointer(FileInfo file)
    {
        file.Length.Should().BeLessThanOrEqualTo(MaxPointerBytes,
            $"{file.Name} has grown past a pointer into a copy, and a copy drifts from AGENTS.md — Copilot combines the instruction files it finds rather than picking one, so a duplicated rule is also applied twice");
    }

    [Test]
    public void EveryInstructionFileIsOneTheRootAccountsFor()
    {
        DirectoryInfo root = FindRepositoryRoot();

        List<string> found = Discover(root)
            .Select(x => Path.GetRelativePath(root.FullName, x.FullName).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        found.Should().BeEquivalentTo(InstructionFiles,
            "a nested instruction file is loaded by four of the surfaces this repository is written for and ignored by the rest, so a rule that lives in one is a rule most agents never read; if one is added deliberately it belongs in this list, where the size budget also covers it");
    }

    public static IEnumerable<TestCaseData> InstructionFileCases() => Cases(InstructionFiles);

    public static IEnumerable<TestCaseData> PointerFileCases() => Cases(PointerFiles);

    private static IEnumerable<TestCaseData> Cases(IReadOnlyList<string> relativePaths)
    {
        DirectoryInfo root = FindRepositoryRoot();

        return relativePaths
            .Select(x => new TestCaseData(new FileInfo(Path.Combine(root.FullName, x))).SetArgDisplayNames(x))
            .ToList();
    }

    private static IEnumerable<FileInfo> Discover(DirectoryInfo directory)
    {
        foreach (FileInfo file in directory.GetFiles())
        {
            if (DiscoveredNames.Contains(file.Name, StringComparer.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }

        foreach (DirectoryInfo child in directory.GetDirectories())
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

    private static DirectoryInfo FindRepositoryRoot()
    {
        DirectoryInfo directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Quartz.slnx")))
        {
            directory = directory.Parent;
        }

        return directory ?? throw new InvalidOperationException(
            $"No Quartz.slnx above {AppContext.BaseDirectory}, so the instruction files cannot be found.");
    }
}
