#nullable enable

using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Quartz.Tests;

/// <summary>
/// Adds to a <c>PublicApiGenerator</c> rendering the contract facts it does not express, so that the
/// public API baselines fail on a change that breaks a caller without changing a shape.
/// </summary>
/// <remarks>
/// Three kinds of break used to pass the baselines in silence: turning a default interface member
/// into an abstract one, turning a record into a class, and removing an explicit interface
/// implementation. The generator's own <c>TreatRecordsAsClasses</c> option covers record classes,
/// which it recognises by the <c>&lt;Clone&gt;$</c> member the compiler gives them; a record
/// <em>struct</em> has no such member, so it still renders as a plain struct. This type supplies the
/// rest: it marks every interface member that carries a default implementation, writes the
/// <c>record</c> keyword onto record structs, and lists the explicit interface implementations the
/// generator omits entirely.
/// </remarks>
/// <remarks>
/// The work is done on the rendered text rather than in the generator, which offers no hook for it.
/// Every annotation is checked against the type it came from: if a member the annotator knows to be
/// a default implementation is not found in the rendering exactly once, <see cref="Annotate" />
/// throws instead of quietly leaving it unmarked. A silent annotator would be worth nothing, since
/// the whole point is that an unmarked member is a promise.
/// </remarks>
internal static partial class PublicApiRendering
{
    /// <summary>
    /// Marks an interface member an implementor may leave unwritten. Its removal from a line is a
    /// source break for every implementor of that interface.
    /// </summary>
    private const string DefaultImplementationMarker = " // default implementation";

    /// <summary>
    /// Introduces one explicit interface implementation of the type it is written into. They are
    /// callable through the interface, so removing one is a break, and the generator renders none of
    /// them because they are private in metadata. The interface is spelled the way metadata spells
    /// it, since that is where the name comes from; the parameters are spelled the way the rest of
    /// the rendering spells them.
    /// </summary>
    private const string ExplicitImplementationPrefix = "// explicit interface implementation: ";

    private const int IndentWidth = 4;

    [GeneratedRegex(
        @"^ *(?:public|protected internal|protected|internal) (?:[a-z]+ )*(?:class|struct|interface|enum|record) (?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant)]
    private static partial Regex TypeDeclaration();

    private static readonly Dictionary<Type, string> keywords = new()
    {
        [typeof(void)] = "void",
        [typeof(bool)] = "bool",
        [typeof(byte)] = "byte",
        [typeof(sbyte)] = "sbyte",
        [typeof(char)] = "char",
        [typeof(short)] = "short",
        [typeof(ushort)] = "ushort",
        [typeof(int)] = "int",
        [typeof(uint)] = "uint",
        [typeof(long)] = "long",
        [typeof(ulong)] = "ulong",
        [typeof(float)] = "float",
        [typeof(double)] = "double",
        [typeof(decimal)] = "decimal",
        [typeof(string)] = "string",
        [typeof(object)] = "object",
    };

    /// <summary>
    /// Annotates <paramref name="publicApi" />, the rendering of <paramref name="assembly" />.
    /// </summary>
    public static string Annotate(string publicApi, Assembly assembly)
    {
        Dictionary<string, Type> types = IndexTypes(assembly);

        string[] lines = publicApi.Split('\n');
        StringBuilder result = new StringBuilder(publicApi.Length + 1024);

        List<TypeScope> scopes = [];
        string currentNamespace = "";
        PendingDeclaration? pending = null;

        for (int index = 0; index < lines.Length; index++)
        {
            string rawLine = lines[index];
            string line = rawLine.TrimEnd('\r');
            string lineEnding = rawLine.Length == line.Length ? "" : "\r";
            int indent = IndentOf(line);
            string trimmed = line.Trim();

            // A generic type puts its constraints on the line between its declaration and its brace,
            // so the declaration is remembered until the brace arrives rather than until the next
            // line.
            if (trimmed == "{" && pending is not null)
            {
                result.Append(line).Append(lineEnding).Append('\n');

                TypeScope scope = OpenScope(pending.Value, indent + IndentWidth);
                scopes.Add(scope);
                pending = null;

                foreach (string implementation in scope.ExplicitImplementations)
                {
                    result.Append(new string(' ', scope.MemberIndent))
                        .Append(ExplicitImplementationPrefix)
                        .Append(implementation)
                        .Append(lineEnding)
                        .Append('\n');
                }

                continue;
            }

            if (trimmed == "}")
            {
                if (indent == 0)
                {
                    currentNamespace = "";
                }
                else if (scopes.Count > 0 && scopes[^1].MemberIndent == indent + IndentWidth)
                {
                    scopes[^1].VerifyEverythingWasMarked();
                    scopes.RemoveAt(scopes.Count - 1);
                }

                result.Append(line).Append(lineEnding).Append('\n');
                continue;
            }

            if (indent == 0 && trimmed.StartsWith("namespace ", StringComparison.Ordinal))
            {
                currentNamespace = trimmed["namespace ".Length..].Trim();
                result.Append(line).Append(lineEnding).Append('\n');
                continue;
            }

            Match declaration = TypeDeclaration().Match(line);
            if (declaration.Success && indent == ExpectedDeclarationIndent(scopes))
            {
                string metadataName = MetadataName(line, declaration);
                string key = TypeKey(currentNamespace, scopes.Select(static scope => scope.MetadataName), metadataName);
                if (!types.TryGetValue(key, out Type? declared))
                {
                    throw new InvalidOperationException(
                        $"The rendered API declares {key.Replace(':', '.')}, which {nameof(PublicApiRendering)} "
                        + "could not find in the assembly it is annotating. Its reading of a type "
                        + $"declaration and the rendering have drifted apart: '{trimmed}'.");
                }

                pending = new PendingDeclaration(declared, metadataName);

                if (declared.IsValueType && IsRecord(declared))
                {
                    line = line.Replace(" struct ", " record struct ", StringComparison.Ordinal);
                }

                result.Append(line).Append(lineEnding).Append('\n');
                continue;
            }

            if (scopes.Count > 0 && indent == scopes[^1].MemberIndent)
            {
                // A generic member's constraints land on their own lines too, and the marker belongs
                // on the last of them, where it cannot be read as ending the declaration early.
                while (!trimmed.EndsWith(';') && index + 1 < lines.Length
                    && lines[index + 1].TrimStart().StartsWith("where ", StringComparison.Ordinal))
                {
                    result.Append(line).Append(lineEnding).Append('\n');
                    index++;
                    rawLine = lines[index];
                    line = rawLine.TrimEnd('\r');
                    lineEnding = rawLine.Length == line.Length ? "" : "\r";
                }

                line = scopes[^1].Mark(line, trimmed);
            }

            result.Append(line).Append(lineEnding).Append('\n');
        }

        // Split() produced one entry past the last newline; putting a newline after every entry adds
        // one the input did not have.
        result.Length -= 1;
        return result.ToString();
    }

    private static int ExpectedDeclarationIndent(List<TypeScope> scopes)
    {
        return scopes.Count == 0 ? IndentWidth : scopes[^1].MemberIndent;
    }

    private static int IndentOf(string line)
    {
        int indent = 0;
        while (indent < line.Length && line[indent] == ' ')
        {
            indent++;
        }

        return indent;
    }

    private static TypeScope OpenScope(PendingDeclaration pending, int memberIndent)
    {
        return new TypeScope(
            pending.MetadataName,
            memberIndent,
            pending.Type is null ? [] : DefaultImplementations(pending.Type),
            pending.Type is null ? [] : ExplicitImplementations(pending.Type));
    }

    /// <summary>
    /// The name a declaration line has in metadata, which for a generic type carries its arity. The
    /// type parameter list has to be read by hand: the base list that follows it is full of angle
    /// brackets and commas of its own, so a regular expression cannot tell where it ends.
    /// </summary>
    private static string MetadataName(string line, Match declaration)
    {
        Group name = declaration.Groups["name"];
        int start = name.Index + name.Length;
        if (start >= line.Length || line[start] != '<')
        {
            return name.Value;
        }

        int arity = 1;
        int depth = 0;
        for (int i = start; i < line.Length; i++)
        {
            if (line[i] == '<')
            {
                depth++;
            }
            else if (line[i] == '>')
            {
                depth--;
                if (depth == 0)
                {
                    break;
                }
            }
            else if (line[i] == ',' && depth == 1)
            {
                arity++;
            }
        }

        return name.Value + "`" + arity.ToString(CultureInfo.InvariantCulture);
    }

    private static Dictionary<string, Type> IndexTypes(Assembly assembly)
    {
        Dictionary<string, Type> types = new(StringComparer.Ordinal);
        foreach (Type type in assembly.GetTypes())
        {
            List<string> path = [];
            for (Type? current = type; current is not null; current = current.DeclaringType)
            {
                path.Insert(0, current.Name);
            }

            string key = TypeKey(type.Namespace ?? "", path[..^1], path[^1]);
            types[key] = type;
        }

        return types;
    }

    private static string TypeKey(string containingNamespace, IEnumerable<string> declaringTypes, string name)
    {
        return containingNamespace + ":" + string.Join("+", declaringTypes.Append(name));
    }

    /// <summary>
    /// The interface members with a body, keyed the way <see cref="TypeScope.Mark" /> reads a
    /// rendered line. Only public instance members can appear in the rendering.
    /// </summary>
    private static Dictionary<string, string> DefaultImplementations(Type type)
    {
        Dictionary<string, string> members = new(StringComparer.Ordinal);
        if (!type.IsInterface)
        {
            return members;
        }

        const BindingFlags declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        foreach (MethodInfo method in type.GetMethods(declared))
        {
            if (method.IsAbstract || method.IsSpecialName)
            {
                continue;
            }

            members[MethodKey(method.Name, method.GetParameters())] = Signature(method.Name, method.GetParameters());
        }

        foreach (PropertyInfo property in type.GetProperties(declared))
        {
            if (property.GetAccessors(nonPublic: true).All(static accessor => accessor.IsAbstract))
            {
                continue;
            }

            members["p:" + property.Name] = property.Name;
        }

        foreach (EventInfo declaredEvent in type.GetEvents(declared))
        {
            if (declaredEvent.AddMethod is null || declaredEvent.AddMethod.IsAbstract)
            {
                continue;
            }

            members["p:" + declaredEvent.Name] = declaredEvent.Name;
        }

        return members;
    }

    /// <summary>
    /// The explicit interface implementations of <paramref name="type" />, rendered so that a change
    /// to one of their signatures moves the line.
    /// </summary>
    private static List<string> ExplicitImplementations(Type type)
    {
        if (type.IsInterface)
        {
            return [];
        }

        const BindingFlags declared = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        List<string> implementations = [];

        foreach (PropertyInfo property in type.GetProperties(declared))
        {
            if (IsExplicit(property.Name))
            {
                implementations.Add(property.Name);
            }
        }

        foreach (EventInfo declaredEvent in type.GetEvents(declared))
        {
            if (IsExplicit(declaredEvent.Name))
            {
                implementations.Add(declaredEvent.Name);
            }
        }

        foreach (MethodInfo method in type.GetMethods(declared))
        {
            if (IsExplicit(method.Name) && !method.IsSpecialName)
            {
                implementations.Add(Signature(method.Name, method.GetParameters()));
            }
        }

        implementations.Sort(StringComparer.Ordinal);
        return implementations;

        static bool IsExplicit(string name) => name.Contains('.', StringComparison.Ordinal);
    }

    private static bool IsRecord(Type type)
    {
        const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        return type.GetMethods(any).Any(static method =>
            method.Name == "PrintMembers"
            && method.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false));
    }

    private static string MethodKey(string name, ParameterInfo[] parameters)
    {
        return "m:" + name + "(" + string.Join(",", parameters.Select(static parameter => parameter.Name)) + ")";
    }

    private static string Signature(string name, ParameterInfo[] parameters)
    {
        return name + "(" + string.Join(", ", parameters.Select(static parameter => Format(parameter.ParameterType))) + ")";
    }

    private static string Format(Type type)
    {
        if (type.IsByRef)
        {
            return "ref " + Format(type.GetElementType()!);
        }

        if (type.IsArray)
        {
            return Format(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        }

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return Format(underlying) + "?";
        }

        if (keywords.TryGetValue(type, out string? keyword))
        {
            return keyword;
        }

        if (type.IsGenericType)
        {
            string definition = type.GetGenericTypeDefinition().FullName ?? type.Name;
            string withoutArity = definition[..definition.IndexOf('`', StringComparison.Ordinal)];
            return withoutArity + "<" + string.Join(", ", type.GetGenericArguments().Select(Format)) + ">";
        }

        return type.FullName ?? type.Name;
    }

    private readonly record struct PendingDeclaration(Type? Type, string MetadataName);

    private sealed class TypeScope(
        string metadataName,
        int memberIndent,
        Dictionary<string, string> defaultImplementations,
        List<string> explicitImplementations)
    {
        private readonly HashSet<string> marked = new(StringComparer.Ordinal);

        public int MemberIndent { get; } = memberIndent;

        public string MetadataName { get; } = metadataName;

        public List<string> ExplicitImplementations { get; } = explicitImplementations;

        /// <summary>
        /// Appends the marker to <paramref name="line" /> when <paramref name="declaration" />
        /// renders a member with a default implementation, and returns the line unchanged otherwise.
        /// The two differ only for a member whose type constraints run onto further lines: the
        /// declaration is read from the first, and the marker written onto the last.
        /// </summary>
        public string Mark(string line, string declaration)
        {
            if (defaultImplementations.Count == 0)
            {
                return line;
            }

            string? key = KeyOf(declaration);
            if (key is null || !defaultImplementations.ContainsKey(key))
            {
                return line;
            }

            marked.Add(key);
            return line + DefaultImplementationMarker;
        }

        /// <summary>
        /// Fails when a member known to carry a default implementation was not found in the
        /// rendering, which means the reader below and the rendering have drifted apart and the
        /// baseline is no longer saying what it claims to say.
        /// </summary>
        public void VerifyEverythingWasMarked()
        {
            List<string> missed = defaultImplementations
                .Where(member => !marked.Contains(member.Key))
                .Select(static member => member.Value)
                .ToList();

            if (missed.Count > 0)
            {
                throw new InvalidOperationException(
                    $"{MetadataName} has {missed.Count} member(s) with a default implementation that "
                    + $"{nameof(PublicApiRendering)} could not find in the rendered API: "
                    + string.Join(", ", missed)
                    + ". Teach it to read the shape those members render as, or the baseline will "
                    + "show them as abstract.");
            }
        }

        /// <summary>
        /// Reads back from a rendered member line the key <see cref="DefaultImplementations" />
        /// files members under: the member name, plus for a method its parameter names, which is
        /// what separates one overload from another in a rendering that names every parameter.
        /// </summary>
        private static string? KeyOf(string line)
        {
            string trimmed = line.Trim();

            // A member whose constraints continue on the next line ends where its parameters do.
            if (trimmed.EndsWith(')'))
            {
                trimmed += ";";
            }

            int brace = trimmed.IndexOf('{', StringComparison.Ordinal);
            if (brace > 0)
            {
                string beforeBrace = trimmed[..brace].TrimEnd();
                int space = beforeBrace.LastIndexOf(' ');
                return space < 0 ? null : "p:" + beforeBrace[(space + 1)..];
            }

            if (!trimmed.EndsWith(");", StringComparison.Ordinal))
            {
                return null;
            }

            int open = OpenParenthesisOf(trimmed);
            if (open < 0)
            {
                return null;
            }

            string declarator = trimmed[..open].TrimEnd();

            // The return type is generic far more often than the method is, so the name ends where
            // the type argument list the compiler wrote *last* begins, not where the first '<' is.
            int nameEnd = declarator.EndsWith('>') ? OpeningAngleBracketOf(declarator) : declarator.Length;
            if (nameEnd <= 0)
            {
                return null;
            }

            string name = declarator[(declarator.LastIndexOf(' ', nameEnd - 1) + 1)..nameEnd];
            string parameters = trimmed[(open + 1)..^2];
            return "m:" + name + "(" + string.Join(",", SplitParameters(parameters).Select(ParameterName)) + ")";
        }

        private static int OpeningAngleBracketOf(string declarator)
        {
            int depth = 0;
            for (int i = declarator.Length - 1; i >= 0; i--)
            {
                if (declarator[i] == '>')
                {
                    depth++;
                }
                else if (declarator[i] == '<')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private static int OpenParenthesisOf(string trimmed)
        {
            int depth = 0;
            for (int i = trimmed.Length - 2; i >= 0; i--)
            {
                if (trimmed[i] == ')')
                {
                    depth++;
                }
                else if (trimmed[i] == '(')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private static List<string> SplitParameters(string parameters)
        {
            List<string> split = [];
            if (parameters.Length == 0)
            {
                return split;
            }

            int depth = 0;
            bool inString = false;
            int start = 0;
            for (int i = 0; i < parameters.Length; i++)
            {
                char character = parameters[i];
                if (character == '"')
                {
                    inString = !inString;
                }
                else if (inString)
                {
                    continue;
                }
                else if (character is '<' or '(' or '[')
                {
                    depth++;
                }
                else if (character is '>' or ')' or ']')
                {
                    depth--;
                }
                else if (character == ',' && depth == 0)
                {
                    split.Add(parameters[start..i]);
                    start = i + 1;
                }
            }

            split.Add(parameters[start..]);
            return split;
        }

        private static string ParameterName(string parameter)
        {
            string declarator = parameter;
            int assignment = declarator.IndexOf('=', StringComparison.Ordinal);
            if (assignment >= 0)
            {
                declarator = declarator[..assignment];
            }

            declarator = declarator.TrimEnd();
            int space = declarator.LastIndexOf(' ');
            return space < 0 ? declarator : declarator[(space + 1)..];
        }
    }
}
