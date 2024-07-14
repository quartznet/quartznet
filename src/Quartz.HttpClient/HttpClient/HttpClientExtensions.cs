using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Quartz.HttpApiContract;
using Quartz.Impl.AdoJobStore;

namespace Quartz.HttpClient;

internal static class HttpClientExtensions
{
    public static async ValueTask<TResponse> Get<TResponse>(
        this System.Net.Http.HttpClient client,
        string requestUri,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        await response.CheckResponseStatusCode(serializerOptions, cancellationToken).ConfigureAwait(false);

        return await response.Content.ReadOrThrow<TResponse>(serializerOptions, cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<TResponse?> GetWithNullForNotFound<TResponse>(
        this System.Net.Http.HttpClient client,
        string requestUri,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken) where TResponse : class
    {
        using var response = await client.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        var okResponse = await response.CheckResponseStatusCode(serializerOptions, cancellationToken, throwOnNotFound: false).ConfigureAwait(false);
        if (!okResponse)
        {
            return null;
        }

        return await response.Content.ReadOrThrow<TResponse>(serializerOptions, cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask Post(
        this System.Net.Http.HttpClient client,
        string requestUri,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken)
    {
        var response = await client.PostAsync(requestUri, content: null!, cancellationToken).ConfigureAwait(false);
        await response.CheckResponseStatusCode(serializerOptions, cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask Post<TRequest>(
        this System.Net.Http.HttpClient client,
        string requestUri,
        TRequest value,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync(requestUri, value, serializerOptions, cancellationToken).ConfigureAwait(false);
        await response.CheckResponseStatusCode(serializerOptions, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<TResponse> PostWithResponse<TResponse>(
        this System.Net.Http.HttpClient client,
        string requestUri,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken)
    {
        var response = await client.PostAsync(requestUri, content: null!, cancellationToken).ConfigureAwait(false);
        await response.CheckResponseStatusCode(serializerOptions, cancellationToken).ConfigureAwait(false);

        return await response.Content.ReadOrThrow<TResponse>(serializerOptions, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<TResponse> PostWithResponse<TRequest, TResponse>(
        this System.Net.Http.HttpClient client,
        string requestUri,
        TRequest value,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync(requestUri, value, serializerOptions, cancellationToken).ConfigureAwait(false);
        await response.CheckResponseStatusCode(serializerOptions, cancellationToken).ConfigureAwait(false);

        return await response.Content.ReadOrThrow<TResponse>(serializerOptions, cancellationToken).ConfigureAwait(false);
    }

    public static async Task Delete(
        this System.Net.Http.HttpClient client,
        string requestUri,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken)
    {
        var response = await client.DeleteAsync(requestUri, cancellationToken).ConfigureAwait(false);
        await response.CheckResponseStatusCode(serializerOptions, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<TResponse> DeleteWithResponse<TResponse>(
        this System.Net.Http.HttpClient client,
        string requestUri,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken)
    {
        var response = await client.DeleteAsync(requestUri, cancellationToken).ConfigureAwait(false);
        await response.CheckResponseStatusCode(serializerOptions, cancellationToken).ConfigureAwait(false);

        return await response.Content.ReadOrThrow<TResponse>(serializerOptions, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<bool> CheckResponseStatusCode(
        this HttpResponseMessage response,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken,
        bool throwOnNotFound = true)
    {
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        ProblemDetails? problemDetails = null;

        try
        {
            problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>(serializerOptions, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Ignored because we can have responses which are not json
        }

        if (problemDetails?.Detail is null || string.IsNullOrWhiteSpace(problemDetails.Detail))
        {
            // When Web API returns error response it is always problem details, so let HTTP client throw if we do not have problem details
            response.EnsureSuccessStatusCode();
            return false;
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // If scheduler is not found, then no requests will succeed, so lets throw even if throwOnNotFound is true.
            // Could probably add separate flag for this in problem details...
            if (problemDetails.Detail.Contains("Unknown scheduler", StringComparison.OrdinalIgnoreCase))
            {
                throw new HttpClientException($"Scheduler not found. {nameof(HttpScheduler)} might have been configured with wrong scheduler name.");
            }

            if (throwOnNotFound)
            {
                throw new HttpClientException($"Received response with not found status code: {problemDetails.Detail}");
            }

            return false;
        }

        // If scheduler throws exception, Web API will return bad request with exception type
        if (response.StatusCode == HttpStatusCode.BadRequest && (problemDetails.Extensions?.ContainsKey(HttpApiConstants.ProblemDetailsExceptionType) ?? false))
        {
            // Might not be the best way to do this...
            var quartzExceptionName = problemDetails.Extensions[HttpApiConstants.ProblemDetailsExceptionType].GetString();
            throw quartzExceptionName switch
            {
                nameof(SchedulerException) => new SchedulerException(problemDetails.Detail),
                nameof(InvalidConfigurationException) => new InvalidConfigurationException(problemDetails.Detail),
                nameof(JobExecutionException) => new JobExecutionException(problemDetails.Detail),
                nameof(JobPersistenceException) => new JobPersistenceException(problemDetails.Detail),
                nameof(SchedulerConfigException) => new SchedulerConfigException(problemDetails.Detail),
                nameof(UnableToInterruptJobException) => new UnableToInterruptJobException(problemDetails.Detail),
                nameof(LockException) => new LockException(problemDetails.Detail),
                nameof(NoSuchDelegateException) => new NoSuchDelegateException(problemDetails.Detail),
                nameof(ObjectAlreadyExistsException) => new ObjectAlreadyExistsException(problemDetails.Detail),
                _ => new HttpClientException(problemDetails.Detail)
            };
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            throw new HttpClientException($"Received response with bad request status code: {problemDetails.Detail}");
        }

        throw new HttpClientException($"Received response with status code {response.StatusCode}, error details: {problemDetails.Detail}");
    }

    private static async Task<T> ReadOrThrow<T>(this HttpContent content, JsonSerializerOptions serializerOptions, CancellationToken cancellationToken)
    {
        var result = await content.ReadFromJsonAsync<T>(serializerOptions, cancellationToken).ConfigureAwait(false);
        return result ?? throw new HttpClientException("Could not deserialize response");
    }

    // Copy & paste from: https://github.com/dotnet/aspnetcore/blob/main/src/Http/Http.Abstractions/src/ProblemDetails/ProblemDetails.cs
    private sealed class ProblemDetails
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("status")]
        public int? Status { get; set; }

        [JsonPropertyName("detail")]
        public string? Detail { get; set; }

        [JsonPropertyName("instance")]
        public string? Instance { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JsonElement>? Extensions { get; set; }
    }
}