/*
 * Copyright 2001-2010 Terracotta, Inc.
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

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Xml;
using System.Xml.Schema;

using Microsoft.Extensions.Logging;

using Quartz.Extensibility;
using Quartz.Util;

namespace Quartz.Xml;

/// <summary>
/// Parses an XML file that declares Jobs and their schedules (Triggers).
/// </summary>
/// <remarks>
/// <para>
/// The xml document must conform to the format defined in "job_scheduling_data_2_0.xsd"
/// </para>
///
/// <para>
/// After creating an instance of this class, you should call one of the <see cref="ProcessFile(CancellationToken)" />
/// functions, after which you may call the ScheduledJobs()
/// function to get a handle to the defined Jobs and Triggers, which can then be
/// scheduled with the <see cref="IScheduler" />. Alternatively, you could call
/// the <see cref="ProcessFileAndScheduleJobs(Quartz.IScheduler, CancellationToken)" /> function to do all of this
/// in one step.
/// </para>
///
/// <para>
/// The same instance can be used again and again, with the list of defined Jobs
/// being cleared each time you call a <see cref="ProcessFile(CancellationToken)" /> method,
/// however a single instance is not thread-safe.
/// </para>
/// </remarks>
/// <author><a href="mailto:bonhamcm@thirdeyeconsulting.com">Chris Bonham</a></author>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
/// <author>Christian Krumm (.NET Bugfix)</author>
internal class XmlSchedulingDataProcessor
{
    public const string QuartzXmlFileName = "quartz_jobs.xml";
    public const string QuartzXsdResourceName = "Quartz.Xml.job_scheduling_data_2_0.xsd";

    private const string DirectivesDoNotApply =
        "The ignore-duplicates and overwrite-existing-data directives describe how the file relates to "
        + "the scheduler, so neither of them suppresses this.";

    // pre-processing commands
    private readonly List<string> jobGroupsToDelete = new List<string>();
    private readonly List<string> triggerGroupsToDelete = new List<string>();
    private readonly List<JobKey> jobsToDelete = new List<JobKey>();
    private readonly List<TriggerKey> triggersToDelete = new List<TriggerKey>();

    // scheduling commands
    private readonly List<IJobDetail> loadedJobs = new List<IJobDetail>();
    private readonly List<ITrigger> loadedTriggers = new List<ITrigger>();

    // the keys accepted so far from the document being processed, so that a key declared twice in
    // one document is caught before the second definition silently overwrites the first
    private readonly HashSet<JobKey> loadedJobKeys = new HashSet<JobKey>();
    private readonly HashSet<TriggerKey> loadedTriggerKeys = new HashSet<TriggerKey>();

    // directives
    private readonly List<Exception> validationExceptions = new List<Exception>();

    private readonly List<string> jobGroupsToNeverDelete = new List<string>();
    private readonly List<string> triggerGroupsToNeverDelete = new List<string>();

    private readonly ILogger logger;
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// Constructor for XmlSchedulingDataProcessor.
    /// </summary>
    public XmlSchedulingDataProcessor(
        ILogger<XmlSchedulingDataProcessor> logger,
        ITypeLoader typeLoader,
        TimeProvider timeProvider)
        : this((ILogger) logger, typeLoader, timeProvider)
    {
    }

    /// <summary>
    /// Constructor for a derived processor, which logs under its own category.
    /// </summary>
    /// <remarks>
    /// Every message this class writes carries the category of the logger it was given, so a subclass
    /// that hands up an <c>ILogger&lt;TSelf&gt;</c> makes the whole of its scheduling path filterable on
    /// its own name. The three paths are <c>Quartz.Xml.XmlSchedulingDataProcessor</c> for a scheduling
    /// file, <c>Quartz.Configuration.ContainerConfigurationProcessor</c> for what <c>AddQuartz</c>
    /// declared, and <c>Quartz.Plugins.Json.JsonSchedulingDataProcessor</c> for a JSON file — which used
    /// to be one category for all three, so "Adding 2 jobs, 2 triggers" said nothing about where they
    /// came from. The event ids are the same on every path.
    /// </remarks>
    /// <param name="logger">Where this processor writes, and the category it writes under.</param>
    /// <param name="typeLoader">Resolves the type names the declarations carry.</param>
    /// <param name="timeProvider">The clock the parsed schedules are measured against.</param>
    protected XmlSchedulingDataProcessor(
        ILogger logger,
        ITypeLoader typeLoader,
        TimeProvider timeProvider)
    {
        this.logger = logger;
        TypeLoader = typeLoader;
        this.timeProvider = timeProvider;

        OverwriteExistingData = true;
        IgnoreDuplicates = false;
    }

    /// <summary>
    /// Whether the existing scheduling data (with same identifiers) will be
    /// overwritten.
    /// </summary>
    /// <remarks>
    /// If false, and <see cref="IgnoreDuplicates" /> is not false, and jobs or
    /// triggers with the same names already exist as those in the file, an
    /// error will occur.
    /// </remarks>
    /// <seealso cref="IgnoreDuplicates" />
    public virtual bool OverwriteExistingData { get; set; }

    /// <summary>
    /// If true (and <see cref="OverwriteExistingData" /> is false) then any
    /// job/triggers encountered in this file that have names that already exist
    /// in the scheduler will be ignored, and no error will be produced.
    /// </summary>
    /// <seealso cref="OverwriteExistingData"/>
    public virtual bool IgnoreDuplicates { get; set; }

    /// <summary>
    /// If true (and <see cref="OverwriteExistingData" /> is true) then any
    /// job/triggers encountered in this file that already exist is scheduler
    /// will be updated with start time relative to old trigger. Effectively
    /// new trigger's last fire time will be updated to old trigger's last fire time
    /// and trigger's next fire time will updated to be next from this last fire time.
    /// </summary>
    public virtual bool ScheduleTriggerRelativeToReplacedTrigger { get; set; }

    protected virtual List<IJobDetail> LoadedJobs => new List<IJobDetail>(loadedJobs);

    protected virtual List<ITrigger> LoadedTriggers => new List<ITrigger>(loadedTriggers);

    protected ITypeLoader TypeLoader { get; }

    /// <summary>
    /// Process the xml file in the default location (a file named
    /// "quartz_jobs.xml" in the current working directory).
    /// </summary>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    [RequiresUnreferencedCode("Register every job type with AddJob<T>() or reference it from JobBuilder.Create<T>(); a type named only by a string in job_scheduling_data XML is not guaranteed to survive trimming.")]
    public virtual ValueTask ProcessFile(CancellationToken cancellationToken = default)
    {
        return ProcessFile(QuartzXmlFileName, cancellationToken);
    }

    /// <summary>
    /// Process the xml file named <see param="fileName" />.
    /// </summary>
    /// <param name="fileName">meta data file name.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    [RequiresUnreferencedCode("Register every job type with AddJob<T>() or reference it from JobBuilder.Create<T>(); a type named only by a string in job_scheduling_data XML is not guaranteed to survive trimming.")]
    public virtual ValueTask ProcessFile(
        string fileName,
        CancellationToken cancellationToken = default)
    {
        return ProcessFile(fileName, fileName, cancellationToken);
    }

    /// <summary>
    /// Process the xmlfile named <see param="fileName" /> with the given system
    /// ID.
    /// </summary>
    /// <param name="fileName">Name of the file.</param>
    /// <param name="systemId">The system id.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    [RequiresUnreferencedCode("Register every job type with AddJob<T>() or reference it from JobBuilder.Create<T>(); a type named only by a string in job_scheduling_data XML is not guaranteed to survive trimming.")]
    public virtual async ValueTask ProcessFile(
        string fileName,
        string systemId,
        CancellationToken cancellationToken = default)
    {
        // resolve file name first
        fileName = FileUtil.ResolveFile(fileName) ?? fileName;

        logger.ParsingFile(fileName, systemId);

        using (var stream = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (StreamReader sr = new StreamReader(stream))
        {
            ProcessInternal(await sr.ReadToEndAsync(cancellationToken).ConfigureAwait(false));
        }
    }

    /// <summary>
    /// Process the xmlfile named <see param="fileName" /> with the given system
    /// ID.
    /// </summary>
    /// <param name="stream">The stream.</param>
    /// <param name="systemId">The system id.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    [RequiresUnreferencedCode("Register every job type with AddJob<T>() or reference it from JobBuilder.Create<T>(); a type named only by a string in job_scheduling_data XML is not guaranteed to survive trimming.")]
    public virtual async ValueTask ProcessStream(
        Stream stream,
        string? systemId,
        CancellationToken cancellationToken = default)
    {
        logger.ParsingStream(systemId);
        using StreamReader sr = new StreamReader(stream);
        ProcessInternal(await sr.ReadToEndAsync(cancellationToken).ConfigureAwait(false));
    }

    protected virtual void PrepareForProcessing()
    {
        ClearValidationExceptions();

        OverwriteExistingData = true;
        IgnoreDuplicates = false;
        ScheduleTriggerRelativeToReplacedTrigger = false;

        jobGroupsToDelete.Clear();
        jobsToDelete.Clear();
        triggerGroupsToDelete.Clear();
        triggersToDelete.Clear();

        loadedJobs.Clear();
        loadedTriggers.Clear();
        loadedJobKeys.Clear();
        loadedTriggerKeys.Clear();
    }

    [RequiresUnreferencedCode("Register every job type with AddJob<T>() or reference it from JobBuilder.Create<T>(); a type named only by a string in job_scheduling_data XML is not guaranteed to survive trimming.")]
    protected virtual void ProcessInternal(string xml)
    {
        PrepareForProcessing();

        ValidateXml(xml);
        MaybeThrowValidationException();

        // read as object model
        JobSchedulingData data = JobSchedulingData.Read(xml);

        //
        // Extract pre-processing commands
        //
        foreach (PreProcessingCommands command in data.PreProcessingCommands)
        {
            foreach (string s in command.DeleteJobsInGroup)
            {
                var deleteJobGroup = s.NullSafeTrim();
                if (!string.IsNullOrEmpty(deleteJobGroup) && deleteJobGroup is not null)
                {
                    jobGroupsToDelete.Add(deleteJobGroup);
                }
            }

            foreach (string s in command.DeleteTriggersInGroup)
            {
                var deleteTriggerGroup = s.NullSafeTrim();
                if (!string.IsNullOrEmpty(deleteTriggerGroup) && deleteTriggerGroup is not null)
                {
                    triggerGroupsToDelete.Add(deleteTriggerGroup);
                }
            }

            foreach (KeyReference s in command.DeleteJobs)
            {
                var name = s.Name.TrimEmptyToNull();
                var group = s.Group.TrimEmptyToNull();

                if (name is null)
                {
                    Throw.SchedulerConfigException("Encountered a 'delete-job' command without a name specified.");
                }

                jobsToDelete.Add(new JobKey(name, group ?? Key<string>.DefaultGroup));
            }

            foreach (KeyReference s in command.DeleteTriggers)
            {
                var name = s.Name.TrimEmptyToNull();
                var group = s.Group.TrimEmptyToNull() ?? Key<string>.DefaultGroup;

                if (name is null)
                {
                    Throw.SchedulerConfigException("Encountered a 'delete-trigger' command without a name specified.");
                }

                triggersToDelete.Add(new TriggerKey(name, group));
            }
        }

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.FoundDeleteJobGroupCommands(jobGroupsToDelete.Count);
            logger.FoundDeleteTriggerGroupCommands(triggerGroupsToDelete.Count);
            logger.FoundDeleteJobCommands(jobsToDelete.Count);
            logger.FoundDeleteTriggerCommands(triggersToDelete.Count);
        }

        //
        // Extract directives
        //
        if (data.Directives is not null)
        {
            logger.OverwriteExistingDataSpecified(data.Directives.OverwriteExistingData);
            OverwriteExistingData = data.Directives.OverwriteExistingData;

            logger.IgnoreDuplicatesSpecified(data.Directives.IgnoreDuplicates);
            IgnoreDuplicates = data.Directives.IgnoreDuplicates;

            logger.ScheduleTriggerRelativeSpecified(data.Directives.ScheduleTriggerRelativeToReplacedTrigger);
            ScheduleTriggerRelativeToReplacedTrigger = data.Directives.ScheduleTriggerRelativeToReplacedTrigger;
        }
        else
        {
            logger.OverwriteExistingDataDefaulted(OverwriteExistingData);
            logger.IgnoreDuplicatesDefaulted(IgnoreDuplicates);
            logger.ScheduleTriggerRelativeDefaulted(ScheduleTriggerRelativeToReplacedTrigger);
        }

        //
        // Extract Job definitions...
        //
        logger.FoundJobDefinitions(data.Jobs.Count);

        foreach (JobDefinition jobDefinition in data.Jobs)
        {
            var jobName = jobDefinition.Name.TrimEmptyToNull();
            var jobGroup = jobDefinition.Group.TrimEmptyToNull() ?? Key<string>.DefaultGroup;
            var jobDescription = jobDefinition.Description.TrimEmptyToNull();
            var jobTypeName = jobDefinition.JobType.TrimEmptyToNull();

            Type jobType = TypeLoader.LoadType(jobTypeName!)!;

            IJobDetail jobDetail = JobBuilder.Create().OfType(jobType)
                .WithIdentity(jobName!, jobGroup)
                .WithDescription(jobDescription)
                .StoreDurably(jobDefinition.Durable)
                .RequestRecovery(jobDefinition.RequestsRecovery)
                .Build();

            foreach (JobDataMapEntry entry in jobDefinition.JobDataMap)
            {
                var key = entry.Key!.Trim();
                var value = entry.Value.TrimEmptyToNull();
                jobDetail.JobDataMap.Add(key, value!);
            }

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.ParsedJobDefinition(jobDetail);
            }

            AddJobToSchedule(jobDetail);
        }

        //
        // Extract Trigger definitions...
        //
        logger.FoundTriggerDefinitions(data.Triggers.Count);

        foreach (TriggerDefinition triggerNode in data.Triggers)
        {
            var triggerName = triggerNode.Name.TrimEmptyToNull()!;
            var triggerGroup = triggerNode.Group.TrimEmptyToNull() ?? Key<string>.DefaultGroup;
            var triggerDescription = triggerNode.Description.TrimEmptyToNull();
            var triggerCalendarRef = triggerNode.CalendarName.TrimEmptyToNull();
            string triggerJobName = triggerNode.JobName.TrimEmptyToNull()!;
            string triggerJobGroup = triggerNode.JobGroup.TrimEmptyToNull() ?? Key<string>.DefaultGroup;

            int triggerPriority = TriggerConstants.DefaultPriority;
            if (!string.IsNullOrWhiteSpace(triggerNode.Priority))
            {
                triggerPriority = Convert.ToInt32(triggerNode.Priority, CultureInfo.InvariantCulture);
            }

            DateTimeOffset triggerStartTime = timeProvider.GetUtcNow();
            if (triggerNode.StartTime is DateTime time)
            {
                triggerStartTime = new DateTimeOffset(time);
            }
            else if (triggerNode.StartTimeSecondsInFuture is not null)
            {
                triggerStartTime = triggerStartTime.AddSeconds(Convert.ToInt32(triggerNode.StartTimeSecondsInFuture, CultureInfo.InvariantCulture));
            }

            DateTimeOffset? triggerEndTime = triggerNode.EndTime is DateTime endTime ? new DateTimeOffset(endTime) : null;

            IScheduleBuilder scheduleBuilder;

            if (triggerNode is SimpleTriggerDefinition simpleTrigger)
            {
                var repeatCountString = simpleTrigger.RepeatCount.TrimEmptyToNull();
                var repeatIntervalString = simpleTrigger.RepeatInterval.TrimEmptyToNull();

                int repeatCount = ParseSimpleTriggerRepeatCount(repeatCountString!);
                TimeSpan repeatInterval = repeatIntervalString is null ? TimeSpan.Zero : TimeSpan.FromMilliseconds(Convert.ToInt64(repeatIntervalString, CultureInfo.InvariantCulture));

                scheduleBuilder = SimpleScheduleBuilder.Create()
                    .WithInterval(repeatInterval)
                    .WithRepeatCount(repeatCount);

                if (!string.IsNullOrWhiteSpace(simpleTrigger.MisfireInstruction))
                {
                    ((SimpleScheduleBuilder) scheduleBuilder).WithMisfireInstruction((SimpleTriggerMisfireInstruction) ReadMisfireInstructionFromString(TriggerFamily.Simple, simpleTrigger.MisfireInstruction));
                }
            }
            else if (triggerNode is CronTriggerDefinition cronTrigger)
            {
                var cronExpression = cronTrigger.CronExpression.TrimEmptyToNull();
                var timezoneString = cronTrigger.TimeZone.TrimEmptyToNull();

                TimeZoneInfo? tz = timezoneString is not null ? TimeZones.FindById(timezoneString) : null;
                scheduleBuilder = CronScheduleBuilder.Create(cronExpression!)
                    .InTimeZone(tz);

                if (!string.IsNullOrWhiteSpace(cronTrigger.MisfireInstruction))
                {
                    ((CronScheduleBuilder) scheduleBuilder).WithMisfireInstruction((CronTriggerMisfireInstruction) ReadMisfireInstructionFromString(TriggerFamily.Cron, cronTrigger.MisfireInstruction));
                }
            }
            else if (triggerNode is CalendarIntervalTriggerDefinition calendarIntervalTrigger)
            {
                var repeatIntervalString = calendarIntervalTrigger.RepeatInterval.TrimEmptyToNull();

                IntervalUnit intervalUnit = ParseDateIntervalTriggerIntervalUnit(calendarIntervalTrigger.RepeatIntervalUnit.TrimEmptyToNull());
                int repeatInterval = repeatIntervalString is null ? 0 : Convert.ToInt32(repeatIntervalString, CultureInfo.InvariantCulture);

                scheduleBuilder = CalendarIntervalScheduleBuilder.Create()
                    .WithInterval(repeatInterval, intervalUnit);

                if (!string.IsNullOrWhiteSpace(calendarIntervalTrigger.MisfireInstruction))
                {
                    ((CalendarIntervalScheduleBuilder) scheduleBuilder).WithMisfireInstruction((CalendarIntervalTriggerMisfireInstruction) ReadMisfireInstructionFromString(TriggerFamily.CalendarInterval, calendarIntervalTrigger.MisfireInstruction));
                }
            }
            else
            {
                Throw.SchedulerConfigException("Unknown trigger type in XML configuration");
                return;
            }

            IMutableTrigger trigger = (IMutableTrigger) TriggerBuilder.Create(timeProvider)
                .WithIdentity(triggerName, triggerGroup)
                .WithDescription(triggerDescription)
                .ForJob(triggerJobName, triggerJobGroup)
                .StartAt(triggerStartTime)
                .EndAt(triggerEndTime)
                .WithPriority(triggerPriority)
                .WithCalendarName(triggerCalendarRef)
                .WithSchedule(scheduleBuilder)
                .Build();

            foreach (JobDataMapEntry entry in triggerNode.JobDataMap)
            {
                string key = entry.Key.TrimEmptyToNull()!;
                var value = entry.Value.TrimEmptyToNull();
                trigger.JobDataMap.Add(key, value!);
            }

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.ParsedTriggerDefinition(trigger);
            }

            AddTriggerToSchedule(trigger);
        }
    }

    /// <summary>
    /// Accepts a parsed job definition into the set that will be scheduled.
    /// </summary>
    /// <remarks>
    /// Every loader funnels its jobs through here, so this is where a job key declared twice in one
    /// document is caught; an override that does not call <c>base</c> gives that check up.
    /// </remarks>
    /// <exception cref="SchedulingDataValidationException">
    /// The document being processed has already declared a job with this name and group.
    /// </exception>
    protected virtual void AddJobToSchedule(IJobDetail job)
    {
        if (!loadedJobKeys.Add(job.Key))
        {
            throw new SchedulingDataValidationException(
                $"Job '{job.Key}' is defined more than once in the scheduling data. {DirectivesDoNotApply}");
        }

        loadedJobs.Add(job);
    }

    /// <summary>
    /// Accepts a parsed trigger definition into the set that will be scheduled.
    /// </summary>
    /// <remarks>
    /// Every loader funnels its triggers through here, so this is where a trigger key declared twice
    /// in one document is caught; an override that does not call <c>base</c> gives that check up.
    /// </remarks>
    /// <exception cref="SchedulingDataValidationException">
    /// The document being processed has already declared a trigger with this name and group.
    /// </exception>
    protected virtual void AddTriggerToSchedule(IMutableTrigger trigger)
    {
        if (!loadedTriggerKeys.Add(trigger.Key))
        {
            throw new SchedulingDataValidationException(
                $"Trigger '{trigger.Key}' is defined more than once in the scheduling data. {DirectivesDoNotApply}");
        }

        loadedTriggers.Add(trigger);
    }

    protected virtual int ParseSimpleTriggerRepeatCount(string repeatCount)
    {
        int value = Convert.ToInt32(repeatCount, CultureInfo.InvariantCulture);
        return value;
    }

    /// <summary>
    /// Resolves a <c>misfire-instruction</c> element's text as the given trigger family spells it.
    /// </summary>
    /// <remarks>
    /// A name belonging to another family is accepted with a warning when its code is valid here,
    /// which is what the previous reflection-based lookup did silently. This used to be a
    /// <c>protected virtual</c> seam that took no family, and so could not tell the families apart.
    /// </remarks>
    private int ReadMisfireInstructionFromString(TriggerFamily family, string misfireInstruction)
    {
        return MisfireInstructionNames.Resolve(family, misfireInstruction, logger);
    }

    protected virtual IntervalUnit ParseDateIntervalTriggerIntervalUnit(string? intervalUnit)
    {
        if (string.IsNullOrEmpty(intervalUnit) || intervalUnit is null)
        {
            return IntervalUnit.Day;
        }

        if (!TryParseEnum(intervalUnit, out IntervalUnit retValue))
        {
            Throw.SchedulerConfigException("Unknown interval unit for DateIntervalTrigger: " + intervalUnit);
        }

        return retValue;
    }

    /// <remarks>
    /// The generic overloads of <see cref="Enum.GetNames{TEnum}" /> and <see cref="Enum.GetValues{TEnum}" />
    /// rather than the ones taking a <see cref="Type" />: building an array of an enum type named at runtime
    /// is code the AOT compiler would have to generate, and the constraint means it never has to.
    /// </remarks>
    protected virtual bool TryParseEnum<T>(string value, out T result) where T : struct, Enum
    {
        string[] names = Enum.GetNames<T>();
        result = Enum.GetValues<T>()[0];
        foreach (string name in names)
        {
            if (name == value)
            {
                result = Enum.Parse<T>(name);
                return true;
            }
        }

        return false;
    }

    private void ValidateXml(string xml)
    {
        try
        {
            var settings = new XmlReaderSettings
            {
                ValidationType = ValidationType.Schema,
                ValidationFlags = XmlSchemaValidationFlags.ProcessInlineSchema
                                  | XmlSchemaValidationFlags.ProcessSchemaLocation
                                  | XmlSchemaValidationFlags.ReportValidationWarnings
            };

            using var stream = typeof(XmlSchedulingDataProcessor).Assembly.GetManifestResourceStream(QuartzXsdResourceName);

            if (stream is null)
            {
                Throw.ArgumentException("Could not read XSD from embedded resource");
            }

            var schema = XmlSchema.Read(XmlReader.Create(stream), XmlValidationCallBack);
            settings.Schemas.Add(schema!);
            settings.ValidationEventHandler += XmlValidationCallBack;

            // stream to validate
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            while (reader.Read())
            {
            }
        }
        catch (Exception ex)
        {
            logger.SchemaValidationUnavailable(ex.Message, ex);
        }
    }

    private void XmlValidationCallBack(object? sender, ValidationEventArgs e)
    {
        if (e.Severity == XmlSeverityType.Error)
        {
            validationExceptions.Add(e.Exception);
        }
        else
        {
            logger.SchemaValidationWarning(e.Message);
        }
    }

    /// <summary>
    /// Process the xml file in the default location, and schedule all of the jobs defined within it.
    /// </summary>
    /// <remarks>Note that we will set overwriteExistingJobs after the default xml is parsed.</remarks>
    [RequiresUnreferencedCode("Register every job type with AddJob<T>() or reference it from JobBuilder.Create<T>(); a type named only by a string in job_scheduling_data XML is not guaranteed to survive trimming.")]
    public async ValueTask ProcessFileAndScheduleJobs(
        IScheduler scheduler,
        bool overwriteExistingJobs,
        CancellationToken cancellationToken = default)
    {
        await ProcessFile(QuartzXmlFileName, QuartzXmlFileName, cancellationToken).ConfigureAwait(false);
        // The overwriteExistingJobs flag was set by ProcessFile() -> PrepareForProcessing(), then by xml parsing, and then now
        // we need to reset it again here by this method parameter to override it.
        OverwriteExistingData = overwriteExistingJobs;
        await ExecutePreProcessCommands(scheduler, cancellationToken).ConfigureAwait(false);
        await ScheduleJobs(scheduler, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Process the xml file in the default location, and schedule all of the
    /// jobs defined within it.
    /// </summary>
    [RequiresUnreferencedCode("Register every job type with AddJob<T>() or reference it from JobBuilder.Create<T>(); a type named only by a string in job_scheduling_data XML is not guaranteed to survive trimming.")]
    public virtual ValueTask ProcessFileAndScheduleJobs(
        IScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        return ProcessFileAndScheduleJobs(QuartzXmlFileName, scheduler, cancellationToken);
    }

    /// <summary>
    /// Process the xml file in the given location, and schedule all of the
    /// jobs defined within it.
    /// </summary>
    /// <param name="fileName">meta data file name.</param>
    /// <param name="scheduler">The scheduler.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    [RequiresUnreferencedCode("Register every job type with AddJob<T>() or reference it from JobBuilder.Create<T>(); a type named only by a string in job_scheduling_data XML is not guaranteed to survive trimming.")]
    public virtual ValueTask ProcessFileAndScheduleJobs(
        string fileName,
        IScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        return ProcessFileAndScheduleJobs(fileName, fileName, scheduler, cancellationToken);
    }

    /// <summary>
    /// Process the xml file in the given location, and schedule all of the
    /// jobs defined within it.
    /// </summary>
    /// <param name="fileName">Name of the file.</param>
    /// <param name="systemId">The system id.</param>
    /// <param name="scheduler">The scheduler.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    [RequiresUnreferencedCode("Register every job type with AddJob<T>() or reference it from JobBuilder.Create<T>(); a type named only by a string in job_scheduling_data XML is not guaranteed to survive trimming.")]
    public virtual async ValueTask ProcessFileAndScheduleJobs(
        string fileName,
        string systemId,
        IScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        await ProcessFile(fileName, systemId, cancellationToken).ConfigureAwait(false);
        await ExecutePreProcessCommands(scheduler, cancellationToken).ConfigureAwait(false);
        await ScheduleJobs(scheduler, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Process the xml file in the given location, and schedule all of the
    /// jobs defined within it.
    /// </summary>
    /// <param name="stream">stream to read XML data from.</param>
    /// <param name="scheduler">The scheduler.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    [RequiresUnreferencedCode("Register every job type with AddJob<T>() or reference it from JobBuilder.Create<T>(); a type named only by a string in job_scheduling_data XML is not guaranteed to survive trimming.")]
    public virtual async ValueTask ProcessStreamAndScheduleJobs(
        Stream stream,
        IScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        using (var sr = new StreamReader(stream))
        {
            ProcessInternal(await sr.ReadToEndAsync(cancellationToken).ConfigureAwait(false));
        }

        await ExecutePreProcessCommands(scheduler, cancellationToken).ConfigureAwait(false);
        await ScheduleJobs(scheduler, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Schedules the given sets of jobs and triggers.
    /// </summary>
    /// <param name="scheduler">The scheduler.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public virtual async ValueTask ScheduleJobs(
        IScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        List<IJobDetail> jobs = new List<IJobDetail>(LoadedJobs);
        List<ITrigger> triggers = new List<ITrigger>(LoadedTriggers);

        logger.AddingJobsAndTriggers(jobs.Count, triggers.Count);

        Dictionary<JobKey, List<IMutableTrigger>> triggersByFQJobName = BuildTriggersByFullyQualifiedJobNameMap(triggers);

        // The keys of the triggers the per-job loop below deals with. Those lists are built from
        // `triggers` but are not backed by it, so taking a trigger out of its job's list leaves it in
        // `triggers` for the trailing loop to meet a second time — which is how a trigger loaded beside
        // its own job used to be scheduled and then immediately rescheduled, the reschedule restoring
        // the fire times the first scheduling had already computed (#3554).
        HashSet<TriggerKey> handledTriggerKeys = [];

        // add each job, and it's associated triggers
        while (jobs.Count > 0)
        {
            // remove jobs as we handle them...
            IJobDetail detail = jobs[0];
            jobs.Remove(detail);

            IJobDetail? dupeJ = null;
            try
            {
                // The existing job could have been deleted, and Quartz API doesn't allow us to query this without
                // loading the job class, so use try/catch to handle it.
                dupeJ = await scheduler.GetJobDetail(detail.Key, cancellationToken).ConfigureAwait(false);
            }
            catch (JobPersistenceException e)
            {
                if (e.InnerException is TypeLoadException && OverwriteExistingData)
                {
                    // We are going to replace jobDetail anyway, so just delete it first.
                    logger.RemovingJob(detail.Key);
                    await scheduler.DeleteJob(detail.Key, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    throw;
                }
            }

            if (dupeJ is not null)
            {
                if (!OverwriteExistingData && IgnoreDuplicates)
                {
                    logger.NotOverwritingExistingJob(dupeJ.Key);
                    continue; // just ignore the entry
                }

                if (!OverwriteExistingData && !IgnoreDuplicates)
                {
                    Throw.ObjectAlreadyExistsException(detail);
                }
            }

            if (dupeJ is not null)
            {
                logger.ReplacingJob(detail.Key);
            }
            else
            {
                logger.AddingJob(detail.Key);
            }

            triggersByFQJobName.TryGetValue(detail.Key, out var triggersOfJob);

            if (!detail.Durable && (triggersOfJob is null || triggersOfJob.Count == 0))
            {
                if (dupeJ is null)
                {
                    Throw.SchedulerException(
                        "A new job defined without any triggers must be durable: " +
                        detail.Key);
                }

                if (dupeJ.Durable && await JobHasNoTriggers(scheduler, detail.Key, cancellationToken).ConfigureAwait(false))
                {
                    Throw.SchedulerException(
                        "Can't change existing durable job without triggers to non-durable: " +
                        detail.Key);
                }
            }

            if (dupeJ is not null || detail.Durable)
            {
                if (triggersOfJob is not null && triggersOfJob.Count > 0)
                {
                    // add the job regardless is durable or not b/c we have trigger to add
                    await scheduler.AddJob(detail, new AddJobOptions { Replace = true, StoreNonDurableWhileAwaitingScheduling = true }, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    // add the job only if a replacement or durable, else exception will throw!
                    await scheduler.AddJob(detail, new AddJobOptions { Replace = true }, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                bool addJobWithFirstSchedule = true;

                // Add triggers related to the job...
                while (triggersOfJob!.Count > 0)
                {
                    IMutableTrigger trigger = triggersOfJob[0];
                    // remove triggers as we handle them...
                    triggersOfJob.Remove(trigger);
                    handledTriggerKeys.Add(trigger.Key);

                    ITrigger? dupeT = await scheduler.GetTrigger(trigger.Key, cancellationToken).ConfigureAwait(false);
                    if (dupeT is not null)
                    {
                        if (OverwriteExistingData)
                        {
                            if (logger.IsEnabled(LogLevel.Debug))
                            {
                                logger.ReschedulingJob(trigger.JobKey, trigger.Key);
                            }
                        }
                        else if (IgnoreDuplicates)
                        {
                            logger.NotOverwritingExistingTriggerByKey(dupeT.Key);
                            continue; // just ignore the trigger (and possibly job)
                        }
                        else
                        {
                            Throw.ObjectAlreadyExistsException(trigger);
                        }

                        if (!dupeT.JobKey.Equals(trigger.JobKey))
                        {
                            ReportDuplicateTrigger(trigger);
                        }

                        await DoRescheduleJob(scheduler, trigger, dupeT, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        if (logger.IsEnabled(LogLevel.Debug))
                        {
                            logger.SchedulingJob(trigger.JobKey, trigger.Key);
                        }

                        try
                        {
                            if (addJobWithFirstSchedule)
                            {
                                await scheduler.ScheduleJob(detail, trigger, cancellationToken: cancellationToken).ConfigureAwait(false); // add the job if it's not in yet...
                                addJobWithFirstSchedule = false;
                            }
                            else
                            {
                                await scheduler.ScheduleJob(trigger, cancellationToken: cancellationToken).ConfigureAwait(false);
                            }
                        }
                        catch (ObjectAlreadyExistsException)
                        {
                            if (logger.IsEnabled(LogLevel.Debug))
                            {
                                logger.TriggerAlreadyExistedWillReschedule(trigger.Key, detail.Key);
                            }

                            // Let's try one more time as reschedule.
                            var oldTrigger = await scheduler.GetTrigger(trigger.Key, cancellationToken).ConfigureAwait(false);
                            await DoRescheduleJob(scheduler, trigger, oldTrigger, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }
        }

        // add triggers that weren't associated with a new job... (those we already handled are skipped)
        foreach (IMutableTrigger trigger in triggers)
        {
            if (handledTriggerKeys.Contains(trigger.Key))
            {
                continue;
            }

            ITrigger? dupeT = await scheduler.GetTrigger(trigger.Key, cancellationToken).ConfigureAwait(false);
            if (dupeT is not null)
            {
                if (OverwriteExistingData)
                {
                    if (logger.IsEnabled(LogLevel.Debug))
                    {
                        logger.ReschedulingJob(trigger.JobKey, trigger.Key);
                    }
                }
                else if (IgnoreDuplicates)
                {
                    logger.NotOverwritingExistingTrigger(dupeT.Key);
                    continue; // just ignore the trigger
                }
                else
                {
                    Throw.ObjectAlreadyExistsException(trigger);
                }

                if (!dupeT.JobKey.Equals(trigger.JobKey))
                {
                    ReportDuplicateTrigger(trigger);
                }

                await DoRescheduleJob(scheduler, trigger, dupeT, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.SchedulingJob(trigger.JobKey, trigger.Key);
                }

                try
                {
                    await scheduler.ScheduleJob(trigger, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                catch (ObjectAlreadyExistsException)
                {
                    if (logger.IsEnabled(LogLevel.Debug))
                    {
                        logger.TriggerAlreadyExistedWillReschedule(trigger.Key, trigger.JobKey);
                    }

                    // Let's rescheduleJob one more time.
                    var oldTrigger = await scheduler.GetTrigger(trigger.Key, cancellationToken).ConfigureAwait(false);
                    await DoRescheduleJob(scheduler, trigger, oldTrigger, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private void ReportDuplicateTrigger(IMutableTrigger trigger)
    {
        logger.DuplicatelyNamedTrigger(trigger.Key);
    }

    private ValueTask<DateTimeOffset?> DoRescheduleJob(
        IScheduler scheduler,
        IMutableTrigger trigger,
        ITrigger? oldTrigger,
        CancellationToken cancellationToken = default)
    {
        // if this is a trigger with default start time we can consider relative scheduling
        if (oldTrigger is not null && trigger.StartTimeUtc - timeProvider.GetUtcNow() < TimeSpan.FromSeconds(5) && ScheduleTriggerRelativeToReplacedTrigger)
        {
            logger.UsingRelativeScheduling(trigger.Key);

            var oldTriggerPreviousFireTime = oldTrigger.PreviousFireTimeUtc;
            trigger.StartTimeUtc = oldTrigger.StartTimeUtc;
            ((IOperableTrigger) trigger).PreviousFireTimeUtc = oldTriggerPreviousFireTime;
            // if oldTriggerPreviousFireTime is null then NextFireTime should be set relative to oldTrigger.StartTimeUtc
            // to be able to handle misfiring for an existing trigger that has never been executed before.
            ((IOperableTrigger) trigger).NextFireTimeUtc = trigger.GetFireTimeAfter(oldTriggerPreviousFireTime ?? oldTrigger.StartTimeUtc);
        }

        return scheduler.RescheduleJob(trigger.Key, trigger, cancellationToken);
    }

    protected virtual Dictionary<JobKey, List<IMutableTrigger>> BuildTriggersByFullyQualifiedJobNameMap(IReadOnlyCollection<ITrigger> triggers)
    {
        Dictionary<JobKey, List<IMutableTrigger>> triggersByFQJobName = new Dictionary<JobKey, List<IMutableTrigger>>();

        foreach (IMutableTrigger trigger in triggers)
        {
            if (!triggersByFQJobName.TryGetValue(trigger.JobKey, out var triggersOfJob))
            {
                triggersOfJob = new List<IMutableTrigger>();
                triggersByFQJobName[trigger.JobKey] = triggersOfJob;
            }

            triggersOfJob.Add(trigger);
        }

        return triggersByFQJobName;
    }

    private static async ValueTask<bool> JobHasNoTriggers(
        IScheduler scheduler,
        JobKey jobKey,
        CancellationToken cancellationToken)
    {
        PagedResult<TriggerHeader> triggers = await scheduler
            .QueryTriggers(new TriggerQuery { Job = jobKey, Take = 0, IncludeTotalCount = true }, cancellationToken)
            .ConfigureAwait(false);
        return triggers.TotalCount == 0;
    }

    protected async ValueTask ExecutePreProcessCommands(
        IScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        foreach (string group in jobGroupsToDelete)
        {
            if (group == "*")
            {
                logger.DeletingAllJobsInAllGroups();
                // deliberately unbounded: deleting only the first page would leave survivors behind
                PagedResult<JobHeader> allJobs = await scheduler.QueryJobs(new JobQuery { Take = PagedQuery.All }, cancellationToken).ConfigureAwait(false);
                foreach (JobHeader job in allJobs.Items)
                {
                    if (!jobGroupsToNeverDelete.Contains(job.Key.Group))
                    {
                        await scheduler.DeleteJob(job.Key, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            else
            {
                if (!jobGroupsToNeverDelete.Contains(group))
                {
                    logger.DeletingAllJobsInGroup(group);
                    foreach (JobKey key in await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(group), cancellationToken).ConfigureAwait(false))
                    {
                        await scheduler.DeleteJob(key, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }

        foreach (string group in triggerGroupsToDelete)
        {
            if (group == "*")
            {
                logger.DeletingAllTriggersInAllGroups();
                // deliberately unbounded: unscheduling only the first page would leave survivors behind
                PagedResult<TriggerHeader> allTriggers = await scheduler.QueryTriggers(new TriggerQuery { Take = PagedQuery.All }, cancellationToken).ConfigureAwait(false);
                foreach (TriggerHeader trigger in allTriggers.Items)
                {
                    if (!triggerGroupsToNeverDelete.Contains(trigger.Key.Group))
                    {
                        await scheduler.UnscheduleJob(trigger.Key, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            else
            {
                if (!triggerGroupsToNeverDelete.Contains(group))
                {
                    logger.DeletingAllTriggersInGroup(group);
                    foreach (TriggerKey key in await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals(group), cancellationToken).ConfigureAwait(false))
                    {
                        await scheduler.UnscheduleJob(key, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }

        foreach (JobKey key in jobsToDelete)
        {
            if (!jobGroupsToNeverDelete.Contains(key.Group))
            {
                logger.DeletingJob(key);
                await scheduler.DeleteJob(key, cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (TriggerKey key in triggersToDelete)
        {
            if (!triggerGroupsToNeverDelete.Contains(key.Group))
            {
                logger.DeletingTrigger(key);
                await scheduler.UnscheduleJob(key, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Adds a detected validation exception.
    /// </summary>
    /// <param name="e">The exception.</param>
    protected virtual void AddValidationException(XmlException e)
    {
        validationExceptions.Add(e);
    }

    /// <summary>
    /// Resets the number of detected validation exceptions.
    /// </summary>
    protected virtual void ClearValidationExceptions()
    {
        validationExceptions.Clear();
    }

    /// <summary>
    /// Throws a SchedulingDataValidationException if the number of validationExceptions
    /// detected is greater than zero.
    /// </summary>
    /// <exception cref="SchedulingDataValidationException">
    /// DTD validation exception.
    /// </exception>
    protected virtual void MaybeThrowValidationException()
    {
        if (validationExceptions.Count > 0)
        {
            throw new SchedulingDataValidationException(validationExceptions);
        }
    }

    public void AddJobGroupToNeverDelete(string jobGroupName)
    {
        jobGroupsToNeverDelete.Add(jobGroupName);
    }

    public void AddTriggerGroupToNeverDelete(string triggerGroupName)
    {
        triggerGroupsToNeverDelete.Add(triggerGroupName);
    }
}