using System.Runtime.Serialization;

namespace Quartz.Util;

internal static class SerializationInfoExtensions
{
    public static T? GetValue<T>(this SerializationInfo info, string name) => (T?) info.GetValue(name, typeof(T));
}