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

using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using Quartz.Configuration;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// The one place in Quartz that writes a property whose name is a string.
/// </summary>
/// <remarks>
/// Everything here used to live on <c>ObjectUtils</c>, where <c>SetPropertyValue</c> was public and had
/// no caller outside the class (#3432). It is private now, so these go through the entry point the four
/// configuration callers use.
/// </remarks>
/// <author>Marko Lahma (.NET)</author>
public class PropertyBinderTest
{
    [Test]
    public void EveryEntryPointSaysItBindsByName()
    {
        List<string> unannotated = typeof(PropertyBinder)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttribute<RequiresUnreferencedCodeAttribute>() is null)
            .Select(method => method.Name)
            .ToList();

        unannotated.Should().BeEmpty(
            "the annotation is what makes this a named seam rather than a helper with its warnings waived in "
            + "TrimAnalysisBaseline.cs, and the file records no entry against this type on the strength of it");
    }

    [Test]
    public void ADurationIsReadInTheUnitsItsParseRuleNames()
    {
        DurationComponent component = new DurationComponent();

        PropertyBinder.SetObjectProperties(component, new NameValueCollection
        {
            ["TimeHours"] = "1",
            ["TimeMinutes"] = "1",
            ["TimeSeconds"] = "1",
            ["TimeMilliseconds"] = "1",
            ["TimeDefault"] = "1",
        });

        component.TimeHours.Should().Be(TimeSpan.FromHours(1));
        component.TimeMinutes.Should().Be(TimeSpan.FromMinutes(1));
        component.TimeSeconds.Should().Be(TimeSpan.FromSeconds(1));
        component.TimeMilliseconds.Should().Be(TimeSpan.FromMilliseconds(1));
        component.TimeDefault.Should().Be(TimeSpan.FromDays(1), "a property with no parse rule takes whatever TimeSpan parses");
    }

    /// <summary>
    /// <c>ObjectUtils.SetPropertyValue fails with explicitly implemented interface members</c> was fixed
    /// in 2.0.1, and the interface search that fixed it is still the only way to reach one.
    /// </summary>
    [Test]
    public void APropertyIsFoundOnAnInterfaceWhenTheTypeImplementsItExplicitly()
    {
        ExplicitImplementor component = new ExplicitImplementor();

        PropertyBinder.SetObjectProperties(component, new NameValueCollection { ["InstanceName"] = "instance" });

        component.ObservedInstanceName.Should().Be("instance",
            "an explicit implementation is not on the type's own property list under that name, so the "
            + "interface search is the only thing that can find it");
    }

    /// <summary>
    /// The binding flags include <see cref="BindingFlags.NonPublic" /> on purpose: a shipped component's
    /// settings are public on its options type and internal on the component, and the flat
    /// <c>quartz.plugin.*</c> and <c>quartz.jobStore.lockHandler.*</c> keys write the component itself.
    /// </summary>
    [Test]
    public void APropertyThatIsNotPublicIsStillBound()
    {
        DurationComponent component = new DurationComponent();

        PropertyBinder.SetObjectProperties(component, new NameValueCollection { ["Marker"] = "configured" });

        component.Marker.Should().Be("configured", "an option that is internal on the component is exactly what a flat key has to reach");
    }

    [Test]
    public void ASetterThatIsNotPublicIsStillBound()
    {
        DurationComponent component = new DurationComponent();

        PropertyBinder.SetObjectProperties(component, new NameValueCollection { ["Note"] = "configured" });

        component.Note.Should().Be("configured", "the setter is asked for with nonPublic: true, which is also what reaches an init accessor");
    }

    [Test]
    public void AKeyNamingNoPropertyIsReportedWithTheKeyInIt()
    {
        Action binding = () => PropertyBinder.SetObjectProperties(
            new DurationComponent(),
            new NameValueCollection { ["NoSuchThing"] = "1" });

        binding.Should().Throw<SchedulerConfigException>()
            .WithMessage("*NoSuchThing*", "a misspelled configuration key is only findable if the message names it");
    }

    [Test]
    public void TheTypeKeyIsNotItselfAProperty()
    {
        DurationComponent component = new DurationComponent();

        Action binding = () => PropertyBinder.SetObjectProperties(component, new NameValueCollection
        {
            ["type"] = "Some.Component, Some.Assembly",
            ["Note"] = "configured",
        });

        binding.Should().NotThrow("the key that chose the component is not one of the properties to set on it");
        component.Note.Should().Be("configured");
    }

    private sealed class DurationComponent
    {
        [TimeSpanParseRule(TimeSpanParseRule.Hours)]
        public TimeSpan TimeHours { get; set; }

        [TimeSpanParseRule(TimeSpanParseRule.Minutes)]
        public TimeSpan TimeMinutes { get; set; }

        [TimeSpanParseRule(TimeSpanParseRule.Seconds)]
        public TimeSpan TimeSeconds { get; set; }

        [TimeSpanParseRule(TimeSpanParseRule.Milliseconds)]
        public TimeSpan TimeMilliseconds { get; set; }

        public TimeSpan TimeDefault { get; set; }

        internal string Marker { get; set; }

        public string Note { get; internal set; }
    }

    private interface INamedComponent
    {
        string InstanceName { get; set; }
    }

    /// <summary>
    /// The shape the 2.0.1 bug report was about: the settable property exists only as an explicit
    /// implementation, so it is named <c>…INamedComponent.InstanceName</c> on the type itself.
    /// </summary>
    private sealed class ExplicitImplementor : INamedComponent
    {
        string INamedComponent.InstanceName { get; set; }

        public string ObservedInstanceName => ((INamedComponent) this).InstanceName;
    }
}
