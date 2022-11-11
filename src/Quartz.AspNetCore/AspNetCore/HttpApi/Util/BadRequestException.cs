using System.Runtime.Serialization;

namespace Quartz.AspNetCore.HttpApi.Util;

internal sealed class BadRequestException : Exception
{
    public BadRequestException(string message) : base(message)
    {
    }

    public BadRequestException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public BadRequestException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}