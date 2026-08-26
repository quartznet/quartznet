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

// The trim- and AOT-analysis warnings Quartz.Plugins still produces (https://github.com/quartznet/quartznet/issues/3431).
//
// Read src/Quartz/TrimAnalysisBaseline.cs first. The reasoning this file works to is written down there
// and is not repeated here: why a suppression is scoped to the type rather than the member, why
// SuppressMessage rather than the unconditional form, and — most of all — that adding an entry is the
// wrong first move. The order to try fixes in, in short: resolve the type statically; else annotate with
// [DynamicallyAccessedMembers] so the requirement travels to a caller that can satisfy it; else say
// [RequiresUnreferencedCode] on surface a trimmed application can avoid, having checked where the
// attribute propagates to.
//
// Four warnings when the analyzers were first turned on here, two now, and the two that went away went
// away rather than being written down. Both belonged to reading quartz_jobs.json:
//
//   - The reader called JsonSerializer.Deserialize<T>(string, JsonSerializerOptions), which is IL2026 and
//     IL3050 both, because the shape it deserializes into is discovered by reflecting over it.
//     JsonSchedulingDataContext states that shape instead, so the file is read through metadata the
//     compiler wrote. That is the whole of the package's IL3050: there is none left.
//   - The job type inside the file was handed to JobBuilder.OfType as an IL2072. It is a string in a
//     schedule file, so there is no annotation that could be true of it; what the file did not have was a
//     way to say so. It says so now — ProcessJsonFileAndScheduleJobs, ProcessJsonContent and ProcessJobs
//     carry [RequiresUnreferencedCode] with the same wording XmlSchedulingDataProcessor has always used,
//     one file format apart — which moves the warning to the caller that cannot pass it on, below.
//
// The single-file analyzer found nothing: the plugins open files by path and read no assembly's location.

// --- A job type named in a schedule file ---------------------------------------------------------------
// Both file plugins exist to schedule what a file declares, and a file declares a job type as text. The
// processors say [RequiresUnreferencedCode] out loud, so an application that loads a schedule file is
// told at compile time; these two entries are where that statement stops travelling.
//
// It stops here because both plugins implement ISchedulerPlugin and IFileScanListener, and the file is
// processed from Start and from FileUpdated. IL2046 wants the attribute on both sides of an interface
// member, and Quartz does not own both sides of either interface — a plugin an application wrote must
// stay free to implement them without the attribute. So the reflection is honest surface one member
// earlier, and recorded here.
//
// The application-facing form of the fix is unchanged and is what the message says: register the job
// types with AddJob<T>(), or reference them from JobBuilder.Create<T>(), and the trimmer keeps them.

[assembly: SuppressMessage("Trimming", "IL2026", Scope = "type", Target = "T:Quartz.Plugins.Xml.XmlSchedulingDataProcessorPlugin", Justification = "job_scheduling_data XML names each job's type as a string; ISchedulerPlugin and IFileScanListener cannot carry the attribute that says so.")]
[assembly: SuppressMessage("Trimming", "IL2026", Scope = "type", Target = "T:Quartz.Plugins.Json.JsonSchedulingDataProcessorPlugin", Justification = "quartz_jobs.json names each job's type as a string; ISchedulerPlugin and IFileScanListener cannot carry the attribute that says so.")]
