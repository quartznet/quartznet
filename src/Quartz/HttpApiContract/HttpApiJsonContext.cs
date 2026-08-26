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

namespace Quartz.HttpApiContract;

/// <summary>
/// The wire contract as metadata the compiler wrote: every request body, response body and page
/// envelope the HTTP API exchanges, listed so that reading or writing one needs no reflection.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="HttpApiJson.ConfigureWireFormat" /> puts this in front of the reflection resolver, so a
/// listed type is answered here and never reflects. The open half of the contract is deliberately not
/// closed by it: an <see cref="ITrigger" />, an <see cref="ICalendar" /> and a <see cref="JobDataMap" />
/// are whatever the application made them, and they keep going through the converters that consult the
/// scheduler's <c>SystemTextJsonSerializerRegistry</c>. They are named below all the same, because the
/// generator follows the closure of the bodies that carry them anyway; what it writes for them is
/// metadata that defers to the converter the options carry, which is what a generated type does with
/// any converter registered at runtime.
/// </para>
/// <para>
/// A body that gains a type has to be listed here too. Nothing breaks if it is not — the chain falls
/// through to reflection, which is exactly why it would go unnoticed. <c>WireFormatSourceGenerationTest</c>
/// is what notices.
/// </para>
/// <para>
/// <see cref="JsonSourceGenerationMode.Metadata" /> rather than the default, because the generated
/// write path bakes in the naming and number handling of the options the context was declared with, and
/// System.Text.Json will only take it when those options match the ones in use. The wire options never
/// do — they carry Quartz's converters — so the generated writers would be dead code. Asking for
/// metadata alone keeps the generated source to what is actually reached.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]

// Request bodies.
[JsonSerializable(typeof(AddCalendarRequest))]
[JsonSerializable(typeof(AddJobRequest))]
[JsonSerializable(typeof(DeleteJobsRequest))]
[JsonSerializable(typeof(JobKeySetRequest))]
[JsonSerializable(typeof(KeyDto[]))]
[JsonSerializable(typeof(RescheduleJobRequest))]
[JsonSerializable(typeof(ScheduleJobRequest))]
[JsonSerializable(typeof(ScheduleJobsRequest))]
[JsonSerializable(typeof(SetExecutionLimitsRequest))]
[JsonSerializable(typeof(TriggerJobRequest))]
[JsonSerializable(typeof(TriggerKeySetRequest))]
[JsonSerializable(typeof(UnscheduleJobsRequest))]

// Response bodies.
[JsonSerializable(typeof(AffectedGroupsResponse))]
[JsonSerializable(typeof(AppliedJobKeysResponse))]
[JsonSerializable(typeof(AppliedTriggerKeysResponse))]
[JsonSerializable(typeof(ClusterNodeDto[]))]
[JsonSerializable(typeof(ExecutionLimitsResponse))]
[JsonSerializable(typeof(ExistsResponse))]
[JsonSerializable(typeof(GroupPausedResponse))]
[JsonSerializable(typeof(JobDetailDto))]
[JsonSerializable(typeof(JobDetailDto[]))]
[JsonSerializable(typeof(OperationAppliedResponse))]
[JsonSerializable(typeof(PagedResultDto<FireInstanceDto>))]
[JsonSerializable(typeof(PagedResultDto<JobGroupDto>))]
[JsonSerializable(typeof(PagedResultDto<JobHeaderDto>))]
[JsonSerializable(typeof(PagedResultDto<string>))]
[JsonSerializable(typeof(PagedResultDto<TriggerGroupDto>))]
[JsonSerializable(typeof(PagedResultDto<TriggerHeaderDto>))]
[JsonSerializable(typeof(ProblemDetailsDto))]
[JsonSerializable(typeof(RescheduleJobResponse))]
[JsonSerializable(typeof(ScheduleJobResponse))]
[JsonSerializable(typeof(SchedulerContextDto))]
[JsonSerializable(typeof(SchedulerDto))]
[JsonSerializable(typeof(SchedulerHeaderDto[]))]
[JsonSerializable(typeof(TriggerStateDto))]

// The open types, which are bodies of their own as well as members of the ones above.
[JsonSerializable(typeof(ICalendar))]
[JsonSerializable(typeof(ITrigger))]
[JsonSerializable(typeof(List<ITrigger>))]
internal sealed partial class HttpApiJsonContext : JsonSerializerContext;
