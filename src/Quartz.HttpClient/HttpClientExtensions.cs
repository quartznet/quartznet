#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using Quartz.HttpApiContract;
using Quartz.Impl.AdoJobStore;

namespace Quartz;

internal static class HttpClientExtensions
{
    /// <summary>
    /// The metadata for one body of the wire contract, asked of the options rather than discovered by
    /// reflecting over <typeparamref name="T" />.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every body this file sends or reads is a contract type, and <c>HttpApiJsonContext</c> — which
    /// <c>ConfigureWireFormat</c> puts in front of whatever resolver the options already had — states all
    /// of them. So the answer comes from generated metadata, and passing it to
    /// <see cref="HttpClientJsonExtensions" /> binds the overloads that carry neither
    /// <c>RequiresUnreferencedCode</c> nor <c>RequiresDynamicCode</c>: what a trimmed or native AOT
    /// application publishes over is the same code path a reflecting one runs.
    /// </para>
    /// <para>
    /// The open half of the contract still goes through Quartz's converters, because a generated
    /// <see cref="JsonTypeInfo" /> for a type the options carry a converter for is metadata that defers
    /// to that converter — an <see cref="ITrigger" /> or an <see cref="ICalendar" /> reaches the registry
    /// either way.
    /// </para>
    /// </remarks>
    private static JsonTypeInfo<T> WireFormatOf<T>(JsonSerializerOptions serializerOptions)
    {
        return (JsonTypeInfo<T>) serializerOptions.GetTypeInfo(typeof(T));
    }

    public static async ValueTask<TResponse> Get<TResponse>(
        this HttpClient client,
        string requestUri,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        await response.CheckResponseStatusCode(serializerOptions, cancellationToken).ConfigureAwait(false);

        return await response.Content.ReadOrThrow<TResponse>(serializerOptions, cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<TResponse?> GetWithNullForNotFound<TResponse>(
        this HttpClient client,
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
        this HttpClient client,
        string requestUri,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync(requestUri, content: null!, cancellationToken).ConfigureAwait(false);
        await response.CheckResponseStatusCode(serializerOptions, cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask Post<TRequest>(
        this HttpClient client,
        string requestUri,
        TRequest value,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(requestUri, value, WireFormatOf<TRequest>(serializerOptions), cancellationToken).ConfigureAwait(false);
        await response.CheckResponseStatusCode(serializerOptions, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<TResponse> PostWithResponse<TResponse>(
        this HttpClient client,
        string requestUri,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync(requestUri, content: null!, cancellationToken).ConfigureAwait(false);
        await response.CheckResponseStatusCode(serializerOptions, cancellationToken).ConfigureAwait(false);

        return await response.Content.ReadOrThrow<TResponse>(serializerOptions, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<TResponse> PostWithResponse<TRequest, TResponse>(
        this HttpClient client,
        string requestUri,
        TRequest value,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(requestUri, value, WireFormatOf<TRequest>(serializerOptions), cancellationToken).ConfigureAwait(false);
        await response.CheckResponseStatusCode(serializerOptions, cancellationToken).ConfigureAwait(false);

        return await response.Content.ReadOrThrow<TResponse>(serializerOptions, cancellationToken).ConfigureAwait(false);
    }

    public static async Task Delete(
        this HttpClient client,
        string requestUri,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken)
    {
        using var response = await client.DeleteAsync(requestUri, cancellationToken).ConfigureAwait(false);
        await response.CheckResponseStatusCode(serializerOptions, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<TResponse> DeleteWithResponse<TResponse>(
        this HttpClient client,
        string requestUri,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken)
    {
        using var response = await client.DeleteAsync(requestUri, cancellationToken).ConfigureAwait(false);
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

        ProblemDetailsDto? problemDetails = null;

        try
        {
            problemDetails = await response.Content.ReadFromJsonAsync(WireFormatOf<ProblemDetailsDto>(serializerOptions), cancellationToken).ConfigureAwait(false);
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

        // Every error body names the exception type the server raised, whichever layer produced it,
        // so a bad request a scheduler raised is rethrown here as the same exception. Any other name -
        // a request the endpoint rejected before it reached the scheduler, or a server that is not
        // this one - is opaque, and reported as such.
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            string? exceptionType = null;
            if (problemDetails.Extensions is not null &&
                problemDetails.Extensions.TryGetValue(HttpApiConstants.ProblemDetailsExceptionType, out JsonElement exceptionTypeElement))
            {
                exceptionType = exceptionTypeElement.GetString();
            }

            throw exceptionType switch
            {
                nameof(SchedulerException) => new SchedulerException(problemDetails.Detail),
                nameof(InvalidConfigurationException) => new InvalidConfigurationException(problemDetails.Detail),
                nameof(JobExecutionException) => new JobExecutionException(problemDetails.Detail),
                nameof(JobPersistenceException) => new JobPersistenceException(problemDetails.Detail),
                nameof(SchedulerConfigException) => new SchedulerConfigException(problemDetails.Detail),
                nameof(LockException) => new LockException(problemDetails.Detail),
                nameof(NoSuchDelegateException) => new NoSuchDelegateException(problemDetails.Detail),
                nameof(ObjectAlreadyExistsException) => new ObjectAlreadyExistsException(problemDetails.Detail),
                _ => new HttpClientException($"Received response with bad request status code: {problemDetails.Detail}")
            };
        }

        throw new HttpClientException($"Received response with status code {response.StatusCode}, error details: {problemDetails.Detail}");
    }

    private static async Task<T> ReadOrThrow<T>(this HttpContent content, JsonSerializerOptions serializerOptions, CancellationToken cancellationToken)
    {
        var result = await content.ReadFromJsonAsync(WireFormatOf<T>(serializerOptions), cancellationToken).ConfigureAwait(false);
        return result ?? throw new HttpClientException("Could not deserialize response");
    }
}