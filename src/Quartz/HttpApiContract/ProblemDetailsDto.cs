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

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Quartz.HttpApiContract;

/// <summary>
/// The error body every failing request answers with — RFC 9457 problem details, as the API's clients
/// read them.
/// </summary>
/// <remarks>
/// <para>
/// The server writes ASP.NET Core's own <c>ProblemDetails</c>; this is the same shape stated where the
/// rest of the contract is stated, so that a client need not reference ASP.NET Core to read an error.
/// It carries the member names explicitly rather than leaning on a naming policy, because the wire
/// spells them in lower case whatever policy the caller's options happen to have.
/// </para>
/// <para>
/// A class with settable properties rather than a record, because <see cref="Extensions" /> is filled
/// after construction: that is how <see cref="JsonExtensionDataAttribute" /> works, and it is where
/// <see cref="HttpApiConstants.ProblemDetailsExceptionType" /> arrives.
/// </para>
/// </remarks>
internal sealed class ProblemDetailsDto
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
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}
