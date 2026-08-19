using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Quartz.Util;

/// <summary>
/// Produces the type name Quartz stores and sends over the wire: assembly-qualified, but without the
/// version, culture and public key token, so a payload written by one build still binds after an
/// assembly version bump.
/// </summary>
internal static partial class ObjectExtensions
{
    private static readonly ConcurrentDictionary<Type, string> assemblyQualifiedNameCache = new();

    [GeneratedRegex(", (Version|Culture|PublicKeyToken)=[0-9.\\w]+", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 5000)]
    private static partial Regex Cleanup();

    public static string AssemblyQualifiedNameWithoutVersion(this Type type)
        => assemblyQualifiedNameCache.GetOrAdd(type, x => $"{GetTypeString(x)}, {x.Assembly.GetName().Name}");

    private static string? GetTypeString(Type type)
        => type.IsGenericType
            ? GenericTypeString(type.FullName)
            : type.FullName;

    private static string? GenericTypeString(string? name)
        => string.IsNullOrEmpty(name)
            ? null
            : Cleanup().Replace(name, "");
}