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

using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Mvc;

namespace Quartz.AspNetCore.HttpApi;

/// <summary>
/// The shape every error the HTTP API answers with, as metadata the compiler wrote.
/// </summary>
/// <remarks>
/// <para>
/// Both error paths, <c>ExceptionHandler</c> and the per-scheduler refusal, answer through
/// <c>Results.Problem</c>. That writes a <see cref="ProblemDetails" /> through the application's HTTP JSON
/// options, and in a trimmed or native AOT publish those options have no metadata for it unless the
/// application called <c>AddProblemDetails()</c>. Writing the error then threw, so every <c>400</c>,
/// <c>403</c>, <c>404</c> and <c>500</c> went out as an empty <c>500</c>, with no trim warning.
/// <c>Quartz.Trimming.Canary.Wire</c> found it (#3965).
/// </para>
/// <para>
/// <see cref="QuartzJsonOptionsSetup" /> adds this at the end of the chain, where <c>AddProblemDetails()</c>
/// adds ASP.NET Core's own. An application with reflection-based serialization answers from reflection
/// first, as it always did. <see cref="string" /> is listed because the members Quartz adds to
/// <see cref="ProblemDetails.Extensions" /> are strings, and an extension value is written as its runtime type.
/// </para>
/// <para>
/// <see cref="JsonSourceGenerationMode.Metadata" />, for the reason <c>HttpApiJsonContext</c> gives: the
/// options in use are never the ones this context was declared with.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(string))]
internal sealed partial class HttpApiProblemDetailsJsonContext : JsonSerializerContext;
