using System.Collections.Concurrent;
using System.Reflection;

namespace Quartz.Util
{
    /// <summary>
    /// A static PropertyInfo cache for generic classes, which have static members per generic parameter
    /// </summary>
    public static class TypePropertyCache
    {
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> propertyCache = new();

        public static PropertyInfo[] Get(Type type) => propertyCache.GetOrAdd(type, t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance));
        public static bool ContainsKey(Type type) => propertyCache.ContainsKey(type);
    }
}