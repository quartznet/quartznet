#region License

/*
 * Copyright 2009- Marko Lahma
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

// The recorded set of trim-analysis warnings Quartz produced when EnableTrimAnalyzer was first turned
// on (https://github.com/quartznet/quartznet/issues/3341). It is a baseline, not an all-clear: none of
// these call sites is trim-safe, and every one of them is work still to do. What the baseline buys is
// that a *new* one fails the build, because TreatWarningsAsErrors makes IL2xxx an error and nothing
// suppresses a warning that is not listed here.
//
// Adding an entry is the wrong first move. Reach for it only after establishing that the reflection is
// genuinely unavoidable, and then say in the group comment why. The preferred fixes, in order:
//
//   1. Resolve the type statically — a generic parameter or a typed option instead of a type name.
//   2. Annotate with [DynamicallyAccessedMembers], so the requirement travels to the caller that does
//      know the type. Check where it propagates to first, and whether it can travel at all: on the fire
//      path it cannot, because JobType.Type erases the annotation — a job named by a string has no
//      annotatable type. That is why ObjectUtils is left alone here (issue #3341, step 3).
//   3. Mark the API [RequiresUnreferencedCode], for opt-in surface a trimmed app can avoid entirely.
//
// Scope is deliberately the type rather than the member. ILLink reports these against compiler-
// generated closure types whose names change whenever a lambda is added to or removed from the file, so
// the companion ILLink.Suppressions.xml has to key on types; keying this file the same way keeps the two
// readable side by side. The cost is that a second reflective call in an already-listed type does not
// fail the build. Every type listed here is reflective by construction, so that is the right trade —
// which is also why a *new* type appearing in this list deserves an argument, not a line.
//
// SuppressMessageAttribute is [Conditional("CODE_ANALYSIS")] and never reaches metadata, so this file
// silences the analyzer during Quartz's own compile and nothing else. Consumers publishing a trimmed
// application still see every warning below, which is honest: their app really is affected. The
// unconditional form would hide it from them too, and that would be a lie until step 3 lands.

// --- Types named by string --------------------------------------------------------------------------
// Configuration and persistence both store types as text: the flat quartz.* keys, the JOB_CLASS_NAME
// column, job_scheduling_data XML, and JobType's name-constructed form. A type resolved from a string
// cannot be proven reachable, so the trimmer cannot keep it.

[assembly: SuppressMessage("Trimming", "IL2057", Scope = "type", Target = "T:Quartz.Impl.SimpleTypeLoader", Justification = "The default ITypeLoader exists to resolve a configured type name; that is its contract.")]
[assembly: SuppressMessage("Trimming", "IL2057", Scope = "type", Target = "T:Quartz.JobType", Justification = "A name-constructed JobType resolves through Type.GetType when the caller supplied no resolver.")]
[assembly: SuppressMessage("Trimming", "IL2057", Scope = "type", Target = "T:Quartz.Impl.AdoJobStore.StdAdoDelegate", Justification = "A persisted job's type is the JOB_CLASS_NAME column, which is a string by the time it is read back.")]
[assembly: SuppressMessage("Trimming", "IL2057", Scope = "type", Target = "T:Quartz.Impl.AdoJobStore.Common.BuiltInDbMetadataFactory", Justification = "An ADO.NET provider's connection type is named by the driver description, so the provider assembly need not be referenced.")]

// --- Types constructed at runtime -------------------------------------------------------------------
// Once a type arrives as a Type rather than a generic argument, Activator/ActivatorUtilities cannot
// promise its constructor survives, and JobBuilder.OfType wants its members annotated.

[assembly: SuppressMessage("Trimming", "IL2067", Scope = "type", Target = "T:Quartz.Configuration.QuartzPropertyBridge", Justification = "Translating legacy quartz.* keys means constructing the component each key names.")]
[assembly: SuppressMessage("Trimming", "IL2072", Scope = "type", Target = "T:Quartz.Configuration.QuartzPropertyBridge", Justification = "Translating legacy quartz.* keys means constructing the component each key names.")]
[assembly: SuppressMessage("Trimming", "IL2067", Scope = "type", Target = "T:Quartz.Configuration.SchedulerPluginFactory", Justification = "A plugin registered by type is constructed through the container from that Type.")]
[assembly: SuppressMessage("Trimming", "IL2072", Scope = "type", Target = "T:Quartz.Configuration.PropertyListenerFactory", Justification = "A listener registered by quartz.*.listener.* keys is constructed from the type the key names.")]
[assembly: SuppressMessage("Trimming", "IL2067", Scope = "type", Target = "T:Quartz.Impl.JobActivatorCache", Justification = "A job type reaches the activator as a Type; that is the whole point of the cache.")]
[assembly: SuppressMessage("Trimming", "IL2072", Scope = "type", Target = "T:Quartz.Configuration.JsonSchedulingHelper", Justification = "Jobs declared in configuration name their type as a string.")]
[assembly: SuppressMessage("Trimming", "IL2072", Scope = "type", Target = "T:Quartz.Xml.XmlSchedulingDataProcessor", Justification = "Jobs declared in job_scheduling_data XML name their type as a string.")]
[assembly: SuppressMessage("Trimming", "IL2072", Scope = "type", Target = "T:Quartz.Impl.AdoJobStore.StdAdoDelegate", Justification = "A persisted job's type reaches JobBuilder.OfType as a Type resolved from JOB_CLASS_NAME.")]

// --- Properties bound by name -----------------------------------------------------------------------
// The flat quartz.* keys and JobDataMap both set properties on a target the compiler cannot see the
// type of. ObjectUtils is the shared implementation and the structural blocker for the whole track:
// annotating it would reach IJobDetail and out into every consumer's code, so it stays as it is until
// issue #3341, step 3 redesigns it.

[assembly: SuppressMessage("Trimming", "IL2026", Scope = "type", Target = "T:Quartz.Util.ObjectUtils", Justification = "TypeDescriptor.GetConverter is how a configuration string becomes a property's value.")]
[assembly: SuppressMessage("Trimming", "IL2057", Scope = "type", Target = "T:Quartz.Util.ObjectUtils", Justification = "A property typed as Type takes its value from a type name in configuration.")]
[assembly: SuppressMessage("Trimming", "IL2067", Scope = "type", Target = "T:Quartz.Util.ObjectUtils", Justification = "The conversion target arrives as a Type read off the property being set.")]
[assembly: SuppressMessage("Trimming", "IL2070", Scope = "type", Target = "T:Quartz.Util.ObjectUtils", Justification = "The conversion target arrives as a Type read off the property being set.")]
[assembly: SuppressMessage("Trimming", "IL2072", Scope = "type", Target = "T:Quartz.Util.ObjectUtils", Justification = "The conversion target arrives as a Type read off the property being set.")]
[assembly: SuppressMessage("Trimming", "IL2075", Scope = "type", Target = "T:Quartz.Util.ObjectUtils", Justification = "Setting a property named by a configuration key means looking it up on the target's runtime type.")]
[assembly: SuppressMessage("Trimming", "IL2080", Scope = "type", Target = "T:Quartz.Util.ObjectUtils", Justification = "Setting a property named by a configuration key means looking it up on the target's runtime type.")]
[assembly: SuppressMessage("Trimming", "IL2075", Scope = "type", Target = "T:Quartz.Impl.PropertySettingJobFactory", Justification = "Pushing a JobDataMap onto a job means finding properties by the map's keys.")]
[assembly: SuppressMessage("Trimming", "IL2075", Scope = "type", Target = "T:Quartz.Configuration.PropertyListenerFactory", Justification = "A listener's Name is set through the property of that name, on a type known only at runtime.")]
[assembly: SuppressMessage("Trimming", "IL2070", Scope = "type", Target = "T:Quartz.Util.JobDataExpression", Justification = "The check that the job factory will find the property is itself a property lookup by name.")]
[assembly: SuppressMessage("Trimming", "IL2075", Scope = "type", Target = "T:Quartz.Impl.AdoJobStore.Common.DbProvider", Justification = "A provider's connection string and command properties are named by its DbMetadata, not referenced.")]
[assembly: SuppressMessage("Trimming", "IL2075", Scope = "type", Target = "T:Quartz.Impl.AdoJobStore.Common.DbMetadata.DerivedMetadata", Justification = "A provider's connection string and command properties are named by its DbMetadata, not referenced.")]

// --- Duck-typed exception inspection ------------------------------------------------------------------
// Whether a database error is worth retrying lives in provider-specific properties (SqlException.Number,
// SqliteException.SqliteErrorCode). Quartz must not reference the provider assemblies to read them.

[assembly: SuppressMessage("Trimming", "IL2070", Scope = "type", Target = "T:Quartz.Impl.AdoJobStore.TransientErrorDetector", Justification = "Retry classification reads provider error codes off exception types Quartz deliberately does not reference.")]

// --- Reflection-based serialization ---------------------------------------------------------------------
// A JobDataMap holds whatever the application put in it, so the payload's types are not known until it
// is serialized. Issue #3341, step 4 replaces the closed set of wire DTOs with a source-generated
// JsonSerializerContext; the open part of the registry stays reflective on purpose.

[assembly: SuppressMessage("Trimming", "IL2026", Scope = "type", Target = "T:Quartz.Impl.SystemTextJsonObjectSerializer", Justification = "Job data is serialized as whatever the application stored; issue #3341, step 4.")]
[assembly: SuppressMessage("Trimming", "IL2026", Scope = "type", Target = "T:Quartz.Serialization.SystemTextJson.Utf8JsonWriterExtensions", Justification = "Job data is serialized as whatever the application stored; issue #3341, step 4.")]

// --- Configuration binding ------------------------------------------------------------------------------
// IServiceCollection.Configure<TOptions>(name, section) is RequiresUnreferencedCode: the binder reflects
// over TOptions. The options types are ours and closed, so the source-generated binder is the fix; that
// is a separate change from this baseline.

[assembly: SuppressMessage("Trimming", "IL2026", Scope = "type", Target = "T:Quartz.Configuration.QuartzTypedOptions", Justification = "Binding the quartz configuration section reflects over the options types; the source-generated binder is the fix.")]
