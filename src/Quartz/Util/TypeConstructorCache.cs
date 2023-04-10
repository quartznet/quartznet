using System.Collections.Concurrent;
using System.Reflection;

namespace Quartz.Util
{
    /// <summary>
    /// A static ConstructorInfo cache for generic classes, which have static members per generic parameter
    /// </summary>
    public static class TypeConstructorCache
    {
        private static readonly ConcurrentDictionary<Type, ConstructorInfo> constructorCache = new();

        public static ConstructorInfo Get(Type type) => constructorCache.GetOrAdd(type, 
            t => t.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null)
                 ?? throw new ArgumentException($"The type {t.FullName} does not have a parameterless constructor."));
        public static bool ContainsKey(Type type) => constructorCache.ContainsKey(type);
    }
}