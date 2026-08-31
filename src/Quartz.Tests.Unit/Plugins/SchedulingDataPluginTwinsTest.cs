#nullable enable

using System.Collections.Specialized;
using System.Reflection;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Configuration;
using Quartz.Extensibility;
using Quartz.Plugins.Json;
using Quartz.Plugins.Xml;

namespace Quartz.Tests.Unit.Plugin;

/// <summary>
/// The XML and JSON scheduling-data plugins do the same thing to two file formats, so they have one
/// surface: the same members, and the same settings reachable the same way.
/// </summary>
/// <remarks>
/// They had drifted apart. The XML one published <c>ProcessFile</c> and no <c>Shutdown</c>, the JSON
/// one a do-nothing <c>Shutdown</c> and no <c>ProcessFile</c>, and both published the settings the
/// <c>quartz.plugin.&lt;name&gt;.*</c> keys write as public get-only properties — readable by an
/// application that could do nothing with them, and different from the options type that configures
/// them in code. The settings are internal now, which is what the second test here is about: the
/// flat keys write non-public setters through reflection, so closing the public surface must not
/// close the configuration path.
/// </remarks>
public sealed class SchedulingDataPluginTwinsTest
{
    [Test]
    public void TheTwinsPublishTheSameMembers()
    {
        List<string> xml = PublicSurface(typeof(XmlSchedulingDataProcessorPlugin));
        List<string> json = PublicSurface(typeof(JsonSchedulingDataProcessorPlugin));

        xml.Should().Equal(json,
            "the two plugins differ in the format they read and in nothing else, so a member on one of "
            + "them is a member on the other or it is on neither");

        xml.Should().Equal(
            [
                ".ctor()",
                ".ctor(ILogger`1, ITypeLoader, TimeProvider)",
                "FileUpdated(String, CancellationToken)",
                "Initialize(String, IScheduler, CancellationToken)",
                "Start(CancellationToken)",
            ],
            "the surface is the two constructors and what ISchedulerPlugin and IFileScanListener ask "
            + "for; everything else is either configuration, which belongs to FileSchedulingOptions, "
            + "or the plugin's own bookkeeping");
    }

    [Test]
    public void FlatKeysStillConfigureBothTwins()
    {
        ServiceCollection services = new();
        services.AddQuartz(new NameValueCollection
        {
            ["quartz.plugin.xml.type"] = typeof(XmlSchedulingDataProcessorPlugin).AssemblyQualifiedName,
            ["quartz.plugin.xml.fileNames"] = "first.xml, second.xml",
            ["quartz.plugin.xml.scanInterval"] = "30",
            ["quartz.plugin.xml.failOnFileNotFound"] = "false",
            ["quartz.plugin.xml.failOnSchedulingError"] = "true",

            ["quartz.plugin.json.type"] = typeof(JsonSchedulingDataProcessorPlugin).AssemblyQualifiedName,
            ["quartz.plugin.json.fileNames"] = "first.json, second.json",
            ["quartz.plugin.json.scanInterval"] = "45",
            ["quartz.plugin.json.failOnFileNotFound"] = "false",
            ["quartz.plugin.json.failOnSchedulingError"] = "true",
        });

        using ServiceProvider provider = services.BuildServiceProvider();

        XmlSchedulingDataProcessorPlugin xml = Plugin<XmlSchedulingDataProcessorPlugin>(provider);
        xml.FileNames.Should().Be("first.xml, second.xml",
            "the settings are internal now, and the property binder reaches a non-public setter — so a "
            + "quartz.plugin.<name>.* key that worked before still writes the plugin");
        xml.ScanInterval.Should().Be(TimeSpan.FromSeconds(30),
            "the scan interval is read in seconds, which is what [TimeSpanParseRule] on the internal "
            + "property says");
        xml.FailOnFileNotFound.Should().BeFalse();
        xml.FailOnSchedulingError.Should().BeTrue();

        JsonSchedulingDataProcessorPlugin json = Plugin<JsonSchedulingDataProcessorPlugin>(provider);
        json.FileNames.Should().Be("first.json, second.json");
        json.ScanInterval.Should().Be(TimeSpan.FromSeconds(45));
        json.FailOnFileNotFound.Should().BeFalse();
        json.FailOnSchedulingError.Should().BeTrue();
    }

    /// <summary>
    /// The members a consumer of the package can see, formatted so that the two plugins' constructors
    /// compare equal — <c>ILogger&lt;T&gt;</c> is the one place the twins are each named after themselves.
    /// </summary>
    private static List<string> PublicSurface(Type type)
    {
        List<string> members = [];

        foreach (MemberInfo member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            members.Add(member switch
            {
                MethodBase method => $"{method.Name}({string.Join(", ", method.GetParameters().Select(x => x.ParameterType.Name))})",
                _ => $"{member.MemberType} {member.Name}",
            });
        }

        members.Sort(StringComparer.Ordinal);
        return members;
    }

    /// <summary>
    /// The plugin of the given type a scheduler ends up with, built the way the scheduler builds it.
    /// </summary>
    private static T Plugin<T>(IServiceProvider provider) where T : ISchedulerPlugin
    {
        SchedulerKey key = new(Key: null);
        return SchedulerPluginFactory.Create(
                provider,
                provider.GetSchedulerServices<ISchedulerPlugin>(key.Key),
                provider.GetSchedulerProperties(key.OptionsName),
                key)
            .Select(x => x.Plugin)
            .OfType<T>()
            .Single();
    }
}
