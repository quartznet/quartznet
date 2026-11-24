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

namespace Quartz.Mcp.Impl;

/// <summary>
/// Every shape the MCP endpoint reads or writes, with generated metadata: the arguments, the answers and the
/// confirmation token's payload.
/// </summary>
/// <remarks>
/// <para>
/// Read with this and nothing else, so no tool reflects over a type at run time, and the native canary reads and
/// writes the same bytes the tests do. The schemas a client is shown are written from the same metadata by
/// <c>JsonSchemaExporter</c>, so a schema cannot say one thing while the reader does another.
/// </para>
/// <para>
/// A constructor parameter without a default is required and a reference type not declared nullable may not be
/// <see langword="null" />, both enforced by the reader and both stated by the schema. Enums travel as their
/// names.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(ListSchedulersArguments))]
[JsonSerializable(typeof(SchedulerArguments))]
[JsonSerializable(typeof(ListJobsArguments))]
[JsonSerializable(typeof(JobArguments))]
[JsonSerializable(typeof(TriggerArguments))]
[JsonSerializable(typeof(ListTriggersArguments))]
[JsonSerializable(typeof(ListExecutingArguments))]
[JsonSerializable(typeof(QueryHistoryArguments))]
[JsonSerializable(typeof(PauseArguments))]
[JsonSerializable(typeof(InterruptArguments))]
[JsonSerializable(typeof(RescheduleArguments))]
[JsonSerializable(typeof(UnscheduleArguments))]
[JsonSerializable(typeof(ListSchedulersAnswer))]
[JsonSerializable(typeof(SchedulerDetailsAnswer))]
[JsonSerializable(typeof(ClusterNodesAnswer))]
[JsonSerializable(typeof(ListJobsAnswer))]
[JsonSerializable(typeof(JobAnswer))]
[JsonSerializable(typeof(ListTriggersAnswer))]
[JsonSerializable(typeof(TriggerAnswer))]
[JsonSerializable(typeof(ListExecutingAnswer))]
[JsonSerializable(typeof(HistoryAnswer))]
[JsonSerializable(typeof(JobRunStatusAnswer))]
[JsonSerializable(typeof(ChangeAnswer))]
[JsonSerializable(typeof(ConfirmAnswer))]
[JsonSerializable(typeof(ErrorAnswer))]
[JsonSerializable(typeof(ConfirmationPayload))]
internal sealed partial class McpJsonContext : JsonSerializerContext;
