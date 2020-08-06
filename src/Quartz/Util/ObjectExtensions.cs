using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace Quartz.Util
{
    /// <summary>
    /// Generic extension methods for objects.
    /// </summary>
    public static class ObjectExtensions
    {
        private static readonly ConcurrentDictionary<Type, string> assemblyQualifiedNameCache = new ConcurrentDictionary<Type, string>();

        public static string AssemblyQualifiedNameWithoutVersion(this Type type)
            => assemblyQualifiedNameCache.GetOrAdd(type, x => x.FullName + ", " + x.GetTypeInfo().Assembly.GetName().Name);
    }
}