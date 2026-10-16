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

#nullable enable

using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Quartz.Tests.Unit;

/// <summary>
/// The three feature switches: what they read as, and that the names the code declares are the names
/// the packed targets file sets.
/// </summary>
/// <remarks>
/// The targets file is the public contract — it is what a consumer's <c>PublishTrimmed</c> publish gets
/// and what the <c>QuartzTypeNameResolutionSupport</c>, <c>QuartzLegacyComponentKeysSupport</c> and
/// <c>QuartzJobDataTypeConverterSupport</c> properties are documented against — so a switch renamed in one
/// place and not the other would ship a publish that keeps every guarded call and reports it.
/// Non-parallelizable because two of the tests flip a process-wide switch.
/// </remarks>
[NonParallelizable]
public sealed class FeatureSwitchesTest
{
    private static readonly string[] SwitchNames =
    [
        FeatureSwitches.TypeNameResolutionSwitch,
        FeatureSwitches.LegacyComponentKeysSwitch,
        FeatureSwitches.TypeConverterCoercionSwitch,
    ];

    private static readonly string[] PropertyNames =
    [
        FeatureSwitches.TypeNameResolutionProperty,
        FeatureSwitches.LegacyComponentKeysProperty,
        FeatureSwitches.TypeConverterCoercionProperty,
    ];

    [Test]
    public void EverySwitchIsOnByDefault()
    {
        FeatureSwitches.IsTypeNameResolutionSupported.Should().BeTrue(
            "an untrimmed application resolves type names by reflection as it always has");
        FeatureSwitches.IsLegacyComponentKeysSupported.Should().BeTrue(
            "an untrimmed application reads every quartz.* key as it always has");
        FeatureSwitches.IsTypeConverterCoercionSupported.Should().BeTrue(
            "an untrimmed application converts job data through TypeDescriptor as it always has");
    }

    [Test]
    public void ASwitchSetOffIsReadBack()
    {
        using (new FeatureSwitchScope(FeatureSwitches.TypeNameResolutionSwitch, enabled: false))
        {
            FeatureSwitches.IsTypeNameResolutionSupported.Should().BeFalse(
                "the switch is read on every call, which is what lets a test flip it");
            FeatureSwitches.IsLegacyComponentKeysSupported.Should().BeTrue("the switches are independent");
            FeatureSwitches.IsTypeConverterCoercionSupported.Should().BeTrue("the switches are independent");
        }

        using (new FeatureSwitchScope(FeatureSwitches.LegacyComponentKeysSwitch, enabled: false))
        {
            FeatureSwitches.IsLegacyComponentKeysSupported.Should().BeFalse();
            FeatureSwitches.IsTypeNameResolutionSupported.Should().BeTrue();
            FeatureSwitches.IsTypeConverterCoercionSupported.Should().BeTrue();
        }

        using (new FeatureSwitchScope(FeatureSwitches.TypeConverterCoercionSwitch, enabled: false))
        {
            FeatureSwitches.IsTypeConverterCoercionSupported.Should().BeFalse();
            FeatureSwitches.IsTypeNameResolutionSupported.Should().BeTrue();
            FeatureSwitches.IsLegacyComponentKeysSupported.Should().BeTrue();
        }
    }

    [Test]
    public void EachGetterDeclaresItsSwitchAndGuardsRequiresUnreferencedCode()
    {
        foreach ((string property, string switchName) in new[]
                 {
                     (nameof(FeatureSwitches.IsTypeNameResolutionSupported), FeatureSwitches.TypeNameResolutionSwitch),
                     (nameof(FeatureSwitches.IsLegacyComponentKeysSupported), FeatureSwitches.LegacyComponentKeysSwitch),
                     (nameof(FeatureSwitches.IsTypeConverterCoercionSupported), FeatureSwitches.TypeConverterCoercionSwitch),
                 })
        {
            PropertyInfo getter = typeof(FeatureSwitches).GetProperty(property, BindingFlags.Public | BindingFlags.Static)!;

            getter.GetCustomAttribute<FeatureSwitchDefinitionAttribute>()!.SwitchName.Should().Be(switchName,
                "the definition is what ILLink and ILCompiler substitute from the runtime host configuration option of that name");
            getter.GetCustomAttribute<FeatureGuardAttribute>()!.FeatureType.Should().Be(typeof(RequiresUnreferencedCodeAttribute),
                "the guard is what lets the trim analyzer accept a RequiresUnreferencedCode call behind the getter");
        }
    }

    [Test]
    public void ThePackedTargetsFileSetsEverySwitchByItsDeclaredName()
    {
        string targets = File.ReadAllText(Path.Combine(RepositoryRoot.Find().FullName, "src", "Quartz", "buildTransitive", "net10.0", "Quartz.targets"));

        foreach (string switchName in SwitchNames)
        {
            targets.Should().Contain($"<RuntimeHostConfigurationOption Include=\"{switchName}\"",
                "a switch the targets file does not set is one no trimmed consumer ever turns off, and every guarded call is then reported");
        }

        foreach (string propertyName in PropertyNames)
        {
            targets.Should().Contain($"<{propertyName} ",
                "the property is how an application opts back in, and the documentation names it");
            targets.Should().Contain($"Value=\"$({propertyName})\"",
                "the option carries whatever the property says, so true turns the feature back on");
        }

        targets.Should().Contain("'$(PublishTrimmed)' == 'true' Or '$(PublishAot)' == 'true'",
            "PublishTrimmed is derived from PublishAot only in targets that import after this file");
        targets.Should().Contain("Trim=\"true\"",
            "without Trim the option reaches the runtimeconfig and never ILLink, which then keeps the guarded code");
    }
}
