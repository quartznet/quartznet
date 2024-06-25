using System.Runtime.Serialization;

namespace Quartz;

[Serializable]
public sealed class JsonSerializationException : SchedulerException
{
    public JsonSerializationException(string message) : base(message)
    {
    }

    public JsonSerializationException(string message, Exception? innerException) : base(message, innerException)
    {
    }

    private JsonSerializationException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}