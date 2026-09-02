#nullable enable

using System.Reflection;
using System.Xml.Linq;

using Quartz.Serialization.Newtonsoft;

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

    /// <summary>
    /// A <c>cref</c> on a member a consumer can see must name something a consumer can reach. The
    /// compiler is no help: CS1574 is satisfied the moment the target resolves anywhere inside the
    /// assembly, so all four public misfire enums could point a reader at the internal
    /// <c>MisfireInstruction</c> they exist to replace, and the settings whose whole meaning is which
    /// of the two ADO stores is built could name both of those internal types.
    /// </summary>
    [Test]
    public void NoDocumentationOnAVisibleMemberPointsAtSomethingInvisible()
    {
        Assembly quartz = typeof(IScheduler).Assembly;
        string documentation = Path.ChangeExtension(quartz.Location, ".xml");

        File.Exists(documentation).Should().BeTrue(
            "Quartz ships its XML documentation, so GenerateDocumentationFile is on and the file is beside the assembly");

        Dictionary<string, Type> types = quartz.GetTypes()
            .Where(x => x.FullName is not null)
            .GroupBy(x => x.FullName!.Replace('+', '.'), StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);

        List<string> offenders = [];

        foreach (XElement member in XDocument.Load(documentation).Descendants("member"))
        {
            string documented = member.Attribute("name")?.Value ?? "";

            if (!IsVisibleApi(documented, types))
            {
                continue;
            }

            foreach (XElement node in member.Descendants())
            {
                // <inheritdoc cref="…" /> copies the target's text in rather than linking to it, so the
                // reader never learns the name and inheriting from a private sibling is fine.
                if (node.Name.LocalName == "inheritdoc")
                {
                    continue;
                }

                string? cref = node.Attribute("cref")?.Value;

                if (cref is null || IsVisibleApi(cref, types, unknownIsVisible: true))
                {
                    continue;
                }

                offenders.Add($"{documented}  <{node.Name.LocalName} cref=\"{cref}\" />");
            }
        }

        offenders.Order(StringComparer.Ordinal).Should().BeEmpty(
            "documentation on a member a consumer can see is documentation a consumer reads, and it must "
            + "not send them to a type they cannot name. The compiler accepts these because the target "
            + "resolves inside the assembly:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders.Order(StringComparer.Ordinal).Select(x => "    " + x)));
    }

    /// <summary>
    /// The shipped assemblies this project can reach, each with its generated XML documentation file.
    /// </summary>
    /// <remarks>
    /// <c>Quartz.AspNetCore</c> and <c>Quartz.Dashboard</c> are not here: only
    /// <c>Quartz.Tests.AspNetCore</c> can reference them, for the reason issue #3532 gives.
    /// </remarks>
    private static IEnumerable<TestCaseData> ShippedAssemblies()
    {
        Assembly[] assemblies =
        [
            typeof(IScheduler).Assembly,
            typeof(HttpScheduler).Assembly,
            typeof(Quartz.Jobs.NoOpJob).Assembly,
            typeof(Quartz.Plugins.Xml.XmlSchedulingDataProcessorPlugin).Assembly,
            typeof(NewtonsoftJsonSerializerRegistry).Assembly,
        ];

        foreach (Assembly assembly in assemblies)
        {
            yield return new TestCaseData(assembly).SetName(assembly.GetName().Name);
        }
    }

    /// <summary>
    /// The documentation elements that must say something where they appear at all.
    /// </summary>
    /// <remarks>
    /// <c>&lt;example&gt;</c>, <c>&lt;exception&gt;</c> and <c>&lt;seealso&gt;</c> are not here: the
    /// first two are rare enough to read by eye, and a <c>&lt;seealso cref="…" /&gt;</c> is meant to
    /// carry nothing but its target.
    /// </remarks>
    private static readonly string[] ElementsThatMustSaySomething =
        ["summary", "returns", "param", "typeparam", "remarks", "value"];

    /// <summary>
    /// A documentation element a consumer can see says something.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CS1591 says a public member has a comment; nothing says the comment has words in it. An empty
    /// <c>&lt;summary /&gt;</c> is a member with no description at all, and it reads in an IDE exactly
    /// as one with no comment does — except that CS1591 is satisfied, so nothing ever mentions it again.
    /// An empty <c>&lt;returns /&gt;</c> is worse than absent: it renders as an empty "Returns" heading
    /// in an IDE and in a generated API reference, so it takes space and promises a sentence that is not
    /// there.
    /// </para>
    /// <para>
    /// The fix for an empty one is usually deletion rather than prose — a <c>&lt;returns /&gt;</c> beside
    /// a summary that already says what the member returns adds nothing. <c>&lt;param&gt;</c> is the
    /// exception: CS1573 fires the moment a member documents some of its parameters and not others, so
    /// there the choice is all of them or none.
    /// </para>
    /// <para>
    /// An element holding only an <c>&lt;inheritdoc /&gt;</c> has no text of its own and is not meant
    /// to, which is why the check is "has an element or has text" rather than "has text".
    /// </para>
    /// </remarks>
    [TestCaseSource(nameof(ShippedAssemblies))]
    public void NoVisibleMemberCarriesAnEmptyDocumentationElement(Assembly assembly)
    {
        string documentation = Path.ChangeExtension(assembly.Location, ".xml");

        File.Exists(documentation).Should().BeTrue(
            $"{assembly.GetName().Name} ships its XML documentation, so the file is beside the assembly");

        Dictionary<string, Type> types = assembly.GetTypes()
            .Where(x => x.FullName is not null)
            .GroupBy(x => x.FullName!.Replace('+', '.'), StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);

        List<string> offenders = [];

        foreach (XElement member in XDocument.Load(documentation).Descendants("member"))
        {
            string documented = member.Attribute("name")?.Value ?? "";

            if (!IsVisibleApi(documented, types))
            {
                continue;
            }

            foreach (XElement node in member.Descendants())
            {
                if (!ElementsThatMustSaySomething.Contains(node.Name.LocalName, StringComparer.Ordinal))
                {
                    continue;
                }

                if (node.Elements().Any() || !string.IsNullOrWhiteSpace(node.Value))
                {
                    continue;
                }

                string named = node.Attribute("name")?.Value is { } name ? $" name=\"{name}\"" : "";
                offenders.Add($"{documented}  <{node.Name.LocalName}{named} />");
            }
        }

        offenders.Order(StringComparer.Ordinal).Should().BeEmpty(
            "an empty documentation element promises a sentence that is not there: an empty <summary /> "
            + "reads in an IDE exactly as no comment does while satisfying CS1591, and an empty "
            + "<returns /> renders as an empty heading. Delete it, or write it — and for <param />, "
            + "either document every parameter or none, because CS1573 refuses a mixture. These say "
            + "nothing:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders.Order(StringComparer.Ordinal).Select(x => "    " + x)));
    }

    /// <summary>
    /// Whether a documentation comment id names something visible outside the assembly.
    /// </summary>
    /// <param name="unknownIsVisible">
    /// What to answer for an id this assembly does not declare. A <c>cref</c> can point into the BCL or
    /// into a package, and those resolved at compile time, so they are somebody's public API;
    /// a documented member always belongs to the assembly the file documents.
    /// </param>
    private static bool IsVisibleApi(string id, Dictionary<string, Type> types, bool unknownIsVisible = false)
    {
        if (id.Length < 2 || id[1] != ':')
        {
            return unknownIsVisible;
        }

        char kind = id[0];
        string path = id[2..];

        int arguments = path.IndexOf('(', StringComparison.Ordinal);
        if (arguments >= 0)
        {
            path = path[..arguments];
        }

        if (kind == 'T')
        {
            return types.TryGetValue(path, out Type? declared) ? IsVisible(declared) : unknownIsVisible;
        }

        // A generic method carries its arity as ``N, which is no part of the declaring type's name.
        int arity = path.IndexOf("``", StringComparison.Ordinal);
        if (arity >= 0)
        {
            path = path[..arity];
        }

        int lastDot = path.LastIndexOf('.');
        if (lastDot < 0)
        {
            return unknownIsVisible;
        }

        string owner = path[..lastDot];
        string name = path[(lastDot + 1)..];

        if (!types.TryGetValue(owner, out Type? type))
        {
            return unknownIsVisible;
        }

        if (!IsVisible(type))
        {
            return false;
        }

        // Any visible member of that name is enough: the id says which overload, but visibility is a
        // property of the name here — an internal overload beside a public one is still documentation a
        // consumer can reach.
        return type
            .GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(x => string.Equals(x.Name, name == "#ctor" ? ".ctor" : name, StringComparison.Ordinal))
            .Any(IsVisible);
    }

    private static bool IsVisible(Type type)
    {
        while (type.IsNested)
        {
            if (!type.IsNestedPublic && !type.IsNestedFamily && !type.IsNestedFamORAssem)
            {
                return false;
            }

            type = type.DeclaringType!;
        }

        return type.IsPublic;
    }

    private static bool IsVisible(MemberInfo member)
    {
        return member switch
        {
            Type nested => IsVisible(nested),
            MethodBase method => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly,
            FieldInfo field => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly,
            PropertyInfo property => property.GetAccessors(nonPublic: true).Any(IsVisible),
            EventInfo declared => new MethodInfo?[] { declared.AddMethod, declared.RemoveMethod }.Any(x => x is not null && IsVisible(x)),
            _ => false,
        };
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
