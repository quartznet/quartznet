namespace Quartz.AspNetCore.HttpApi.Util;

internal class BadRequestException : Exception
{
    public BadRequestException(string message) : base(message)
    {
    }
}