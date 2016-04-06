using System;
using System.IO;
using System.Reflection;
#if BINARY_SERIALIZATION
using System.Runtime.Serialization.Formatters.Binary;
#endif // BINARY_SERIALIZATION

namespace Quartz.Util
{
    /// <summary>
    /// Generic extension methods for objects.
    /// </summary>
    public static class ObjectExtensions
    {
#if BINARY_SERIALIZATION // This is unused in Quartz.Net. If it's needed, similar functionality could be implemented with DCS
        /// <summary>
        /// Creates a deep copy of object by serializing to memory stream.
        /// </summary>
        /// <param name="obj"></param>
        public static T DeepClone<T>(this T obj) where T : class
        {
            if (obj == null)
            {
                return null;
            }

            using (MemoryStream ms = new MemoryStream())
            {
                BinaryFormatter bf = new BinaryFormatter();
                bf.Serialize(ms, obj);
                ms.Seek(0, SeekOrigin.Begin);
                return (T) bf.Deserialize(ms);
            }
        }
#endif // BINARY_SERIALIZATION

        public static string AssemblyQualifiedNameWithoutVersion(this Type type)
        {
            string retValue = type.FullName + ", " + type.GetTypeInfo().Assembly.GetName().Name;
            return retValue;
        }
    }
}