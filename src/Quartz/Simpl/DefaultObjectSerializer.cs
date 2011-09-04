using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

using Quartz.Spi;

namespace Quartz.Simpl
{
    /// <summary>
    /// Default object serialization strategy that uses <see cref="BinaryFormatter" /> 
    /// under the hood.
    /// </summary>
    /// <author>Marko Lahma</author>
    public class DefaultObjectSerializer : IObjectSerializer
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
                BinaryFormatter bf = new BinaryFormatter();
                bf.Serialize(ms, obj);
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
                BinaryFormatter bf = new BinaryFormatter();
                return (T) bf.Deserialize(ms);
            }
        }
    }
}