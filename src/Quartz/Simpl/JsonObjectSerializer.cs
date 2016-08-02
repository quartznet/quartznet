using System.IO;

using Newtonsoft.Json;

using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Simpl
{
    /// <summary>
    /// Default object serialization strategy that uses <see cref="JsonSerializer" /> 
    /// under the hood.
    /// </summary>
    /// <author>Marko Lahma</author>
    public class JsonObjectSerializer : IObjectSerializer
    {
        /// <summary>
        /// Serializes given object as bytes 
        /// that can be stored to permanent stores.
        /// </summary>
        /// <param name="obj">Object to serialize.</param>
        public byte[] Serialize<T>(T obj) where T : class
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (var sw = new StreamWriter(ms))
                {
                    var js = new JsonSerializer
                    {
                        TypeNameHandling = TypeNameHandling.Auto,
                        ContractResolver = new WritablePropertiesOnlyResolver()
                    };
                    js.Serialize(sw, obj, typeof(object));
                }
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Deserializes object from byte array presentation.
        /// </summary>
        /// <param name="data">Data to deserialize object from.</param>
        public T DeSerialize<T>(byte[] data) where T : class
        {
            using (MemoryStream ms = new MemoryStream(data))
            {
                using (var sr = new StreamReader(ms))
                {
                    var js = new JsonSerializer
                    {
                        TypeNameHandling = TypeNameHandling.Auto
                    };
                    return (T) js.Deserialize(sr, typeof(T));
                }
            }
        }
    }
}