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

// The trim- and AOT-analysis warnings Quartz still produces (https://github.com/quartznet/quartznet/issues/3341).
// It is a baseline, not an all-clear: none of these call sites is trim-safe, and the IL3050 ones are not
// AOT-safe either. What the baseline buys is that a *new* one fails the build, because
// TreatWarningsAsErrors makes an IL2xxx or an IL3xxx an error and nothing suppresses a warning that is
// not listed here.
//
// Step 3 of that issue took the fire path out of this file. A job type that reached Quartz as a type -
// JobBuilder.Create<T>(), OfType<T>(), AddJob<T>() - now carries [DynamicallyAccessedMembers] all the
// way to the attribute checks, the job factories and the ADO store's acquisition loop, and the APIs
// that accept a type *name* instead say [RequiresUnreferencedCode] out loud. What is left below is what
// that redesign could not reach, and each group says which wall it ran into.
//
// Step 4 closed the HTTP wire contract without adding a line here: HttpApiJsonContext states every body
// the API exchanges, and HttpApiJson.ConfigureWireFormat asks it before it asks reflection. That change
// removed no entry either, because the contract's reflection was never Quartz's own warning to record -
// it lived in System.Text.Json's lazy fallback, which nothing reports.
//
// Step 5 turned the AOT and single-file analyzers on beside the trim analyzer, which is where the IL3050
// entries came from. The single-file analyzer found nothing at all: Quartz reads no file it did not
// embed, so there is no Assembly.Location for it to object to. The AOT analyzer found twelve call sites
// and not one new type - every IL3050 below is on a type this file already listed, because needing
// runtime code generation and being untrimmable are the same three habits here: binding options,
// serializing job data, and reading it back. Two of the twelve are gone rather than recorded, which is
// the order this file asks for: XmlSchedulingDataProcessor asked Enum.GetValues for an array of a type
// named at runtime and now asks the generic overload for the same array at compile time, and
// HttpApiJson.ReflectionResolver answers at the call site, because a native AOT publish substitutes its
// feature switch away exactly as a trimmed one does.
//
// Step 7 deleted none, and moved the ADO store out from behind the one that reads worst. The driver
// descriptions BuiltInDbMetadataFactory ships are split in two: what is true of a driver whatever Quartz
// reaches it through, and the types, which are still resolved from strings. A registration that hands
// over the driver's DbProviderFactory - or a DbDataSource - reads only the first half and constructs
// nothing, so the IL2057 below is the name path's alone, and the overloads that take a name say
// [RequiresUnreferencedCode] rather than leaving an application to find out. The trim canary runs a whole
// SQLite store out of a TrimMode=full publish and out of a native one, which is what makes that a fact
// rather than a claim.
//
// Step 6 deleted four entries - the IL2026 and IL3050 on SystemTextJsonObjectSerializer and on
// Utf8JsonWriterExtensions - and they are the only ones this file has ever recorded that a publish did
// not merely warn about. The store serializer built options with converters and no resolver, and a
// trimmed publish switches System.Text.Json's reflection fallback off, so a persistent job store threw
// "Reflection-based serialization has been disabled for this application" on the first trigger it wrote.
// QuartzStoreJsonContext names the closed set, SystemTextJsonSerializerRegistry answers for the trigger
// and calendar types registered with it and for whatever context the application handed in, and the
// call sites ask options.GetTypeInfo and pass the JsonTypeInfo - which is the overload that carries
// neither attribute. Quartz.Trimming.Canary runs the round trip out of a trimmed publish, so the leg
// proves the fix rather than the absence of a warning.
//
// Adding an entry is the wrong first move. Reach for it only after establishing that the reflection is
// genuinely unavoidable, and then say in the group comment why. The preferred fixes, in order:
//
//   1. Resolve the type statically — a generic parameter or a typed option instead of a type name.
//   2. Annotate with [DynamicallyAccessedMembers], so the requirement travels to the caller that does
//      know the type. Check where it propagates to first: it has to reach a caller that can satisfy it.
//   3. Mark the API [RequiresUnreferencedCode], for opt-in surface a trimmed app can avoid entirely.
//      Check where *that* propagates to as well. It cannot cross a member that implements an interface
//      Quartz does not own both sides of (IJobStore, IDriverDelegate, ISchedulerFactory all forbid it,
//      because IL2046 wants the attribute to match on both sides and RAMJobStore must stay clean), and
//      it must stop before AddQuartz — which every application calls, including the trim canary, and
//      which is trimmable when it is configured in code.
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
// unconditional form would hide it from them too. Where Quartz *has* reasoned an application's way out
// of a warning, it says so in an [UnconditionalSuppressMessage] at the call site instead — there are three,
// on JobType.FoundByName, PropertySettingJobFactory.SetObjectProperty and ListenerManagerImpl.VerifyShape,
// and each carries the argument.

// --- Types and properties named by string -----------------------------------------------------------
// Configuration and persistence both store types as text: the flat quartz.* keys, the JOB_CLASS_NAME
// column, and JobType's name-constructed form. A type resolved from a string cannot be proven
// reachable, so the trimmer cannot keep it — nor the properties a key then sets on it.
//
// The APIs an application calls to name a job type by string say [RequiresUnreferencedCode] instead,
// so a trimmed application is told; the job_scheduling_data XML loader left this file that way. What
// remains is Quartz reading its own configuration and its own tables, on paths that reach AddQuartz or
// an interface Quartz does not own both sides of, and so cannot carry the attribute themselves.

[assembly: SuppressMessage("Trimming", "IL2057", Scope = "type", Target = "T:Quartz.Impl.SimpleTypeLoader", Justification = "The default ITypeLoader exists to resolve a configured type name; that is its contract.")]
[assembly: SuppressMessage("Trimming", "IL2057", Scope = "type", Target = "T:Quartz.JobType", Justification = "A name-constructed JobType resolves through Type.GetType when the caller supplied no resolver.")]
[assembly: SuppressMessage("Trimming", "IL2057", Scope = "type", Target = "T:Quartz.Impl.AdoJobStore.StdAdoDelegate", Justification = "A persisted job's type is the JOB_CLASS_NAME column, which is a string by the time it is read back.")]
[assembly: SuppressMessage("Trimming", "IL2072", Scope = "type", Target = "T:Quartz.Impl.AdoJobStore.StdAdoDelegate", Justification = "A persisted job's type reaches JobBuilder.OfType as a Type resolved from JOB_CLASS_NAME.")]
[assembly: SuppressMessage("Trimming", "IL2057", Scope = "type", Target = "T:Quartz.Impl.AdoJobStore.Common.BuiltInDbMetadataFactory", Justification = "Only the name-resolved half of a driver description asks for a type; a provider built over a DbProviderFactory or a DbDataSource never does.")]
[assembly: SuppressMessage("Trimming", "IL2026", Scope = "type", Target = "T:Quartz.Impl.AdoJobStore.Common.ConfigurationBasedDbMetadataFactory", Justification = "A driver described by quartz.dbprovider.* keys has those keys applied to its DbMetadata by name.")]
[assembly: SuppressMessage("Trimming", "IL2067", Scope = "type", Target = "T:Quartz.Configuration.QuartzPropertyBridge", Justification = "Translating legacy quartz.* keys means constructing the component each key names.")]
[assembly: SuppressMessage("Trimming", "IL2072", Scope = "type", Target = "T:Quartz.Configuration.QuartzPropertyBridge", Justification = "Translating legacy quartz.* keys means constructing the component each key names.")]
[assembly: SuppressMessage("Trimming", "IL2026", Scope = "type", Target = "T:Quartz.Configuration.QuartzPropertyBridge", Justification = "A component with no typed options of its own takes the leftover quartz.* keys as properties set by name.")]
[assembly: SuppressMessage("Trimming", "IL2067", Scope = "type", Target = "T:Quartz.Configuration.SchedulerPluginFactory", Justification = "A plugin registered by type is constructed through the container from that Type.")]
[assembly: SuppressMessage("Trimming", "IL2026", Scope = "type", Target = "T:Quartz.Configuration.SchedulerPluginFactory", Justification = "A plugin's quartz.plugin.<name>.* keys are applied to it as properties set by name.")]
[assembly: SuppressMessage("Trimming", "IL2072", Scope = "type", Target = "T:Quartz.Configuration.PropertyListenerFactory", Justification = "A listener registered by quartz.*.listener.* keys is constructed from the type the key names.")]
[assembly: SuppressMessage("Trimming", "IL2075", Scope = "type", Target = "T:Quartz.Configuration.PropertyListenerFactory", Justification = "A listener's Name is set through the property of that name, on a type known only at runtime.")]
[assembly: SuppressMessage("Trimming", "IL2026", Scope = "type", Target = "T:Quartz.Configuration.PropertyListenerFactory", Justification = "A listener's quartz.*.listener.<name>.* keys are applied to it as properties set by name.")]
[assembly: SuppressMessage("Trimming", "IL2072", Scope = "type", Target = "T:Quartz.Configuration.JsonSchedulingHelper", Justification = "Jobs declared in configuration name their type as a string.")]

// --- Values converted onto a property the compiler cannot see -----------------------------------------
// Not a string contract, and the one wall step 3 could not move. ConvertValueIfNecessary answers the two
// questions that need no converter itself, and hands the rest to a RequiresUnreferencedCode helper: a
// converter is found by reflecting over the target type, and the target arrives as
// PropertyInfo.PropertyType or as a setter's parameter type. The framework annotates neither, and could
// not — so there is no [DynamicallyAccessedMembers] chain to build, and the callers that would have to
// carry [RequiresUnreferencedCode] instead are the default job factory and everything that runs a job.

[assembly: SuppressMessage("Trimming", "IL2026", Scope = "type", Target = "T:Quartz.Util.ObjectUtils", Justification = "TypeDescriptor.GetConverter is how a value becomes a property's value, and the property's type is not annotatable.")]
[assembly: SuppressMessage("Trimming", "IL2067", Scope = "type", Target = "T:Quartz.Util.ObjectUtils", Justification = "The default for a missing value is Activator.CreateInstance of the target type, which is only ever reached for a value type.")]

// --- Duck-typed exception inspection ------------------------------------------------------------------
// Whether a database error is worth retrying lives in provider-specific properties (SqlException.Number,
// SqliteException.SqliteErrorCode). Quartz must not reference the provider assemblies to read them.

[assembly: SuppressMessage("Trimming", "IL2070", Scope = "type", Target = "T:Quartz.Impl.AdoJobStore.TransientErrorDetector", Justification = "Retry classification reads provider error codes off exception types Quartz deliberately does not reference.")]

// --- Configuration binding ------------------------------------------------------------------------------
// IServiceCollection.Configure<TOptions>(name, section) is RequiresUnreferencedCode and
// RequiresDynamicCode both: the binder reflects over TOptions, and builds what it needs to set a
// collection or a nullable property on it. The options types are ours and closed, so the
// source-generated binder is the fix for both; that is a separate change from this baseline, and the
// one entry here that a later step can expect to delete rather than argue for.

[assembly: SuppressMessage("Trimming", "IL2026", Scope = "type", Target = "T:Quartz.Configuration.QuartzTypedOptions", Justification = "Binding the quartz configuration section reflects over the options types; the source-generated binder is the fix.")]
[assembly: SuppressMessage("AOT", "IL3050", Scope = "type", Target = "T:Quartz.Configuration.QuartzTypedOptions", Justification = "Binding the quartz configuration section generates code for the options types; the source-generated binder is the fix.")]
