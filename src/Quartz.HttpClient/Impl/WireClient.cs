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
using System.Text.Json;

using Quartz.HttpApiContract;

namespace Quartz.Impl;

/// <summary>
/// Turns calls into wire requests and their answers back into results, or into the exceptions an
/// in-process caller would have seen.
/// </summary>
/// <remarks>
/// What every reader of a remote scheduler shares, whatever carries its requests: the body is written
/// with the wire format's generated metadata, the answer's status goes through
/// <see cref="HttpClientExtensions.EnsureSuccess" />, and its body is read back the same way.
/// </remarks>
internal sealed class WireClient
{
    private readonly IWireTransport transport;
    private readonly JsonSerializerOptions serializerOptions;

    public WireClient(IWireTransport transport, JsonSerializerOptions serializerOptions)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        this.serializerOptions = serializerOptions ?? throw new ArgumentNullException(nameof(serializerOptions));
    }

    /// <summary>
    /// Sends a request whose answer has nothing to read.
    /// </summary>
    public async ValueTask Send(WireRequest request, CancellationToken cancellationToken)
    {
        WireResponse response = await transport.Send(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccess(serializerOptions);
    }

    /// <summary>
    /// Sends <paramref name="body" /> with a request whose answer has nothing to read.
    /// </summary>
    public ValueTask Send<TBody>(WireRequest request, TBody body, CancellationToken cancellationToken)
    {
        return Send(WithBody(request, body), cancellationToken);
    }

    /// <summary>
    /// Sends a request and reads its answer.
    /// </summary>
    public async ValueTask<TResponse> SendAndRead<TResponse>(WireRequest request, CancellationToken cancellationToken)
    {
        WireResponse response = await transport.Send(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccess(serializerOptions);
        return response.ReadBody<TResponse>(serializerOptions);
    }

    /// <summary>
    /// Sends <paramref name="body" /> with a request and reads its answer.
    /// </summary>
    public ValueTask<TResponse> SendAndRead<TBody, TResponse>(WireRequest request, TBody body, CancellationToken cancellationToken)
    {
        return SendAndRead<TResponse>(WithBody(request, body), cancellationToken);
    }

    /// <summary>
    /// Sends a read and reads its answer, or <see langword="null" /> when the answer is the <c>404</c>
    /// that says the thing read is absent.
    /// </summary>
    public async ValueTask<TResponse?> SendAndReadOrNull<TResponse>(WireRequest request, CancellationToken cancellationToken) where TResponse : class
    {
        WireResponse response = await transport.Send(request, cancellationToken).ConfigureAwait(false);
        if (!response.EnsureSuccess(serializerOptions, throwOnNotFound: false))
        {
            return null;
        }

        return response.ReadBody<TResponse>(serializerOptions);
    }

    /// <summary>
    /// Sends <paramref name="body" /> with a request and hands back the answer as it came, for a caller
    /// that reads an answer of its own out of a failure before <see cref="EnsureSuccess" /> reads the rest.
    /// </summary>
    public ValueTask<WireResponse> Exchange<TBody>(WireRequest request, TBody body, CancellationToken cancellationToken)
    {
        return transport.Send(WithBody(request, body), cancellationToken);
    }

    /// <summary>
    /// Sends a request with no body and hands back the answer as it came.
    /// </summary>
    /// <inheritdoc cref="Exchange{TBody}" path="/remarks" />
    public ValueTask<WireResponse> Exchange(WireRequest request, CancellationToken cancellationToken)
    {
        return transport.Send(request, cancellationToken);
    }

    /// <summary>
    /// The detail of a failure's problem details, or <see langword="null" /> when it carries none.
    /// </summary>
    public string? ProblemDetail(WireResponse response)
    {
        try
        {
            return JsonSerializer.Deserialize(response.Body, HttpClientExtensions.WireFormatOf<ProblemDetailsDto>(serializerOptions))?.Detail;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <inheritdoc cref="HttpClientExtensions.EnsureSuccess" />
    public bool EnsureSuccess(WireResponse response, bool throwOnNotFound = true)
    {
        return response.EnsureSuccess(serializerOptions, throwOnNotFound);
    }

    /// <summary>
    /// The detail of a <c>400</c> that refused the request itself, rather than one the scheduler raised.
    /// </summary>
    public bool TryReadRequestRefusal(WireResponse response, [NotNullWhen(true)] out string? detail)
    {
        return response.TryReadRequestRefusal(serializerOptions, out detail);
    }

    /// <summary>
    /// Reads a success's body.
    /// </summary>
    public TResponse Read<TResponse>(WireResponse response)
    {
        return response.ReadBody<TResponse>(serializerOptions);
    }

    private WireRequest WithBody<TBody>(WireRequest request, TBody body)
    {
        return request with { Body = JsonSerializer.SerializeToUtf8Bytes(body, HttpClientExtensions.WireFormatOf<TBody>(serializerOptions)) };
    }
}
