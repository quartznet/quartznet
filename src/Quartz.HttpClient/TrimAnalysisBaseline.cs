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

// The trim- and AOT-analysis warnings Quartz.HttpClient still produces (https://github.com/quartznet/quartznet/issues/3431).
//
// Read src/Quartz/TrimAnalysisBaseline.cs first. The reasoning this file works to is written down there
// and is not repeated here: why a suppression is scoped to the type rather than the member, why
// SuppressMessage rather than the unconditional form, and — most of all — that adding an entry is the
// wrong first move. The order to try fixes in, in short: resolve the type statically; else annotate with
// [DynamicallyAccessedMembers] so the requirement travels to a caller that can satisfy it; else say
// [RequiresUnreferencedCode] on surface a trimmed application can avoid, having checked where the
// attribute propagates to.
//
// Ten warnings when the analyzers were first turned on here, two now, and the eight that went away went
// away rather than being written down. They were four call sites in HttpClientExtensions — two
// PostAsJsonAsync, two ReadFromJsonAsync — each an IL2026 and an IL3050 for the same reason: handed only
// a JsonSerializerOptions, System.Text.Json has to reflect over the body's type to find out what it is.
// It does not have to. Step 4 of #3341 made the wire contract source-generated (HttpApiJsonContext), and
// ConfigureWireFormat puts that context in front of whatever resolver the options already had — so the
// answer was already there to be asked for. HttpClientExtensions.WireFormatOf asks for it and passes the
// JsonTypeInfo, which binds the overloads that carry neither attribute. Nothing about the bytes on the
// wire changed; the converters still answer for the open half of the contract, because generated metadata
// for a type the options carry a converter for is metadata that defers to that converter.
//
// So this package produces no IL3050 at all, and the single-file analyzer found nothing: the client reads
// no file and knows no assembly's location.

// --- A job type named in an HTTP response ---------------------------------------------------------------
// A job travels over the API as a JobDetailDto, whose JobType is a string — deliberately, and the type
// says why: resolving a name on receipt would have the receiving process walk its probing paths for
// whatever a peer sent it, and would break a reader that has every right not to have the job's assembly
// loaded. JobDetailDto.AsIJobDetail says [RequiresUnreferencedCode] for the consequence: whoever does
// eventually run that job needs the type to have survived trimming.
//
// The entry below is where that statement stops travelling. The two call sites it covers are IScheduler
// members — GetJobDetail and GetJobDetails — and IL2046 wants [RequiresUnreferencedCode] on both sides of
// an interface member, which would put it on IScheduler and therefore on every scheduler including the
// in-memory one, which is clean. So the attribute stops one member earlier and the fact is recorded here.
//
// An application publishing trimmed over this package keeps its job types the ordinary way: reference
// them from AddJob<T>() or JobBuilder.Create<T>(). A client that only lists jobs rather than running them
// needs nothing — the name is carried, not resolved.

[assembly: SuppressMessage("Trimming", "IL2026", Scope = "type", Target = "T:Quartz.HttpScheduler", Justification = "A job read back over HTTP names its type as a string; IScheduler cannot carry the attribute that says so.")]
