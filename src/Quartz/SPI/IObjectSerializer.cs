namespace Quartz.Spi
{
    /// <summary>
    /// Interface for object serializers.
    /// </summary>
    /// <author>Marko Lahma</author>
    public interface IObjectSerializer
    {
        void Initialize();

        /// <summary>
        /// Serializes given object as bytes
        /// that can be stored to permanent stores.
        /// </summary>
        /// <param name="obj">Object to serialize, always non-null.</param>
        byte[] Serialize<T>(T obj) where T : class;

        /// <summary>
        /// Deserializes object from byte array presentation.
        /// </summary>
        /// <param name="data">Data to deserialize object from, always non-null and non-empty.</param>
        T DeSerialize<T>(byte[] data) where T : class;
    }
}