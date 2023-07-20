using System.Text.Json;

using Quartz.HttpClient;
using Quartz.Spi;

namespace Quartz.Simpl;

/// <summary>
/// A <see cref="IRemotableSchedulerProxyFactory" /> implementation that creates
/// connection to remote scheduler using HTTP.
/// </summary>
public class HttpSchedulerProxyFactory : IRemotableSchedulerProxyFactory
{
    /// <summary>
    /// Gets or sets the remote scheduler address.
    /// </summary>
    /// <value>The remote scheduler address.</value>
    public string? Address { private get; set; }

    /// <summary>
    /// Returns a client proxy to a remote <see cref="IScheduler" />.
    /// </summary>
    public IScheduler GetProxy(string schedulerName, string schedulerInstanceId)
    {
        if (string.IsNullOrWhiteSpace(Address))
        {
            ThrowHelper.ThrowInvalidOperationException("Address hasn't been configured");
        }

        var scheduler = new HttpScheduler(schedulerName, CreateHttpClient(Address!), CreateJsonSerializerOptions());
        return scheduler;
    }

    protected virtual System.Net.Http.HttpClient CreateHttpClient(string address)
    {
        return new System.Net.Http.HttpClient
        {
            BaseAddress = new Uri(address)
        };
    }

    protected virtual JsonSerializerOptions? CreateJsonSerializerOptions() => null;
}