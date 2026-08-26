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

// The trim- and AOT-analysis warnings Quartz.Jobs still produces (https://github.com/quartznet/quartznet/issues/3431).
//
// Read src/Quartz/TrimAnalysisBaseline.cs first. The reasoning this file works to is written down there
// and is not repeated here: why a suppression is scoped to the type rather than the member, why
// SuppressMessage rather than the unconditional form, and — most of all — that adding an entry is the
// wrong first move. The order to try fixes in, in short: resolve the type statically; else annotate with
// [DynamicallyAccessedMembers] so the requirement travels to a caller that can satisfy it; else say
// [RequiresUnreferencedCode] on surface a trimmed application can avoid, having checked where the
// attribute propagates to.
//
// One warning when the analyzers were first turned on here, one now, and it is the one below. The AOT
// and single-file analyzers found nothing at all: the jobs in this package read the file system and are
// otherwise ordinary types the container constructs.

// --- A listener named in the job data map --------------------------------------------------------------
// DirectoryScanJob takes the name of its IDirectoryScanListener as job data, which is a string by the
// time the job runs. Where a service provider is available the name is read as a type name, and the only
// place to look one up is the assemblies that happen to be loaded — the container is asked for the
// listener only once its Type has been found that way. The answer is cached per name, so the walk is
// once per listener rather than once per fire, but the trimmer still cannot follow it: a listener type
// reached only this way has to be kept by the application, by registering it in the container or by
// naming it in a TrimmerRootDescriptor.
//
// [RequiresUnreferencedCode] cannot say so from here: the only path in is DirectoryScanJob.Execute, and
// IJob is an interface Quartz does not own both sides of.

[assembly: SuppressMessage("Trimming", "IL2026", Scope = "type", Target = "T:Quartz.Jobs.DirectoryScanJobModel", Justification = "A directory-scan listener named as job data is found by walking the loaded assemblies for a type of that name.")]
