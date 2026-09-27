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

using System.Net.Http.Headers;

using Quartz.HttpApiContract;

namespace Quartz.Impl;

/// <summary>
/// Carries wire requests over HTTP, through an <see cref="HttpClient" /> whose base address is where the
/// API is mapped.
/// </summary>
/// <remarks>
/// <para>
/// What <c>HttpScheduler</c> always did, as a transport: the request's method and path relative to the
/// base address, a JSON body sent as <c>application/json; charset=utf-8</c> when there is one, and the
/// whole response read back. The bytes of a body are the bytes the serializer writes, so a request says
/// what it said before; only its framing is a <c>Content-Length</c> now rather than a chunked stream.
/// </para>
/// <para>
/// The client belongs to whoever made it and is never disposed here.
/// </para>
/// </remarks>
internal sealed class HttpWireTransport : IWireTransport
{
    private readonly HttpClient httpClient;

    public HttpWireTransport(HttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async ValueTask<WireResponse> Send(WireRequest request, CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage message = new(HttpMethod.Parse(request.Route.Method), request.Path);
        if (request.Body is not null)
        {
            message.Content = new ByteArrayContent(request.Body)
            {
                Headers = { ContentType = new MediaTypeHeaderValue(JsonMediaType) { CharSet = "utf-8" } }
            };
        }

        using HttpResponseMessage response = await httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return new WireResponse(response.StatusCode, body);
    }

    private const string JsonMediaType = "application/json";
}
