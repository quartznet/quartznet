using System.Runtime.Serialization;

namespace Quartz;

public sealed class JsonSerializationException : SchedulerException
{
    public JsonSerializationException(string message) : base(message)
    {
    }

    public JsonSerializationException(string message, Exception? innerException) : base(message, innerException)
    {
    }

    public JsonSerializationException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}