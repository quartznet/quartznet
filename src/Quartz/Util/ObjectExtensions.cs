using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace Quartz.Util
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

            BinaryFormatter bf = new BinaryFormatter();
            using (MemoryStream ms = new MemoryStream())
            {
                bf.Serialize(ms, obj);
                ms.Seek(0, SeekOrigin.Begin);
                return (T) bf.Deserialize(ms);
            }
        }
    }
}