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

using System.Xml;
using System.Xml.Linq;

namespace Quartz.Xml;

/// <summary>
/// A job scheduling XML document, as read.
/// </summary>
/// <remarks>
/// <para>
/// This is the shape of the document and nothing more: every value that the schema declares as a
/// number is kept as the text it was written as, so that the one place that turns text into a job or
/// a trigger — <see cref="XmlSchedulingDataProcessor" /> — stays the only place that decides what a
/// missing or malformed value means.
/// </para>
/// <para>
/// The document is validated against <c>job_scheduling_data_2_0.xsd</c> before it is read, so the
/// reader is deliberately forgiving: element order does not matter to it, and it never rejects a
/// document the schema would have accepted.
/// </para>
/// </remarks>
internal sealed class JobSchedulingData
{
    /// <summary>
    /// The XML namespace every element of a job scheduling document belongs to.
    /// </summary>
    private static readonly XNamespace Namespace = "http://quartznet.sourceforge.net/JobSchedulingData";

    public List<PreProcessingCommands> PreProcessingCommands { get; } = [];

    /// <summary>
    /// The directives, if the document gave any. A document may repeat the element; as with the
    /// generated model this replaced, only the first one is honoured.
    /// </summary>
    public ProcessingDirectives? Directives { get; private set; }

    public List<JobDefinition> Jobs { get; } = [];

    public List<TriggerDefinition> Triggers { get; } = [];

    /// <summary>
    /// Reads a job scheduling document.
    /// </summary>
    /// <exception cref="XmlException">The document is not well-formed XML.</exception>
    /// <exception cref="SchedulerConfigException">The document is not a job scheduling document.</exception>
    public static JobSchedulingData Read(string xml)
    {
        XDocument document = XDocument.Parse(xml);
        XElement? root = document.Root;

        if (root is null || root.Name != Namespace + "job-scheduling-data")
        {
            Throw.SchedulerConfigException(
                $"Expected a 'job-scheduling-data' root element in namespace '{Namespace}', found "
                + (root is null ? "an empty document" : $"'{root.Name}'"));
        }

        JobSchedulingData data = new JobSchedulingData();

        foreach (XElement commands in root!.Elements(Namespace + "pre-processing-commands"))
        {
            data.PreProcessingCommands.Add(ReadPreProcessingCommands(commands));
        }

        XElement? directives = root.Element(Namespace + "processing-directives");
        if (directives is not null)
        {
            data.Directives = ReadProcessingDirectives(directives);
        }

        foreach (XElement schedule in root.Elements(Namespace + "schedule"))
        {
            foreach (XElement job in schedule.Elements(Namespace + "job"))
            {
                data.Jobs.Add(ReadJob(job));
            }

            foreach (XElement trigger in schedule.Elements(Namespace + "trigger"))
            {
                data.Triggers.Add(ReadTrigger(trigger));
            }
        }

        return data;
    }

    private static PreProcessingCommands ReadPreProcessingCommands(XElement element)
    {
        PreProcessingCommands commands = new PreProcessingCommands();

        foreach (XElement group in element.Elements(Namespace + "delete-jobs-in-group"))
        {
            commands.DeleteJobsInGroup.Add(group.Value);
        }

        foreach (XElement group in element.Elements(Namespace + "delete-triggers-in-group"))
        {
            commands.DeleteTriggersInGroup.Add(group.Value);
        }

        foreach (XElement job in element.Elements(Namespace + "delete-job"))
        {
            commands.DeleteJobs.Add(ReadKeyReference(job));
        }

        foreach (XElement trigger in element.Elements(Namespace + "delete-trigger"))
        {
            commands.DeleteTriggers.Add(ReadKeyReference(trigger));
        }

        return commands;
    }

    private static KeyReference ReadKeyReference(XElement element)
    {
        return new KeyReference
        {
            Name = Text(element, "name"),
            Group = Text(element, "group"),
        };
    }

    private static ProcessingDirectives ReadProcessingDirectives(XElement element)
    {
        // Each directive keeps its default when the document leaves it out, which is why an empty
        // processing-directives element does not turn overwriting off. The one exception is the pair:
        // a document that says ignore-duplicates and nothing about overwrite-existing-data is asking for
        // duplicates to be passed over, and overwriting on top of that would be the opposite answer to
        // the same question.
        bool ignoreDuplicates = Flag(element, "ignore-duplicates", defaultValue: false);

        return new ProcessingDirectives
        {
            OverwriteExistingData = Flag(element, "overwrite-existing-data", defaultValue: !ignoreDuplicates),
            IgnoreDuplicates = ignoreDuplicates,
            ScheduleTriggerRelativeToReplacedTrigger =
                Flag(element, "schedule-trigger-relative-to-replaced-trigger", defaultValue: false),
        };
    }

    private static JobDefinition ReadJob(XElement element)
    {
        return new JobDefinition
        {
            Name = Text(element, "name"),
            Group = Text(element, "group"),
            Description = Text(element, "description"),
            JobType = Text(element, "job-type"),
            Durable = Flag(element, "durable", defaultValue: false),
            RequestsRecovery = Flag(element, "recover", defaultValue: false),
            JobDataMap = ReadJobDataMap(element),
        };
    }

    private static TriggerDefinition ReadTrigger(XElement element)
    {
        foreach (XElement child in element.Elements())
        {
            if (child.Name == Namespace + "simple")
            {
                return ReadCommon(new SimpleTriggerDefinition
                {
                    RepeatCount = Text(child, "repeat-count"),
                    RepeatInterval = Text(child, "repeat-interval"),
                }, child);
            }

            if (child.Name == Namespace + "cron")
            {
                return ReadCommon(new CronTriggerDefinition
                {
                    CronExpression = Text(child, "cron-expression"),
                    TimeZone = Text(child, "time-zone"),
                }, child);
            }

            if (child.Name == Namespace + "calendar-interval")
            {
                return ReadCommon(new CalendarIntervalTriggerDefinition
                {
                    RepeatInterval = Text(child, "repeat-interval"),
                    RepeatIntervalUnit = Text(child, "repeat-interval-unit"),
                }, child);
            }
        }

        Throw.SchedulerConfigException("Unknown trigger type in XML configuration");
        return null!;
    }

    private static T ReadCommon<T>(T trigger, XElement element) where T : TriggerDefinition
    {
        trigger.Name = Text(element, "name");
        trigger.Group = Text(element, "group");
        trigger.Description = Text(element, "description");
        trigger.JobName = Text(element, "job-name");
        trigger.JobGroup = Text(element, "job-group");
        trigger.Priority = Text(element, "priority");
        trigger.CalendarName = Text(element, "calendar-name");
        trigger.MisfireInstruction = Text(element, "misfire-instruction");
        trigger.JobDataMap = ReadJobDataMap(element);
        trigger.StartTime = Timestamp(element, "start-time");
        trigger.StartTimeSecondsInFuture = Text(element, "start-time-seconds-in-future");
        trigger.EndTime = Timestamp(element, "end-time");
        return trigger;
    }

    private static List<JobDataMapEntry> ReadJobDataMap(XElement element)
    {
        XElement? map = element.Element(Namespace + "job-data-map");
        if (map is null)
        {
            return [];
        }

        List<JobDataMapEntry> entries = [];
        foreach (XElement entry in map.Elements(Namespace + "entry"))
        {
            entries.Add(new JobDataMapEntry
            {
                Key = Text(entry, "key"),
                Value = Text(entry, "value"),
            });
        }

        return entries;
    }

    /// <summary>
    /// The element's text as written, untrimmed, or null when the element is absent. Telling those
    /// two apart is what makes an omitted element mean "use the default" rather than "empty".
    /// </summary>
    private static string? Text(XElement element, string name) => element.Element(Namespace + name)?.Value;

    private static bool Flag(XElement element, string name, bool defaultValue)
    {
        XElement? child = element.Element(Namespace + name);
        return child is null ? defaultValue : XmlConvert.ToBoolean(child.Value);
    }

    private static DateTime? Timestamp(XElement element, string name)
    {
        XElement? child = element.Element(Namespace + name);
        return child is null ? null : XmlConvert.ToDateTime(child.Value, XmlDateTimeSerializationMode.RoundtripKind);
    }
}

/// <summary>
/// Commands run against the scheduler before anything in the document is scheduled.
/// </summary>
internal sealed class PreProcessingCommands
{
    public List<string> DeleteJobsInGroup { get; } = [];

    public List<string> DeleteTriggersInGroup { get; } = [];

    public List<KeyReference> DeleteJobs { get; } = [];

    public List<KeyReference> DeleteTriggers { get; } = [];
}

/// <summary>
/// A job or trigger named by a delete command.
/// </summary>
internal sealed class KeyReference
{
    public string? Name { get; init; }

    public string? Group { get; init; }
}

/// <summary>
/// How the document is to be applied to the scheduler.
/// </summary>
internal sealed class ProcessingDirectives
{
    public bool OverwriteExistingData { get; init; }

    public bool IgnoreDuplicates { get; init; }

    public bool ScheduleTriggerRelativeToReplacedTrigger { get; init; }
}

/// <summary>
/// A job, as the document declares it.
/// </summary>
internal sealed class JobDefinition
{
    public string? Name { get; init; }

    public string? Group { get; init; }

    public string? Description { get; init; }

    public string? JobType { get; init; }

    public bool Durable { get; init; }

    public bool RequestsRecovery { get; init; }

    public List<JobDataMapEntry> JobDataMap { get; init; } = [];
}

/// <summary>
/// One <c>job-data-map</c> entry.
/// </summary>
internal sealed class JobDataMapEntry
{
    public string? Key { get; init; }

    public string? Value { get; init; }
}

/// <summary>
/// What every trigger declares, whatever its schedule.
/// </summary>
internal abstract class TriggerDefinition
{
    public string? Name { get; set; }

    public string? Group { get; set; }

    public string? Description { get; set; }

    public string? JobName { get; set; }

    public string? JobGroup { get; set; }

    public string? Priority { get; set; }

    public string? CalendarName { get; set; }

    public string? MisfireInstruction { get; set; }

    public List<JobDataMapEntry> JobDataMap { get; set; } = [];

    /// <summary>
    /// The <c>start-time</c> element, which the schema offers as an alternative to
    /// <see cref="StartTimeSecondsInFuture" />.
    /// </summary>
    public DateTime? StartTime { get; set; }

    /// <summary>
    /// The <c>start-time-seconds-in-future</c> element, which the schema offers as an alternative to
    /// <see cref="StartTime" />.
    /// </summary>
    public string? StartTimeSecondsInFuture { get; set; }

    /// <summary>
    /// The <c>end-time</c> element, or null when the document leaves the trigger unbounded.
    /// </summary>
    public DateTime? EndTime { get; set; }
}

/// <summary>
/// A <c>simple</c> trigger.
/// </summary>
internal sealed class SimpleTriggerDefinition : TriggerDefinition
{
    public string? RepeatCount { get; init; }

    public string? RepeatInterval { get; init; }
}

/// <summary>
/// A <c>cron</c> trigger.
/// </summary>
internal sealed class CronTriggerDefinition : TriggerDefinition
{
    public string? CronExpression { get; init; }

    public string? TimeZone { get; init; }
}

/// <summary>
/// A <c>calendar-interval</c> trigger.
/// </summary>
internal sealed class CalendarIntervalTriggerDefinition : TriggerDefinition
{
    public string? RepeatInterval { get; init; }

    public string? RepeatIntervalUnit { get; init; }
}
