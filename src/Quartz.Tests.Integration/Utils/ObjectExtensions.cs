using System.Runtime.Serialization.Formatters.Binary;

namespace Quartz.Tests.Integration.Utils;

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

        using MemoryStream ms = new MemoryStream();
        var bf = new BinaryFormatter();
#pragma warning disable 618
        bf.Serialize(ms, obj);
        ms.Seek(0, SeekOrigin.Begin);
        return (T) bf.Deserialize(ms);
#pragma warning restore 618
    }
}