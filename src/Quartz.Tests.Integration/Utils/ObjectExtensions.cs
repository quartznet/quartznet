using System.IO;
#if BINARY_SERIALIZATION
using System.Runtime.Serialization.Formatters.Binary;
#else // BINARY_SERIALIZATION
using Newtonsoft.Json;
#endif // BINARY_SERIALIZATION

namespace Quartz.Tests.Integration.Utils
{
    /// <summary>
    /// Generic extension methods for objects.
    /// </summary>
    public static class ObjectExtensions
    {
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
#if BINARY_SERIALIZATION
                BinaryFormatter bf = new BinaryFormatter();
                bf.Serialize(ms, obj);
                ms.Seek(0, SeekOrigin.Begin);
                return (T)bf.Deserialize(ms);
#else // BINARY_SERIALIZATION
                using (var sw = new StreamWriter(ms))
                {
                    var js = new JsonSerializer();
                    js.TypeNameHandling = TypeNameHandling.All;
                    js.PreserveReferencesHandling = PreserveReferencesHandling.All;
                    js.Serialize(sw, obj);
                    sw.Flush();
                    ms.Seek(0, SeekOrigin.Begin);
                    using (var sr = new StreamReader(ms))
                    {
                        return (T)js.Deserialize(sr, typeof(T));
                    }
                }
#endif // BINARY_SERIALIZATION
            }
        }
    }
}