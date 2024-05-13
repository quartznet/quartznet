using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Quartz.Util;

/// <summary>
/// Generic extension methods for objects.
/// </summary>
public static class ObjectExtensions
{
    private static readonly ConcurrentDictionary<Type, string> assemblyQualifiedNameCache = new();
    private static readonly Regex cleanup = new(", (Version|Culture|PublicKeyToken)=[0-9.\\w]+", RegexOptions.Compiled | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(5));

    public static string AssemblyQualifiedNameWithoutVersion(this Type type)
        => assemblyQualifiedNameCache.GetOrAdd(type, x => $"{GetTypeString(x)}, {x.Assembly.GetName().Name}");

    private static string? GetTypeString(Type type)
        => type.IsGenericType
            ? GenericTypeString(type.FullName)
            : type.FullName;

    private static string? GenericTypeString(string? name)
        => string.IsNullOrEmpty(name)
            ? null
            : cleanup.Replace(name, "");
}