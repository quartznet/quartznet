#nullable enable

using System.Collections.Specialized;
using System.Reflection;
using System.Text;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Quartz.Configuration;
using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Plugins.Json;
using Quartz.Plugins.Xml;
using Quartz.Xml;

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
    /// The two formats declare a trigger's execution group, retry policy and preferred node in the same
    /// words, and produce the same trigger from them.
    /// </summary>
    /// <remarks>
    /// These three were JSON-only until 4.1 — the XML schema had no element for any of them — so this is
    /// where the catching-up is held. A parity test is the only thing that keeps two readers of one
    /// concept honest: each of them alone can be entirely self-consistent and still disagree with the
    /// other about what <c>*</c> means.
    /// </remarks>
    [Test]
    public async Task BothFormatsDeclareTheSameTriggerSettings()
    {
        List<ITrigger> xml = await ReadXml($"""
            <?xml version="1.0" encoding="UTF-8"?>
            <job-scheduling-data xmlns="http://quartznet.sourceforge.net/JobSchedulingData" version="2.0">
              <schedule>
                <job>
                  <name>job1</name>
                  <job-type>{JobType}</job-type>
                </job>
                <trigger>
                  <cron>
                    <name>pinned</name>
                    <job-name>job1</job-name>
                    <execution-group>batch</execution-group>
                    <retry-policy>fixed;3;00:00:30</retry-policy>
                    <preferred-node>production-node-1</preferred-node>
                    <cron-expression>0 0 12 * * ?</cron-expression>
                  </cron>
                </trigger>
                <trigger>
                  <cron>
                    <name>autoPinned</name>
                    <job-name>job1</job-name>
                    <preferred-node>*</preferred-node>
                    <cron-expression>0 0 12 * * ?</cron-expression>
                  </cron>
                </trigger>
                <trigger>
                  <cron>
                    <name>unpinned</name>
                    <job-name>job1</job-name>
                    <cron-expression>0 0 12 * * ?</cron-expression>
                  </cron>
                </trigger>
              </schedule>
            </job-scheduling-data>
            """);

        List<ITrigger> json = ReadJson($$"""
            {
              "Schedule": {
                "Jobs": [{ "Name": "job1", "JobType": "{{JobType}}" }],
                "Triggers": [
                  {
                    "Name": "pinned", "JobName": "job1",
                    "ExecutionGroup": "batch",
                    "RetryPolicy": "fixed;3;00:00:30",
                    "PreferredNode": "production-node-1",
                    "Cron": { "Expression": "0 0 12 * * ?" }
                  },
                  {
                    "Name": "autoPinned", "JobName": "job1",
                    "PreferredNode": "*",
                    "Cron": { "Expression": "0 0 12 * * ?" }
                  },
                  {
                    "Name": "unpinned", "JobName": "job1",
                    "Cron": { "Expression": "0 0 12 * * ?" }
                  }
                ]
              }
            }
            """);

        Settings(xml).Should().Equal(Settings(json),
            "the two formats are one feature spelled twice, so a trigger declared the same way in each "
            + "has to come out the same - including what '*' means, which is an automatic pin and not a "
            + "node named '*'");

        Settings(xml).Should().Equal(
            [
                "pinned: group=batch, retry=fixed;3;00:00:30, node=production-node-1, auto=False",
                "autoPinned: group=, retry=, node=, auto=True",
                "unpinned: group=, retry=, node=, auto=False",
            ],
            "and the settings they agree on are the ones the documents state");
    }

    private const string JobType = "Quartz.Jobs.NoOpJob, Quartz.Jobs";

    private static List<string> Settings(List<ITrigger> triggers)
    {
        return triggers
            .Select(x => $"{x.Key.Name}: group={x.ExecutionGroup}, retry={x.RetryPolicy}, node={x.PreferredNode.Node}, auto={x.PreferredNode.IsAutomatic}")
            .ToList();
    }

    private static async Task<List<ITrigger>> ReadXml(string document)
    {
        ExposedXmlProcessor processor = new();
        await processor.ProcessStream(new MemoryStream(Encoding.UTF8.GetBytes(document)), systemId: null);
        return processor.Triggers;
    }

    private static List<ITrigger> ReadJson(string document)
    {
        JsonSchedulingDataProcessor processor = new(
            NullLogger<JsonSchedulingDataProcessor>.Instance,
            new SimpleTypeLoader(),
            TimeProvider.System);

        processor.ProcessJsonContent(document);
        return [.. processor.ParsedTriggers];
    }

    /// <summary>
    /// The XML processor keeps what it loaded for its subclasses, which is what the JSON one is.
    /// </summary>
    private sealed class ExposedXmlProcessor : XmlSchedulingDataProcessor
    {
        public ExposedXmlProcessor()
            : base(NullLogger<XmlSchedulingDataProcessor>.Instance, new SimpleTypeLoader(), TimeProvider.System)
        {
        }

        public List<ITrigger> Triggers => LoadedTriggers;
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
