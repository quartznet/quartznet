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

namespace Quartz.HttpApiContract;

/// <summary>
/// One call on the wire: the route it is a call of, the path with its query, and the JSON body when it
/// carries one.
/// </summary>
/// <remarks>
/// The route rides along rather than just its method, so that a carrier can dispatch on it without
/// parsing the path back apart, and so that a request can only be made of a route the catalogue holds.
/// </remarks>
/// <param name="Route">The route this is a call of.</param>
/// <param name="Path">The route's template with its values filled in, and the query string if any.</param>
/// <param name="Body">The UTF-8 JSON body, or <see langword="null" /> for a request that sends none.</param>
internal readonly record struct WireRequest(WireRoute Route, string Path, byte[]? Body = null)
{
    /// <summary>
    /// The same request with <paramref name="query" /> appended to the path as given — empty, or starting
    /// with <c>?</c>.
    /// </summary>
    public WireRequest WithQuery(string query) => this with { Path = Path + query };
}

/// <summary>
/// The answer to one <see cref="WireRequest" />: its status, and its body as the bytes that arrived.
/// </summary>
/// <param name="Status">The status the operation answered with.</param>
/// <param name="Body">The body, empty when there was none.</param>
internal readonly record struct WireResponse(HttpStatusCode Status, byte[] Body);

/// <summary>
/// What carries a <see cref="WireRequest" /> to a scheduler and brings its <see cref="WireResponse" />
/// back.
/// </summary>
/// <remarks>
/// <para>
/// The seam under <c>HttpScheduler</c>. The one implementation Quartz ships is HTTP, which is the
/// behaviour <c>HttpScheduler</c> always had; a connection the scheduler's own process dialled out on
/// carries the same requests and the same answers without HTTP, so a client over it is the same
/// <c>HttpScheduler</c> rather than a second mapping of <see cref="IScheduler" /> onto the wire.
/// </para>
/// <para>
/// A transport reports what came back, whatever it was. Turning a status and a problem-details body into
/// the exception an in-process caller would have seen is the client's job, in one place, so that every
/// transport raises the same exceptions for the same answers.
/// </para>
/// </remarks>
internal interface IWireTransport
{
    /// <summary>
    /// Sends <paramref name="request" /> and answers with what came back.
    /// </summary>
    /// <remarks>
    /// Throws only when there is no answer at all — the target could not be reached, or the call was
    /// cancelled. Every status the target answers with, a failure included, is a
    /// <see cref="WireResponse" />.
    /// </remarks>
    ValueTask<WireResponse> Send(WireRequest request, CancellationToken cancellationToken = default);
}
