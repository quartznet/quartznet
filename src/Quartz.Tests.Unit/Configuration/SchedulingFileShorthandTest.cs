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

using Microsoft.Extensions.DependencyInjection;

using Quartz.Configuration;
using Quartz.Extensibility;
using Quartz.Plugins.Json;
using Quartz.Plugins.Xml;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// <c>UseXmlSchedulingConfiguration("jobs.xml")</c> and its JSON twin say what the callback form says.
/// </summary>
/// <remarks>
/// Every sample of the commonest case — one schedule file, read once — was
/// <c>options =&gt; options.Files.Add("~/quartz_jobs.json")</c>, which is a get-only collection being
/// mutated inside a callback to say a file name. The shorthand adds to the same list rather than
/// replacing it, so it composes with the callback form and with itself, which is what these hold.
/// </remarks>
public sealed class SchedulingFileShorthandTest
{
    [Test]
    public void TheShorthandAndTheCallbackProduceTheSameFileList()
    {
        Files<XmlSchedulingDataProcessorPlugin>(q => q.UseXmlSchedulingConfiguration("jobs.xml"))
            .Should().Be(Files<XmlSchedulingDataProcessorPlugin>(q => q.UseXmlSchedulingConfiguration(options => options.Files.Add("jobs.xml"))));

        Files<JsonSchedulingDataProcessorPlugin>(q => q.UseJsonSchedulingConfiguration("jobs.json"))
            .Should().Be(Files<JsonSchedulingDataProcessorPlugin>(q => q.UseJsonSchedulingConfiguration(options => options.Files.Add("jobs.json"))));
    }

    [Test]
    public void SeveralFilesAreAllLoaded()
    {
        Files<XmlSchedulingDataProcessorPlugin>(q => q.UseXmlSchedulingConfiguration("first.xml", "second.xml"))
            .Should().Be("first.xml,second.xml",
                "the parameter is params, so naming the files is the whole of what a caller has to write");
    }

    [Test]
    public void TheShorthandAddsToTheListRatherThanReplacingIt()
    {
        Files<JsonSchedulingDataProcessorPlugin>(q => q
                .UseJsonSchedulingConfiguration(options => options.ScanInterval = TimeSpan.FromMinutes(1))
                .UseJsonSchedulingConfiguration("late.json"))
            .Should().Be("late.json",
                "Files is get-only for the reason every Quartz options collection is - one configure "
                + "callback must not discard what another added - and the shorthand is a callback like "
                + "any other");
    }

    [Test]
    public void NamingNoFileAtAllIsRefused()
    {
        Action act = () => new ServiceCollection().AddQuartz(q => q.UseXmlSchedulingConfiguration());

        act.Should().Throw<ArgumentException>(
                "the shorthand exists to name files, so an empty call is a caller who meant the callback "
                + "overload and got a plugin loading whatever the plugin's own default file is")
            .WithMessage("*configure callback*");
    }

    /// <summary>
    /// The comma-joined file list the plugin was configured with, which is how it spells the option.
    /// </summary>
    private static string? Files<TPlugin>(Action<IQuartzBuilder> configure) where TPlugin : ISchedulerPlugin
    {
        ServiceCollection services = new();
        services.AddQuartz(configure);

        using ServiceProvider provider = services.BuildServiceProvider();

        SchedulerKey key = new(null);
        return SchedulerPluginFactory.Create(
                provider,
                provider.GetSchedulerServices<ISchedulerPlugin>(key.Key),
                provider.GetSchedulerProperties(key.OptionsName),
                key)
            .Select(x => x.Plugin)
            .OfType<TPlugin>()
            .Single() switch
        {
            XmlSchedulingDataProcessorPlugin xml => xml.FileNames,
            JsonSchedulingDataProcessorPlugin json => json.FileNames,
            var other => throw new InvalidOperationException($"{other.GetType()} has no file list"),
        };
    }
}
