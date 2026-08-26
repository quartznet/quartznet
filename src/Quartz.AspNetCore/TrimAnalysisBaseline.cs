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

// The trim- and AOT-analysis warnings Quartz.AspNetCore still produces (https://github.com/quartznet/quartznet/issues/3431).
//
// Read src/Quartz/TrimAnalysisBaseline.cs first. The reasoning this file works to is written down there
// and is not repeated here: why a suppression is scoped to the type rather than the member, why
// SuppressMessage rather than the unconditional form, and — most of all — that adding an entry is the
// wrong first move. The order to try fixes in, in short: resolve the type statically; else annotate with
// [DynamicallyAccessedMembers] so the requirement travels to a caller that can satisfy it; else say
// [RequiresUnreferencedCode] on surface a trimmed application can avoid, having checked where the
// attribute propagates to.
//
// 116 warnings when the analyzers were first turned on here, one now, and 115 went away rather than being
// written down. This package is the reason #3431 says the framework's own fix comes before the first line
// of baseline:
//
//   - 114 were MapGet, MapPost and MapDelete, each an IL2026 and an IL3050, because a Delegate handed to
//     the routing table has its parameters bound and its result written by RequestDelegateFactory
//     reflecting over it at run time. EnableRequestDelegateGenerator in Quartz.AspNetCore.csproj has the
//     compiler write that code instead, and it intercepts all 57 call sites — the csproj says how that
//     was counted. Nothing about the endpoints changed to earn it.
//   - Two were EndpointHelper.JsonResponse calling Results.Json with a JsonSerializerOptions and no
//     metadata, which leaves System.Text.Json to reflect over the response type. The wire contract has
//     been source-generated since #3400 (HttpApiJsonContext), and ConfigureWireFormat puts it in front of
//     whatever resolver the options carry, so the answer was already there to be asked for: EndpointHelper
//     takes the application's JsonOptions, asks GetTypeInfo, and passes the JsonTypeInfo — the overload
//     that carries neither attribute. That made JsonResponse and ExecuteWithJsonResponse instance members,
//     which is why the endpoints now call them through the EndpointHelper they were already being handed.
//
// So this package produces no IL3050 at all, and the single-file analyzer found nothing.
//
// One warning arrived rather than left, and it was here all along. The analyzer does not report an
// IL2026 for a [RequiresUnreferencedCode] call whose result is read through a tuple element —
// `dto.AsIJobDetail().JobDetail` says nothing, where `var (detail, reason) = dto.AsIJobDetail()` reports —
// so the three endpoints that build a job out of a request body were silently calling one. Gathering them
// into RequestedJobDetail, which uses the reason it is given instead of null-forgiving it away, makes the
// call visible as well as better-behaved. Worth knowing when reading any of these files: a quiet analyzer
// is not the same as no reflection.

// --- A job type named in a request body -----------------------------------------------------------------
// A job arrives over the API as a JobDetailDto, whose JobType is a string — deliberately, and JobDetailDto
// says why: resolving a name on receipt would have this process walk its probing paths for whatever a
// client sent it, which is a type disclosure side channel that does real work per request. So the name is
// stored unresolved and AsIJobDetail says [RequiresUnreferencedCode] for the consequence: whoever runs
// that job needs its type to have survived trimming.
//
// The entry below is where that statement stops travelling. RequestedJobDetail is reached from AddJob,
// ScheduleJob and ScheduleJobs, which are delegates the routing table holds; the attribute on them would
// land on the MapPost that converts them, then on MapEndpoints, then on MapQuartzHttpApi — which every
// application serving this API calls, including one that only ever lists and pauses. That is the same wall
// AddQuartz is in the core package, and the answer is the same: stop one member earlier and record it.
//
// An application publishing trimmed keeps its job types the ordinary way — reference them from AddJob<T>()
// or JobBuilder.Create<T>() — and an API that is only read from needs nothing, because a job listed over
// HTTP carries its type name and never resolves it.

[assembly: SuppressMessage("Trimming", "IL2026", Scope = "type", Target = "T:Quartz.AspNetCore.HttpApi.Util.RequestedJobDetail", Justification = "A job posted to the HTTP API names its type as a string; the attribute that says so cannot reach past MapQuartzHttpApi.")]
