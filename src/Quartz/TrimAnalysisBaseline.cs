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
// It is a baseline, not an all-clear: none of these call sites is trim-safe. There is no IL3050 left in
// it, though, and that is not an accident of what has been looked at - Quartz needs no runtime code
// generation anywhere, which is the whole of what IsAotCompatible in Quartz.csproj claims. What the
// baseline buys is that a *new* warning of either kind fails the build, because TreatWarningsAsErrors
// makes an IL2xxx or an IL3xxx an error and nothing suppresses a warning that is not listed here.
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
// Step 8 deleted the last two - the IL2026 and IL3050 on QuartzTypedOptions - and with them the last
// IL3050 anywhere in Quartz, which is what let the package say IsAotCompatible. Their own justification
// had named the fix, and it is EnableConfigurationBindingGenerator in Quartz.csproj: the compiler
// intercepts the six Configure<TOptions>(name, section) calls and writes a binder for each options type,
// so nothing reflects over one and nothing is generated at runtime. That entry, like step 6's, recorded
// a warning a publish did more than warn about - the trim canary now binds a whole scheduler out of an
// IConfiguration, and built against the reflection binder it passes trimmed and fails natively, with
// MaxBatchSize, ShutdownJobInterruption and the whole scheduler context arriving as defaults.
//
// After the eight steps, three entries went with no feature switch and nothing any application sees.
// StdAdoDelegate's IL2072 and JsonSchedulingHelper's were a type the scheduler's ITypeLoader had already
// resolved, handed to JobBuilder.OfType without the annotation; both now ask JobType.Resolve, which is
// the FoundByName fork the acquisition loop already used. StdAdoDelegate's IL2057 moved rather than
// went: it is the runtime's lookup behind a loader that refuses a stored name, which a loader answering
// only for the names it knows does by contract, so the fallback stays and lives in
// JobType.ResolveWithoutLoader.
//
// Step 9 (#4068) took the whole string-named group - the first group this file ever had - out from
// behind two feature switches, declared in FeatureSwitches.cs and set false under PublishTrimmed and
// PublishAot by buildTransitive/net10.0/Quartz.targets. A type name is answered first from
// RegisteredTypeNames, the table every typed API feeds - JobType(Type), AddJobType<T>(),
// TypeLoaderOptions.Map - and by Type.GetType only while Quartz.TypeNameResolution.IsSupported is on; a
// flat quartz.* key that names a component by type, or sets one's properties by string, is read only
// while Quartz.LegacyComponentKeys.IsSupported is on, with Quartz's own spellings of a store, a thread
// pool, a serializer and SYS_PROP mapped to a typeof instead. The reflective half of each site is a
// private [RequiresUnreferencedCode] method, and the guard is the call to it; the Roslyn analyzer honours
// the guard on its own, which is why nothing below records it, and ILLink and ILCompiler honour it only
// from the setting, which is why the targets file is load-bearing and the ILLink.Suppressions.xml beside
// this one says so. That took eleven entries, and with them the four side packages' baseline files -
// each recorded one IL2026 at a call into JobDetailDto.AsIJobDetail or a schedule-file processor, which
// said RequiresUnreferencedCode only because of the cast from a string, and says it no longer.
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
// of a warning, it says so in an [UnconditionalSuppressMessage] at the call site instead — there are four,
// on JobType.FoundByName, PropertySettingJobFactory.SetObjectProperty, ListenerManagerImpl.VerifyShape and
// TransientErrorDetector.FindDriverProperty, and each carries the argument.

// --- Types and properties named by string -----------------------------------------------------------
// Gone, behind the two switches step 9 above describes. The group used to read: configuration and
// persistence both store types as text - the flat quartz.* keys, the JOB_CLASS_NAME column, JobType's
// name-constructed form - and a type resolved from a string cannot be proven reachable, so the trimmer
// cannot keep it, nor the properties a key then sets on it. That is still true of the reflective half,
// which is why it is [RequiresUnreferencedCode] and behind a guard rather than annotated; what changed is
// that a name is answered from the registered types before the runtime is asked, and that a trimmed
// publish never asks the runtime at all. Do not bring the group back by calling Type.GetType, or
// ActivatorUtilities.CreateInstance over a Type a string produced, outside one of those guarded
// methods: the compile fails on it, which is the point.

// --- Values converted onto a property the compiler cannot see -----------------------------------------
// Not a string contract, and the one wall step 3 could not move. ConvertValueIfNecessary answers the two
// questions that need no converter itself, and hands the rest to a RequiresUnreferencedCode helper: a
// converter is found by reflecting over the target type, and the target arrives as
// PropertyInfo.PropertyType or as a setter's parameter type. The framework annotates neither, and could
// not — so there is no [DynamicallyAccessedMembers] chain to build, and the callers that would have to
// carry [RequiresUnreferencedCode] instead are the default job factory and everything that runs a job.
//
// Both entries were on Quartz.Util.ObjectUtils until #3432 dissolved it. What binds a property by name
// is Quartz.Configuration.PropertyBinder now, and it is [RequiresUnreferencedCode] on every member, so
// it appears nowhere in this file — the two below are the coercion the fire path shares with it, which
// is the only part of the old type that could not say so for itself.

[assembly: SuppressMessage("Trimming", "IL2026", Scope = "type", Target = "T:Quartz.Util.ValueConverter", Justification = "TypeDescriptor.GetConverter is how a value becomes a property's value, and the property's type is not annotatable: it arrives as PropertyInfo.PropertyType, which the framework does not annotate and could not.")]
[assembly: SuppressMessage("Trimming", "IL2067", Scope = "type", Target = "T:Quartz.Util.ValueConverter", Justification = "The default for a missing value is Activator.CreateInstance of the target type, reached only when that type is a value type - and the type is the same unannotatable PropertyInfo.PropertyType the entry above is about.")]

// Duck-typed exception inspection was a group here too, and it is gone rather than re-justified: the
// trimmer did remove the driver properties TransientErrorDetector reads, and a SQLite store then gave
// up on a busy database instead of retrying it. FindDriverProperty names each of them with
// [DynamicDependency] and says why that makes its own IL2070 safe, so it is one of the call-site
// suppressions above rather than a line here.
//
// Configuration binding was the last group in this file; step 8 above says where it went. Do not bring
// it back by writing a Configure<TOptions>(section) call the generator cannot intercept, or an options
// member it cannot bind - ConfigurationBindingIsSourceGeneratedTest holds both.
