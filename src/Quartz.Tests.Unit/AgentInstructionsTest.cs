using System.Text.RegularExpressions;

namespace Quartz.Tests.Unit;

/// <summary>
/// The files AI coding agents read before they touch anything.
/// </summary>
/// <remarks>
/// <c>AGENTS.md</c> is the single source of truth and every other file is a pointer to it (#3369). Two
/// things quietly break that: a pointer that grows instructions of its own and drifts, and a root file
/// that outgrows what a tool will read. Codex's <c>project_doc_max_bytes</c> is 32,768 and is a running
/// budget over the whole root-to-working-directory chain, which makes it the cap that binds here.
/// <para>
/// <c>AGENTS.md</c> stays inside that budget by stating each rule in brief and routing to a topic document
/// under <c>maintainers/</c> for the rest. That only works while the routing does: a link to a document
/// that moved sends an agent nowhere, and a document nothing links to is one no agent reads.
/// </para>
/// </remarks>
public partial class AgentInstructionsTest
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

    /// <summary>
    /// Directories no tool searches for instructions. <c>.claude</c> is here because Claude Code checks
    /// agent worktrees out under <c>.claude/worktrees/</c>, each with an <c>AGENTS.md</c> of its own.
    /// </summary>
    private static readonly string[] NotSearched = ["bin", "obj", "node_modules", ".git", ".vs", ".vuepress", "dist", "packages", "TestResults", ".claude"];

    /// <summary>
    /// The topic documents <c>AGENTS.md</c> routes to, root-relative.
    /// </summary>
    private const string TopicDirectory = "maintainers";

    /// <summary>
    /// The Claude Code subagent definitions, root-relative, and the document that lists them.
    /// </summary>
    private const string AgentDirectory = ".claude/agents";

    private const string AgentRoster = "maintainers/agent-tooling.md";

    [GeneratedRegex(@"\]\((?<target>[^)\s]+)\)", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownLink();

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
        DirectoryInfo root = RepositoryRoot.Find();

        List<string> found = Discover(root)
            .Select(x => Path.GetRelativePath(root.FullName, x.FullName).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        found.Should().BeEquivalentTo(InstructionFiles,
            "a nested instruction file is loaded by four of the surfaces this repository is written for and ignored by the rest, so a rule that lives in one is a rule most agents never read; if one is added deliberately it belongs in this list, where the size budget also covers it");
    }

    [TestCaseSource(nameof(RoutingDocumentCases))]
    public void RoutingLinkNamesAFileThatExists(FileInfo file)
    {
        List<string> broken = LocalLinks(file)
            .Where(x => !File.Exists(x.Resolved) && !Directory.Exists(x.Resolved))
            .Select(x => x.Target)
            .ToList();

        broken.Should().BeEmpty(
            $"{file.Name} routes agents to these, and a link that names no file sends an agent nowhere with no sign that it went wrong");
    }

    [Test]
    public void EveryTopicDocumentIsRouted()
    {
        DirectoryInfo root = RepositoryRoot.Find();
        FileInfo agents = new(Path.Combine(root.FullName, "AGENTS.md"));

        List<string> routed = LocalLinks(agents)
            .Select(x => Path.GetRelativePath(root.FullName, x.Resolved).Replace('\\', '/'))
            .ToList();

        List<string> documents = new DirectoryInfo(Path.Combine(root.FullName, TopicDirectory))
            .GetFiles("*.md")
            .Select(x => $"{TopicDirectory}/{x.Name}")
            .ToList();

        documents.Should().NotBeEmpty("AGENTS.md routes to topic documents under maintainers/");
        routed.Should().Contain(documents,
            "a topic document that AGENTS.md's routing table does not name is one no agent is told to read");
    }

    [TestCaseSource(nameof(AgentDefinitionCases))]
    public void AgentDefinitionIsNamedAndListed(FileInfo file)
    {
        string name = FrontmatterName(file);
        name.Should().NotBeNull($"{file.Name} must open with YAML frontmatter that has a name, or Claude Code does not load it");

        name.Should().Be(Path.GetFileNameWithoutExtension(file.Name),
            "a definition is invoked by its name, and a file named otherwise is one nobody finds by browsing the directory");

        string roster = File.ReadAllText(Path.Combine(RepositoryRoot.Find().FullName, AgentRoster));
        roster.Should().Contain($"`{name}`",
            $"{AgentRoster} is where a caller chooses an agent, so one it does not list is chosen by accident or not at all");
    }

    public static IEnumerable<TestCaseData> InstructionFileCases() => Cases(InstructionFiles);

    public static IEnumerable<TestCaseData> RoutingDocumentCases()
    {
        DirectoryInfo root = RepositoryRoot.Find();

        List<string> documents = ["AGENTS.md"];
        documents.AddRange(new DirectoryInfo(Path.Combine(root.FullName, TopicDirectory))
            .GetFiles("*.md")
            .Select(x => $"{TopicDirectory}/{x.Name}")
            .Order(StringComparer.Ordinal));

        return Cases(documents);
    }

    public static IEnumerable<TestCaseData> AgentDefinitionCases()
    {
        DirectoryInfo root = RepositoryRoot.Find();

        return Cases(new DirectoryInfo(Path.Combine(root.FullName, AgentDirectory))
            .GetFiles("*.md")
            .Select(x => $"{AgentDirectory}/{x.Name}")
            .Order(StringComparer.Ordinal)
            .ToList());
    }

    public static IEnumerable<TestCaseData> PointerFileCases() => Cases(PointerFiles);

    private static IEnumerable<TestCaseData> Cases(IReadOnlyList<string> relativePaths)
    {
        DirectoryInfo root = RepositoryRoot.Find();

        return relativePaths
            .Select(x => new TestCaseData(new FileInfo(Path.Combine(root.FullName, x))).SetArgDisplayNames(x))
            .ToList();
    }

    /// <summary>
    /// The <c>name:</c> in a definition's YAML frontmatter, or <see langword="null"/> when the file does not
    /// open with frontmatter that has one.
    /// </summary>
    private static string FrontmatterName(FileInfo file)
    {
        string[] lines = File.ReadAllLines(file.FullName);

        if (lines.Length == 0 || lines[0] != "---")
        {
            return null;
        }

        foreach (string line in lines.Skip(1).TakeWhile(x => x != "---"))
        {
            if (line.StartsWith("name:", StringComparison.Ordinal))
            {
                return line["name:".Length..].Trim();
            }
        }

        return null;
    }

    /// <summary>
    /// The links in a markdown file that point into the repository, each with the path it resolves to.
    /// A fragment is dropped: the file is what has to exist.
    /// </summary>
    private static IEnumerable<(string Target, string Resolved)> LocalLinks(FileInfo file)
    {
        foreach (Match match in MarkdownLink().Matches(File.ReadAllText(file.FullName)))
        {
            string target = match.Groups["target"].Value;

            if (target.StartsWith('#') || target.Contains("://", StringComparison.Ordinal) || target.StartsWith("mailto:", StringComparison.Ordinal))
            {
                continue;
            }

            string path = target.Split('#')[0];
            yield return (target, Path.GetFullPath(Path.Combine(file.DirectoryName!, path)));
        }
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
}
