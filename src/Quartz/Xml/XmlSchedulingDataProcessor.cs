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

    // pre-processing commands
    private readonly List<string> jobGroupsToDelete = new List<string>();
    private readonly List<string> triggerGroupsToDelete = new List<string>();
    private readonly List<JobKey> jobsToDelete = new List<JobKey>();
    private readonly List<TriggerKey> triggersToDelete = new List<TriggerKey>();

    // scheduling commands
    private readonly List<IJobDetail> loadedJobs = new List<IJobDetail>();
    private readonly List<ITrigger> loadedTriggers = new List<ITrigger>();

    // directives
    private readonly List<Exception> validationExceptions = new List<Exception>();

    private readonly List<string> jobGroupsToNeverDelete = new List<string>();
    private readonly List<string> triggerGroupsToNeverDelete = new List<string>();

    private readonly ILogger<XmlSchedulingDataProcessor> logger;
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// Constructor for XmlSchedulingDataProcessor.
    /// </summary>
    public XmlSchedulingDataProcessor(
        ILogger<XmlSchedulingDataProcessor> logger,
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

        logger.LogInformation("Parsing XML file: {FileName} with systemId: {SystemId}", fileName, systemId);

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
        logger.LogInformation("Parsing XML from stream with systemId: {SystemId}", systemId);
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
            logger.LogDebug("Found {JobGroupCount} delete job group commands.", jobGroupsToDelete.Count);
            logger.LogDebug("Found {TriggerGroupDeleteCount}delete trigger group commands.", triggerGroupsToDelete.Count);
            logger.LogDebug("Found {JobsToDeleteCount} delete job commands.", jobsToDelete.Count);
            logger.LogDebug("Found {TriggersToDelete} delete trigger commands.", triggersToDelete.Count);
        }

        //
        // Extract directives
        //
        if (data.Directives is not null)
        {
            logger.LogDebug("Directive 'overwrite-existing-data' specified as: {Overwrite}", data.Directives.OverwriteExistingData);
            OverwriteExistingData = data.Directives.OverwriteExistingData;

            logger.LogDebug("Directive 'ignore-duplicates' specified as: {IgnoreDuplicates}", data.Directives.IgnoreDuplicates);
            IgnoreDuplicates = data.Directives.IgnoreDuplicates;

            logger.LogDebug("Directive 'schedule-trigger-relative-to-replaced-trigger' specified as: {ScheduleRelative}",
                data.Directives.ScheduleTriggerRelativeToReplacedTrigger);
            ScheduleTriggerRelativeToReplacedTrigger = data.Directives.ScheduleTriggerRelativeToReplacedTrigger;
        }
        else
        {
            logger.LogDebug("Directive 'overwrite-existing-data' not specified, defaulting to {Overwrite}", OverwriteExistingData);
            logger.LogDebug("Directive 'ignore-duplicates' not specified, defaulting to {IgnoreDuplicates}", IgnoreDuplicates);
            logger.LogDebug("Directive 'schedule-trigger-relative-to-replaced-trigger' not specified, defaulting to {ScheduleTriggerRelativeToReplacedTrigger}",
                ScheduleTriggerRelativeToReplacedTrigger);
        }

        //
        // Extract Job definitions...
        //
        logger.LogDebug("Found {Count} job definitions.", data.Jobs.Count);

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
                logger.LogDebug("Parsed job definition: {JobDetail}", jobDetail);
            }

            AddJobToSchedule(jobDetail);
        }

        //
        // Extract Trigger definitions...
        //
        logger.LogDebug("Found {TriggerCount} trigger definitions.", data.Triggers.Count);

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

            IMutableTrigger trigger = (IMutableTrigger) TriggerBuilder.Create()
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
                logger.LogDebug("Parsed trigger definition: {Trigger}", trigger);
            }

            AddTriggerToSchedule(trigger);
        }
    }

    protected virtual void AddJobToSchedule(IJobDetail job)
    {
        loadedJobs.Add(job);
    }

    protected virtual void AddTriggerToSchedule(IMutableTrigger trigger)
    {
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
            logger.LogWarning(ex, "Unable to validate XML with schema: {Message}", ex.Message);
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
#pragma warning disable CA2254
            logger.LogWarning(e.Message);
#pragma warning restore CA2254
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

        logger.LogInformation("Adding {JobCount} jobs, {TriggerCount} triggers", jobs.Count, triggers.Count);

        Dictionary<JobKey, List<IMutableTrigger>> triggersByFQJobName = BuildTriggersByFullyQualifiedJobNameMap(triggers);

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
                    logger.LogInformation("Removing job: {JobKey}", detail.Key);
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
                    logger.LogInformation("Not overwriting existing job: {JobKey}", dupeJ.Key);
                    continue; // just ignore the entry
                }

                if (!OverwriteExistingData && !IgnoreDuplicates)
                {
                    Throw.ObjectAlreadyExistsException(detail);
                }
            }

            if (dupeJ is not null)
            {
                logger.LogInformation("Replacing job: {JobKey}", detail.Key);
            }
            else
            {
                logger.LogInformation("Adding job: {JobKey}", detail.Key);
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

                    ITrigger? dupeT = await scheduler.GetTrigger(trigger.Key, cancellationToken).ConfigureAwait(false);
                    if (dupeT is not null)
                    {
                        if (OverwriteExistingData)
                        {
                            if (logger.IsEnabled(LogLevel.Debug))
                            {
                                logger.LogDebug("Rescheduling job: {JobKey} with updated trigger: {TriggerKey}", trigger.JobKey, trigger.Key);
                            }
                        }
                        else if (IgnoreDuplicates)
                        {
                            logger.LogInformation("Not overwriting existing trigger: {Key}", dupeT.Key);
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
                            logger.LogDebug("Scheduling job: {JobKey} with trigger: {TriggerKey}", trigger.JobKey, trigger.Key);
                        }

                        try
                        {
                            if (addJobWithFirstSchedule)
                            {
                                await scheduler.ScheduleJob(detail, trigger, cancellationToken).ConfigureAwait(false); // add the job if it's not in yet...
                                addJobWithFirstSchedule = false;
                            }
                            else
                            {
                                await scheduler.ScheduleJob(trigger, cancellationToken).ConfigureAwait(false);
                            }
                        }
                        catch (ObjectAlreadyExistsException)
                        {
                            if (logger.IsEnabled(LogLevel.Debug))
                            {
                                logger.LogDebug("Adding trigger: {TriggerKey} for job: {JobKey} failed because the trigger already existed.  "
                                                + "This is likely due to a race condition between multiple instances "
                                                + "in the cluster.  Will try to reschedule instead.", trigger.Key, detail.Key);
                            }

                            // Let's try one more time as reschedule.
                            var oldTrigger = await scheduler.GetTrigger(trigger.Key, cancellationToken).ConfigureAwait(false);
                            await DoRescheduleJob(scheduler, trigger, oldTrigger, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }
        }

        // add triggers that weren't associated with a new job... (those we already handled were removed above)
        foreach (IMutableTrigger trigger in triggers)
        {
            ITrigger? dupeT = await scheduler.GetTrigger(trigger.Key, cancellationToken).ConfigureAwait(false);
            if (dupeT is not null)
            {
                if (OverwriteExistingData)
                {
                    if (logger.IsEnabled(LogLevel.Debug))
                    {
                        logger.LogDebug("Rescheduling job: {JobKey} with updated trigger: {TriggerKey}", trigger.JobKey, trigger.Key);
                    }
                }
                else if (IgnoreDuplicates)
                {
                    logger.LogInformation("Not overwriting existing trigger: {JobKey}", dupeT.Key);
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
                    logger.LogDebug("Scheduling job: {JobKey} with trigger: {TriggerKey}", trigger.JobKey, trigger.Key);
                }

                try
                {
                    await scheduler.ScheduleJob(trigger, cancellationToken).ConfigureAwait(false);
                }
                catch (ObjectAlreadyExistsException)
                {
                    if (logger.IsEnabled(LogLevel.Debug))
                    {
                        logger.LogDebug(
                            "Adding trigger: {TriggerKey} for job: {JobKey} failed because the trigger already existed. This is likely due to a race condition between multiple instances in the cluster. Will try to reschedule instead.",
                            trigger.Key,
                            trigger.JobKey);
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
        logger.LogWarning("Possibly duplicately named ({TriggerKey}) trigger in configuration, this can be caused by not having a fixed job key for targeted jobs",
            trigger.Key);
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
            logger.LogDebug("Using relative scheduling for trigger with key {TriggerKey}", trigger.Key);

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
                logger.LogInformation("Deleting all jobs in ALL groups.");
                // deliberately unbounded: deleting only the first page would leave survivors behind
                PagedResult<JobHeader> allJobs = await scheduler.QueryJobs(new JobQuery { Take = int.MaxValue }, cancellationToken).ConfigureAwait(false);
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
                    logger.LogInformation("Deleting all jobs in group: {Group}", group);
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
                logger.LogInformation("Deleting all triggers in ALL groups.");
                // deliberately unbounded: unscheduling only the first page would leave survivors behind
                PagedResult<TriggerHeader> allTriggers = await scheduler.QueryTriggers(new TriggerQuery { Take = int.MaxValue }, cancellationToken).ConfigureAwait(false);
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
                    logger.LogInformation("Deleting all triggers in group: {Group}", group);
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
                logger.LogInformation("Deleting job: {Key}", key);
                await scheduler.DeleteJob(key, cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (TriggerKey key in triggersToDelete)
        {
            if (!triggerGroupsToNeverDelete.Contains(key.Group))
            {
                logger.LogInformation("Deleting trigger: {Key}", key);
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