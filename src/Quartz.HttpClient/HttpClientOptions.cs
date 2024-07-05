using System.Text.Json;

namespace Quartz;

public class HttpClientOptions
{
    /// <summary>
    /// Name of the scheduler, must be same as the remote scheduler.
    /// </summary>
    public string SchedulerName { get; set; } = null!;

    /// <summary>
    /// If given, IHttpClientFactory is used to fetch HttpClient with this name.
    /// </summary>
    /// <remarks>
    /// Either this or HttpClient must be given
    /// </remarks>
    public string? HttpClientName { get; set; }

    /// <summary>
    /// If given this HttpClient will be used
    /// </summary>
    /// <remarks>
    /// Either this or HttpClientName must be given
    /// </remarks>
    public System.Net.Http.HttpClient? HttpClient { get; set; }

    /// <summary>
    /// Optional json serializer options to be used by the HTTP scheduler
    /// </summary>
    public JsonSerializerOptions? JsonSerializerOptions { get; set; }

    internal void AssertValid()
    {
        if (string.IsNullOrWhiteSpace(SchedulerName))
        {
            throw new InvalidOperationException("Scheduler name required");
        }

        if (string.IsNullOrWhiteSpace(HttpClientName) && HttpClient is null)
        {
            throw new InvalidOperationException($"Either {nameof(HttpClientName)} or {nameof(HttpClient)} instance is required");
        }

        if (!string.IsNullOrWhiteSpace(HttpClientName) && HttpClient is not null)
        {
            throw new InvalidOperationException($"Both {nameof(HttpClientName)} and {nameof(HttpClient)} instance have been set, only one can be set");
        }
    }
}