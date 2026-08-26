using System.Reflection;

using Microsoft.Extensions.Logging;

namespace Quartz.Tests.Unit;

/// <summary>
/// The catalogue of log events a package raises: every <c>[LoggerMessage]</c> method, its event id,
/// its level and its message template.
/// </summary>
/// <remarks>
/// <para>
/// An event id is what an operator filters and alerts on, and a message template is what a
/// structured-logging consumer matches. Both are contract once shipped, and neither shows up in the
/// public API baseline, because the generated classes are internal. The snapshot is what makes adding
/// an event, renumbering one, or rewording a message a reviewed diff instead of an invisible one.
/// </para>
/// <para>
/// Ids are allocated in ranges, one set per package, so two packages loaded into the same process
/// never disagree about what an id means. The ranges live in <see cref="Ranges" />; a package may only
/// use its own.
/// </para>
/// </remarks>
public class LogEventCatalogTest
{
    /// <summary>
    /// Who owns which event ids. Every id an assembly raises must fall inside a range recorded here
    /// against that assembly, which is also what keeps it out of another package's reserved range.
    /// </summary>
    private static readonly EventIdRange[] Ranges =
    [
        new("Quartz", 1000, 1999, "scheduler core"),
        new("Quartz", 2000, 2999, "in-memory store"),
        new("Quartz", 3000, 3499, "ADO.NET store"),
        new("Quartz", 3500, 3599, "clustering"),
        new("Quartz", 3600, 3699, "misfire handling"),
        new("Quartz", 3700, 3799, "lock handlers"),
        new("Quartz", 4000, 4999, "configuration, dependency injection and hosting"),
        new("Quartz", 5000, 5999, "serialization, type loading, triggers, calendars and utilities"),
        new("Quartz.Plugins", 6000, 6999, "plugins"),
        new("Quartz.Jobs", 7000, 7999, "jobs"),
        new("Quartz.Extensions.Redis", 8000, 8999, "Redis"),
    ];

    /// <summary>
    /// The assemblies whose catalogue is snapshotted. A package joins this list on the day its
    /// <c>Log*</c> calls become <c>[LoggerMessage]</c> methods — one line, and a snapshot of its own.
    /// </summary>
    private static readonly Assembly[] Catalogued =
    [
        typeof(global::Quartz.IScheduler).Assembly,
        typeof(global::Quartz.Plugins.Management.ShutdownHookPlugin).Assembly,
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
                LoggerMessageAttribute attribute = method.GetCustomAttribute<LoggerMessageAttribute>();
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
