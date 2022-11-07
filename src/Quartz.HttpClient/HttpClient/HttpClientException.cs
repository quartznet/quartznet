using System.Runtime.Serialization;

namespace Quartz.HttpClient;

public sealed class HttpClientException : SchedulerException
{
    public HttpClientException(string message) : base(message)
    {
    }

    public HttpClientException(string message, Exception? innerException) : base(message, innerException)
    {
    }

    public HttpClientException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}