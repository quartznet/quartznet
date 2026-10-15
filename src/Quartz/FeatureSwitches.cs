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

namespace Quartz;

/// <summary>
/// The two feature switches that put Quartz's remaining reflection behind a setting, so that a trimmed
/// or native publish can turn it off and have the trimmer remove it.
/// </summary>
/// <remarks>
/// <para>
/// Each switch is an <see cref="AppContext" /> switch read on every call, and each is on unless
/// something says otherwise. What says otherwise is <c>buildTransitive/net10.0/Quartz.targets</c>,
/// which every package consumer imports: under <c>PublishTrimmed</c> or <c>PublishAot</c> it sets both
/// to <see langword="false" /> as <c>RuntimeHostConfigurationOption</c> items with <c>Trim="true"</c>,
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
    /// The MSBuild property that sets <see cref="TypeNameResolutionSwitch" />.
    /// </summary>
    public const string TypeNameResolutionProperty = "QuartzTypeNameResolutionSupport";

    /// <summary>
    /// The MSBuild property that sets <see cref="LegacyComponentKeysSwitch" />.
    /// </summary>
    public const string LegacyComponentKeysProperty = "QuartzLegacyComponentKeysSupport";

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
}
