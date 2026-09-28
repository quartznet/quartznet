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

using System.Diagnostics.CodeAnalysis;
using System.Net;
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
    /// Every body this client sends or reads is a contract type, and <c>HttpApiJsonContext</c> — which
    /// <c>ConfigureWireFormat</c> puts in front of whatever resolver the options already had — states all
    /// of them. So the answer comes from generated metadata, and passing it to the serializer binds the
    /// overloads that carry neither <c>RequiresUnreferencedCode</c> nor <c>RequiresDynamicCode</c>: what a
    /// trimmed or native AOT application publishes over is the same code path a reflecting one runs.
    /// </para>
    /// <para>
    /// The open half of the contract still goes through Quartz's converters, because a generated
    /// <see cref="JsonTypeInfo" /> for a type the options carry a converter for is metadata that defers
    /// to that converter — an <see cref="ITrigger" /> or an <see cref="ICalendar" /> reaches the registry
    /// either way.
    /// </para>
    /// </remarks>
    public static JsonTypeInfo<T> WireFormatOf<T>(JsonSerializerOptions serializerOptions)
    {
        return (JsonTypeInfo<T>) serializerOptions.GetTypeInfo(typeof(T));
    }

    /// <summary>
    /// Opens a response whose body is read as it arrives rather than to its end, answering the response
    /// itself so that the caller owns both it and the stream.
    /// </summary>
    /// <remarks>
    /// <see cref="HttpCompletionOption.ResponseHeadersRead" /> is what makes it a stream, which is the
    /// one thing <see cref="IWireTransport" /> does not carry: its answer is a whole body, and this one
    /// has no end. The status is checked by <see cref="EnsureSuccess" /> all the same, which on anything
    /// but a success means the response is disposed and the caller sees the exception rather than a
    /// stream that will never yield.
    /// </remarks>
    public static async ValueTask<HttpResponseMessage> GetStream(
        this HttpClient client,
        string requestUri,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await client
            .GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (!response.IsSuccessStatusCode)
            {
                byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                new WireResponse(response.StatusCode, body).EnsureSuccess(serializerOptions);
            }

            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Raises the exception an in-process caller would have seen for an answer that is not a success.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one place a status and a problem-details body become an exception, whatever transport carried
    /// them. A <c>404</c> naming an unknown scheduler always throws: no request to it can succeed.
    /// </para>
    /// <para>
    /// An error that carries no problem details is not one this API wrote — a route the server does not
    /// have, or a server that is not this one — and raises the <see cref="HttpRequestException" />
    /// <see cref="HttpResponseMessage.EnsureSuccessStatusCode" /> raises, with its status, so a caller that
    /// tells "this target has no such route" apart by it still can.
    /// </para>
    /// </remarks>
    /// <returns>
    /// <see langword="true" /> for a success; <see langword="false" /> for a <c>404</c> with problem
    /// details, when <paramref name="throwOnNotFound" /> is off, which a read answers as "absent".
    /// </returns>
    public static bool EnsureSuccess(this WireResponse response, JsonSerializerOptions serializerOptions, bool throwOnNotFound = true)
    {
        if ((int) response.Status is >= 200 and <= 299)
        {
            return true;
        }

        ProblemDetailsDto? problemDetails = null;

        try
        {
            problemDetails = JsonSerializer.Deserialize(response.Body, WireFormatOf<ProblemDetailsDto>(serializerOptions));
        }
        catch (JsonException)
        {
            // Ignored because we can have responses which are not json
        }

        if (problemDetails?.Detail is null || string.IsNullOrWhiteSpace(problemDetails.Detail))
        {
            // When Web API returns error response it is always problem details, so throw what HttpClient
            // throws for a failure status if we do not have problem details
            using HttpResponseMessage failure = new(response.Status);
            failure.EnsureSuccessStatusCode();
            return false;
        }

        if (response.Status == HttpStatusCode.NotFound)
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
        if (response.Status == HttpStatusCode.BadRequest)
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
                nameof(ObjectDoesNotExistException) => new ObjectDoesNotExistException(problemDetails.Detail),
                _ => new HttpClientException($"Received response with bad request status code: {problemDetails.Detail}")
            };
        }

        throw new HttpClientException($"Received response with status code {response.Status}, error details: {problemDetails.Detail}");
    }

    /// <summary>
    /// Whether <paramref name="response" /> is a <c>400</c> refusing the request itself — its problem details
    /// named <see cref="HttpApiConstants.RequestRefusedExceptionType" /> — and the refusal's detail when it is.
    /// </summary>
    /// <remarks>
    /// <see cref="EnsureSuccess" /> raises such a refusal as an opaque <see cref="HttpClientException" />,
    /// because most of them say the client built a request wrong. A caller whose request carries a value the
    /// host judges — a backfill's range against the host's clock — reads the refusal here first and raises
    /// what an in-process call would have.
    /// </remarks>
    public static bool TryReadRequestRefusal(this WireResponse response, JsonSerializerOptions serializerOptions, [NotNullWhen(true)] out string? detail)
    {
        detail = null;
        if (response.Status != HttpStatusCode.BadRequest)
        {
            return false;
        }

        ProblemDetailsDto? problemDetails;
        try
        {
            problemDetails = JsonSerializer.Deserialize(response.Body, WireFormatOf<ProblemDetailsDto>(serializerOptions));
        }
        catch (JsonException)
        {
            return false;
        }

        if (problemDetails?.Extensions is null
            || string.IsNullOrWhiteSpace(problemDetails.Detail)
            || !problemDetails.Extensions.TryGetValue(HttpApiConstants.ProblemDetailsExceptionType, out JsonElement exceptionType)
            || exceptionType.ValueKind != JsonValueKind.String
            || !string.Equals(exceptionType.GetString(), HttpApiConstants.RequestRefusedExceptionType, StringComparison.Ordinal))
        {
            return false;
        }

        detail = problemDetails.Detail;
        return true;
    }

    /// <summary>
    /// Reads a success's body as <typeparamref name="T" />.
    /// </summary>
    /// <exception cref="HttpClientException">The body is JSON <c>null</c>.</exception>
    public static T ReadBody<T>(this WireResponse response, JsonSerializerOptions serializerOptions)
    {
        T? result = JsonSerializer.Deserialize(response.Body, WireFormatOf<T>(serializerOptions));
        return result ?? throw new HttpClientException("Could not deserialize response");
    }
}
