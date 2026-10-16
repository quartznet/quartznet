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

// Quartz records no trim or AOT warning (https://github.com/quartznet/quartznet/issues/3341), and this
// file is where its remaining reflection lives: each switch below guards a [RequiresUnreferencedCode]
// method, and a trimmed or native publish turns every switch off. TreatWarningsAsErrors makes any IL2xxx
// or IL3xxx an error in Quartz's own compile, and build/Build.cs fails a canary publish that reports one
// against a Quartz type, so a new warning is new reflection. Fix it; there is nowhere to record it.
//
// The fixes, in order:
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
//   4. Put the reflective half behind a switch here: a private [RequiresUnreferencedCode] method called
//      only under the guard, an answer for the switch-off case that reflects over nothing, a property in
//      buildTransitive/net10.0/Quartz.targets, and the names in FeatureSwitchesTest. The analyzer
//      honours the guard on its own; ILLink and ILCompiler honour it only from the targets file's
//      setting, which is why every trimmed project in this repository imports it.
//
// An [UnconditionalSuppressMessage] ships in the assembly and silences the warning for every consumer, so
// it is only for reflection Quartz has reasoned an application's way out of, with the argument on the
// attribute: JobType.FoundByName and its IL2073 twin, PropertySettingJobFactory.SetObjectProperty,
// ListenerManagerImpl.VerifyShape, TransientErrorDetector.FindDriverProperty, and the
// IsReflectionEnabledByDefault branch of QuartzTypeInfoResolvers. Never for reflection that can break.
//
// How it got here. Until 4.6 the warnings Quartz still produced were recorded in TrimAnalysisBaseline.cs,
// as type-scoped [SuppressMessage]s the analyzer honoured, and mirrored for ILLink in
// ILLink.Suppressions.xml; neither shipped, so a consumer still saw every warning. The track's steps:
//
//   Step 3   A job type that reaches Quartz as a type — JobBuilder.Create<T>(), OfType<T>(), AddJob<T>() —
//            carries [DynamicallyAccessedMembers] to the attribute checks, the job factories and the ADO
//            store's acquisition loop, which took the fire path out of the baseline.
//   Step 4   The HTTP wire contract closed with no entry: HttpApiJsonContext states every body the API
//            exchanges. Its reflection was System.Text.Json's lazy fallback, which nothing reports.
//   Step 5   The AOT and single-file analyzers came on. The single-file analyzer found nothing; the AOT
//            analyzer found twelve IL3050s, every one on a type already listed, and two were fixed rather
//            than recorded (XmlSchedulingDataProcessor's Enum.GetValues, HttpApiJson.ReflectionResolver).
//   Step 6   Four entries on SystemTextJsonObjectSerializer and Utf8JsonWriterExtensions, the first a
//            publish did more than warn about: every persistent store threw "Reflection-based
//            serialization has been disabled" on the first trigger it wrote. QuartzStoreJsonContext and
//            SystemTextJsonSerializerRegistry fixed it, and the canary round-trips every blob.
//   Step 7   The ADO store's driver descriptions split: a registration handing over a DbProviderFactory or
//            a DbDataSource constructs nothing by name, and the name overloads say
//            [RequiresUnreferencedCode]. The canary runs a SQLite store trimmed and natively.
//   Step 8   QuartzTypedOptions' IL2026 and IL3050, the last IL3050, went with
//            EnableConfigurationBindingGenerator; built against the reflection binder, the canary passes
//            trimmed and fails natively. ConfigurationBindingIsSourceGeneratedTest holds it.
//   #4083    StdAdoDelegate's IL2072 and JsonSchedulingHelper's went to JobType.Resolve; StdAdoDelegate's
//            IL2057 moved to JobType.ResolveWithoutLoader; TransientErrorDetector's driver properties got
//            [DynamicDependency], since the trimmer had removed them and a busy SQLite database was no
//            longer retried.
//   #4087    The string-named group, fifteen entries with the four side packages' files, went behind
//            TypeNameResolution and LegacyComponentKeys: a name answers from RegisteredTypeNames first.
//   #4082    The last two, ValueConverter's IL2026 (TypeDescriptor) and IL2067 (Activator.CreateInstance
//            for a value type's default), went behind TypeConverterCoercion, with conversions that
//            reproduce TypeDescriptor's for the types job data carries. The baseline files went with them,
//            and the build's proof that ILLink and ILCompiler ran moved to the canaries' TrimWitness.

namespace Quartz;

/// <summary>
/// The three feature switches that put Quartz's remaining reflection behind a setting, so that a trimmed
/// or native publish can turn it off and have the trimmer remove it.
/// </summary>
/// <remarks>
/// <para>
/// Each switch is an <see cref="AppContext" /> switch read on every call, and each is on unless
/// something says otherwise. What says otherwise is <c>buildTransitive/net10.0/Quartz.targets</c>,
/// which every package consumer imports: under <c>PublishTrimmed</c> or <c>PublishAot</c> it sets all
/// three to <see langword="false" /> as <c>RuntimeHostConfigurationOption</c> items with <c>Trim="true"</c>,
/// which is how <c>System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault</c> is set too. An
/// application opts back in with the MSBuild properties named below.
/// </para>
/// <para>
/// The targets file is load-bearing, not decoration. ILLink and ILCompiler substitute a
/// <see cref="FeatureSwitchDefinitionAttribute" /> getter only when the publish carries a setting for
/// it; with none, the guard is ignored and every guarded call site is reported as <c>IL2026</c>. The
/// Roslyn trim analyzer is the one tool that honours <see cref="FeatureGuardAttribute" /> on its own,
/// which is why Quartz compiles clean without any setting at all.
/// </para>
/// <para>
/// <c>IL4000</c> is disabled around each getter because the analyzer wants a guard to be provably
/// <see langword="false" /> when the feature is unsupported, and a ternary over
/// <see cref="AppContext.TryGetSwitch" /> is not. The runtime's own guards of this shape —
/// <c>DefaultValueAttribute.IsSupported</c>, <c>TypeDescriptor.IsComObjectDescriptorSupported</c> — do
/// the same, for the same reason: the MSBuild side guarantees the setting in a trimmed publish. Defaulting
/// to <see langword="false" /> instead would turn the feature off for every untrimmed application.
/// </para>
/// </remarks>
internal static class FeatureSwitches
{
    /// <summary>
    /// The switch behind <see cref="IsTypeNameResolutionSupported" />.
    /// </summary>
    public const string TypeNameResolutionSwitch = "Quartz.TypeNameResolution.IsSupported";

    /// <summary>
    /// The switch behind <see cref="IsLegacyComponentKeysSupported" />.
    /// </summary>
    public const string LegacyComponentKeysSwitch = "Quartz.LegacyComponentKeys.IsSupported";

    /// <summary>
    /// The switch behind <see cref="IsTypeConverterCoercionSupported" />.
    /// </summary>
    public const string TypeConverterCoercionSwitch = "Quartz.JobData.IsTypeConverterCoercionSupported";

    /// <summary>
    /// The MSBuild property that sets <see cref="TypeNameResolutionSwitch" />.
    /// </summary>
    public const string TypeNameResolutionProperty = "QuartzTypeNameResolutionSupport";

    /// <summary>
    /// The MSBuild property that sets <see cref="LegacyComponentKeysSwitch" />.
    /// </summary>
    public const string LegacyComponentKeysProperty = "QuartzLegacyComponentKeysSupport";

    /// <summary>
    /// The MSBuild property that sets <see cref="TypeConverterCoercionSwitch" />.
    /// </summary>
    public const string TypeConverterCoercionProperty = "QuartzJobDataTypeConverterSupport";

    /// <summary>
    /// Whether a type name nothing registered may be resolved by reflection.
    /// </summary>
    /// <remarks>
    /// A name — a stored <c>JOB_CLASS_NAME</c>, a job in a schedule file or a request body, a
    /// <c>.type</c> key, a driver's type — is answered from <see cref="Impl.RegisteredTypeNames" />
    /// first, whatever this says. Off, a name the table does not hold is refused with a message naming
    /// the registration to add; on, <see cref="Type.GetType(string)" /> gets its say, as it always has.
    /// </remarks>
    [FeatureSwitchDefinition(TypeNameResolutionSwitch)]
    [FeatureGuard(typeof(RequiresUnreferencedCodeAttribute))]
#pragma warning disable IL4000 // True for a JIT application; Quartz.targets sets it false under PublishTrimmed, where the guard has to hold.
    public static bool IsTypeNameResolutionSupported =>
        AppContext.TryGetSwitch(TypeNameResolutionSwitch, out bool enabled) ? enabled : true;
#pragma warning restore IL4000

    /// <summary>
    /// Whether a flat <c>quartz.*</c> key may name a component by type, or set a component's
    /// properties by string.
    /// </summary>
    /// <remarks>
    /// Off, such a key fails <c>AddQuartz</c> with a <see cref="SchedulerConfigException" /> naming the
    /// code form, except for Quartz's own spellings of <c>quartz.jobStore.type</c>,
    /// <c>quartz.threadPool.type</c>, <c>quartz.serializer.type</c> and <c>SYS_PROP</c>, which are
    /// mapped to the types they name with no reflection. Value keys bind as before.
    /// </remarks>
    [FeatureSwitchDefinition(LegacyComponentKeysSwitch)]
    [FeatureGuard(typeof(RequiresUnreferencedCodeAttribute))]
#pragma warning disable IL4000 // As above.
    public static bool IsLegacyComponentKeysSupported =>
        AppContext.TryGetSwitch(LegacyComponentKeysSwitch, out bool enabled) ? enabled : true;
#pragma warning restore IL4000

    /// <summary>
    /// Whether a value no built-in conversion handles may be coerced onto a property through
    /// <see cref="System.ComponentModel.TypeConverter" />, and a value type's default made with
    /// <see cref="Activator.CreateInstance(Type)" />.
    /// </summary>
    /// <remarks>
    /// <see cref="Util.ValueConverter" /> answers the conversions job data needs in practice first,
    /// whatever this says: a string to a primitive, <see cref="decimal" />, an enum,
    /// <see cref="Guid" />, <see cref="TimeSpan" />, <see cref="DateTime" />,
    /// <see cref="DateTimeOffset" />, <see cref="Uri" /> or the <see cref="Nullable{T}" /> of one, a number
    /// to a number or an enum, and the way back. Off, any other conversion is refused with a
    /// <see cref="NotSupportedException" /> naming the two types and this switch, and a missing value for
    /// a struct property is the struct's zeroed default rather than what its parameterless constructor
    /// makes.
    /// </remarks>
    [FeatureSwitchDefinition(TypeConverterCoercionSwitch)]
    [FeatureGuard(typeof(RequiresUnreferencedCodeAttribute))]
#pragma warning disable IL4000 // As above.
    public static bool IsTypeConverterCoercionSupported =>
        AppContext.TryGetSwitch(TypeConverterCoercionSwitch, out bool enabled) ? enabled : true;
#pragma warning restore IL4000
}
