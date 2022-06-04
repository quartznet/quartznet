using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Quartz.Util
{
    /// <summary>
    /// Generic extension methods for objects.
    /// </summary>
    public static class ObjectExtensions
    {
        private static readonly ConcurrentDictionary<Type, string> assemblyQualifiedNameCache = new ConcurrentDictionary<Type, string>();
        private static readonly Regex cleanup = new Regex(", (Version|Culture|PublicKeyToken)=[0-9.\\w]+", RegexOptions.Compiled);
        
        public static string AssemblyQualifiedNameWithoutVersion(this Type type)
            => assemblyQualifiedNameCache.GetOrAdd(type, x => GetTypeString(x) + ", " + x.Assembly.GetName().Name);

        public static string? GetTypeString(Type type)
            => type.IsGenericType
                ? GenericTypeString(type.FullName)
                : type.FullName;
        
        public static string? GenericTypeString(string? name)
            => string.IsNullOrEmpty(name)
                ? null
                : cleanup.Replace(name, "");
    }
}