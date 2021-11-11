namespace Quartz.HttpClient;

public class HttpClientException : SchedulerException
{
    public HttpClientException(string message) : base(message)
    {
    }
}