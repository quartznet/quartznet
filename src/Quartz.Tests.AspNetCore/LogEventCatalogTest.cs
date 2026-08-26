using System.Reflection;

using Microsoft.Extensions.Logging;

namespace Quartz.Tests.AspNetCore;

/// <summary>
/// The catalogue of log events a package raises: every <c>[LoggerMessage]</c> method, its event id,
/// its level and its message template.
/// </summary>
/// <remarks>
/// <para>
/// The companion test in <c>Quartz.Tests.Unit</c> covers the packages that do not need ASP.NET Core to
/// reference, and explains what the catalogue is for. This one covers <c>Quartz.AspNetCore</c>, and
/// holds the range that package draws from — each test owns the ranges for the assemblies it snapshots,
/// so no range is written down twice and neither copy can go stale against the other.
/// </para>
/// <para>
/// <c>Quartz.Dashboard</c> is not here: it raises no events at all. It is still in
/// <c>LogCallSiteTest.Converted</c>, which is what says it may not start raising them the plain way.
/// </para>
/// </remarks>
public class LogEventCatalogTest
{
    /// <summary>
    /// Who owns which event ids. Every id an assembly raises must fall inside a range recorded here
    /// against that assembly, which is also what keeps it out of another package's reserved range.
    /// 1000-8999 belong to the packages the companion test in <c>Quartz.Tests.Unit</c> covers.
    /// </summary>
    private static readonly EventIdRange[] Ranges =
    [
        new("Quartz.AspNetCore", 9000, 9099, "HTTP API"),
    ];

    /// <summary>
    /// The assemblies whose catalogue is snapshotted.
    /// </summary>
    private static readonly Assembly[] Catalogued =
    [
        typeof(global::Quartz.QuartzAspNetCoreConfigurationExtensions).Assembly,
    ];

    private static IEnumerable<TestCaseData> Assemblies()
    {
        foreach (Assembly assembly in Catalogued)
        {
            yield return new TestCaseData(assembly).SetName(assembly.GetName().Name);
        }
    }

    [TestCaseSource(nameof(Assemblies))]
    public async Task LogEventCatalogHasNotChangedUnintentionally(Assembly assembly)
    {
        string name = assembly.GetName().Name!;
        List<LogEvent> events = Read(assembly);

        events.Should().NotBeEmpty(
            $"{name} is listed as having a catalogue, so reflecting over it must find [LoggerMessage] methods "
            + "- an empty result means the query stopped working, not that the package stopped logging");

        List<IGrouping<int, LogEvent>> collisions = events.GroupBy(x => x.EventId).Where(x => x.Count() > 1).ToList();

        collisions.Should().BeEmpty(
            "an event id identifies one event to whoever filters on it, so two events cannot share one:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, collisions.Select(x => $"    {x.Key}: {string.Join(", ", x.Select(y => y.Method))}")));

        List<LogEvent> outside = events.Where(x => !Ranges.Any(r => r.Owner == name && r.Contains(x.EventId))).ToList();

        outside.Should().BeEmpty(
            $"every id {name} raises has to be in a range allocated to {name}, which is what keeps it out of "
            + "another package's range when both are loaded into one process. These are not:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, outside.Select(x => $"    {x.EventId} {x.Method}"))
            + Environment.NewLine
            + $"Ranges for {name}: "
            + string.Join(", ", Ranges.Where(x => x.Owner == name).Select(x => $"{x.From}-{x.To} ({x.Area})")));

        await Verify(Format(events), extension: "txt")
            .UseDirectory("Verify")
            .UseFileName($"LogEventCatalogTest_{name}")
            .DisableRequireUniquePrefix();
    }

    private static List<LogEvent> Read(Assembly assembly)
    {
        List<LogEvent> events = [];

        foreach (Type type in assembly.GetTypes())
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                LoggerMessageAttribute? attribute = method.GetCustomAttribute<LoggerMessageAttribute>();
                if (attribute is null)
                {
                    continue;
                }

                events.Add(new LogEvent(attribute.EventId, $"{type.Name}.{method.Name}", attribute.Level, attribute.Message));
            }
        }

        events.Sort((x, y) => x.EventId != y.EventId
            ? x.EventId.CompareTo(y.EventId)
            : string.CompareOrdinal(x.Method, y.Method));

        return events;
    }

    /// <summary>
    /// The message is quoted because trailing whitespace is part of a template and would otherwise be
    /// invisible in the snapshot and in its diff.
    /// </summary>
    private static string Format(List<LogEvent> events)
    {
        return string.Join(Environment.NewLine, events.Select(x =>
            $"{x.EventId}  {x.Level}  {x.Method}{Environment.NewLine}    \"{x.Message}\""));
    }

    private sealed record LogEvent(int EventId, string Method, LogLevel Level, string Message);

    private sealed record EventIdRange(string Owner, int From, int To, string Area)
    {
        public bool Contains(int eventId) => eventId >= From && eventId <= To;
    }
}
