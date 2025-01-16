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

using System.Globalization;
using System.Reflection;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

using Microsoft.Extensions.Logging;

using Quartz.Impl.Matchers;
using Quartz.Spi;
using Quartz.Util;
using Quartz.Xml.JobSchedulingData20;

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
public class XMLSchedulingDataProcessor
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

    private readonly ILogger<XMLSchedulingDataProcessor> logger;
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// Constructor for XMLSchedulingDataProcessor.
    /// </summary>
    public XMLSchedulingDataProcessor(
        ILogger<XMLSchedulingDataProcessor> logger,
        ITypeLoadHelper typeLoadHelper,
        TimeProvider timeProvider)
    {
        this.logger = logger;
        TypeLoadHelper = typeLoadHelper;
        this.timeProvider = timeProvider;

        OverWriteExistingData = true;
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
    public virtual bool OverWriteExistingData { get; set; }

    /// <summary>
    /// If true (and <see cref="OverWriteExistingData" /> is false) then any
    /// job/triggers encountered in this file that have names that already exist
    /// in the scheduler will be ignored, and no error will be produced.
    /// </summary>
    /// <seealso cref="OverWriteExistingData"/>
    public virtual bool IgnoreDuplicates { get; set; }

    /// <summary>
    /// If true (and <see cref="OverWriteExistingData" /> is true) then any
    /// job/triggers encountered in this file that already exist is scheduler
    /// will be updated with start time relative to old trigger. Effectively
    /// new trigger's last fire time will be updated to old trigger's last fire time
    /// and trigger's next fire time will updated to be next from this last fire time.
    /// </summary>
    public virtual bool ScheduleTriggerRelativeToReplacedTrigger { get; set; }

    protected virtual IReadOnlyList<IJobDetail> LoadedJobs => loadedJobs.AsReadOnly();

    protected virtual IReadOnlyList<ITrigger> LoadedTriggers => loadedTriggers.AsReadOnly();

    protected ITypeLoadHelper TypeLoadHelper { get; }

    /// <summary>
    /// Process the xml file in the default location (a file named
    /// "quartz_jobs.xml" in the current working directory).
    /// </summary>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public virtual ValueTask ProcessFile(CancellationToken cancellationToken = default)
    {
        return ProcessFile(QuartzXmlFileName, cancellationToken);
    }

    /// <summary>
    /// Process the xml file named <see param="fileName" />.
    /// </summary>
    /// <param name="fileName">meta data file name.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
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
    public virtual async ValueTask ProcessStream(
        Stream stream,
        string? systemId,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Parsing XML from stream with systemId: {SystemId}", systemId);
        using StreamReader sr = new StreamReader(stream);
        ProcessInternal(await sr.ReadToEndAsync(cancellationToken).ConfigureAwait(false));
    }

    protected virtual void PrepForProcessing()
    {
        ClearValidationExceptions();

        OverWriteExistingData = true;
        IgnoreDuplicates = false;

        jobGroupsToDelete.Clear();
        jobsToDelete.Clear();
        triggerGroupsToDelete.Clear();
        triggersToDelete.Clear();

        loadedJobs.Clear();
        loadedTriggers.Clear();
    }

    protected virtual void ProcessInternal(string xml)
    {
        PrepForProcessing();

        ValidateXml(xml);
        MaybeThrowValidationException();

        // deserialize as object model
        var xs = new XmlSerializer(typeof(QuartzXmlConfiguration20));
        var data = (QuartzXmlConfiguration20?) xs.Deserialize(XmlReader.Create(new StringReader(xml)));

        if (data is null)
        {
            ThrowHelper.ThrowSchedulerConfigException("Job definition data from XML was null after deserialization");
        }

        //
        // Extract pre-processing commands
        //
        if (data.preprocessingcommands is not null)
        {
            foreach (preprocessingcommandsType command in data.preprocessingcommands)
            {
                if (command.deletejobsingroup is not null)
                {
                    foreach (string s in command.deletejobsingroup)
                    {
                        var deleteJobGroup = s.NullSafeTrim();
                        if (!string.IsNullOrEmpty(deleteJobGroup) && deleteJobGroup is not null)
                        {
                            jobGroupsToDelete.Add(deleteJobGroup);
                        }
                    }
                }

                if (command.deletetriggersingroup is not null)
                {
                    foreach (string s in command.deletetriggersingroup)
                    {
                        var deleteTriggerGroup = s.NullSafeTrim();
                        if (!string.IsNullOrEmpty(deleteTriggerGroup) && deleteTriggerGroup is not null)
                        {
                            triggerGroupsToDelete.Add(deleteTriggerGroup);
                        }
                    }
                }

                if (command.deletejob is not null)
                {
                    foreach (preprocessingcommandsTypeDeletejob s in command.deletejob)
                    {
                        var name = s.name.TrimEmptyToNull();
                        var group = s.group.TrimEmptyToNull();

                        if (name is null)
                        {
                            ThrowHelper.ThrowSchedulerConfigException("Encountered a 'delete-job' command without a name specified.");
                        }

                        jobsToDelete.Add(new JobKey(name, group ?? Key<string>.DefaultGroup));
                    }
                }

                if (command.deletetrigger is not null)
                {
                    foreach (preprocessingcommandsTypeDeletetrigger s in command.deletetrigger)
                    {
                        var name = s.name.TrimEmptyToNull();
                        var group = s.group.TrimEmptyToNull() ?? Key<string>.DefaultGroup;

                        if (name is null)
                        {
                            ThrowHelper.ThrowSchedulerConfigException("Encountered a 'delete-trigger' command without a name specified.");
                        }

                        triggersToDelete.Add(new TriggerKey(name, group));
                    }
                }
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
        if (data.processingdirectives is not null && data.processingdirectives.Length > 0)
        {
            bool overWrite = data.processingdirectives[0].overwriteexistingdata;
            logger.LogDebug("Directive 'overwrite-existing-data' specified as: {Overwrite}", overWrite);
            OverWriteExistingData = overWrite;
        }
        else
        {
            logger.LogDebug("Directive 'overwrite-existing-data' not specified, defaulting to {Overwrite}", OverWriteExistingData);
        }

        if (data.processingdirectives is not null && data.processingdirectives.Length > 0)
        {
            bool ignoreduplicates = data.processingdirectives[0].ignoreduplicates;
            logger.LogDebug("Directive 'ignore-duplicates' specified as: {IgnoreDuplicates}", ignoreduplicates);
            IgnoreDuplicates = ignoreduplicates;
        }
        else
        {
            logger.LogDebug("Directive 'ignore-duplicates' not specified, defaulting to {IgnoreDuplicates}", IgnoreDuplicates);
        }

        if (data.processingdirectives is not null && data.processingdirectives.Length > 0)
        {
            bool scheduleRelative = data.processingdirectives[0].scheduletriggerrelativetoreplacedtrigger;
            logger.LogDebug("Directive 'schedule-trigger-relative-to-replaced-trigger' specified as: {ScheduleRelative}", scheduleRelative);
            ScheduleTriggerRelativeToReplacedTrigger = scheduleRelative;
        }
        else
        {
            logger.LogDebug("Directive 'schedule-trigger-relative-to-replaced-trigger' not specified, defaulting to {ScheduleTriggerRelativeToReplacedTrigger}",
                ScheduleTriggerRelativeToReplacedTrigger);
        }

        //
        // Extract Job definitions...
        //
        List<jobdetailType> jobNodes = new List<jobdetailType>();
        if (data.schedule is not null)
        {
            foreach (var schedule in data.schedule)
            {
                if (schedule?.job is not null)
                {
                    jobNodes.AddRange(schedule.job);
                }
            }
        }

        logger.LogDebug("Found {Count} job definitions.", jobNodes.Count);

        foreach (jobdetailType jobDetailType in jobNodes)
        {
            var jobName = jobDetailType.name.TrimEmptyToNull();
            var jobGroup = jobDetailType.group.TrimEmptyToNull() ?? Key<string>.DefaultGroup;
            var jobDescription = jobDetailType.description.TrimEmptyToNull();
            var jobTypeName = jobDetailType.jobtype.TrimEmptyToNull();
            bool jobDurability = jobDetailType.durable;
            bool jobRecoveryRequested = jobDetailType.recover;

            Type jobType = TypeLoadHelper.LoadType(jobTypeName!)!;

            IJobDetail jobDetail = JobBuilder.Create(jobType!)
                .WithIdentity(jobName!, jobGroup)
                .WithDescription(jobDescription)
                .StoreDurably(jobDurability)
                .RequestRecovery(jobRecoveryRequested)
                .Build();

            if (jobDetailType.jobdatamap is not null && jobDetailType.jobdatamap.entry is not null)
            {
                foreach (entryType entry in jobDetailType.jobdatamap.entry)
                {
                    var key = entry.key.Trim();
                    var value = entry.value.TrimEmptyToNull();
                    jobDetail.JobDataMap.Add(key, value!);
                }
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

        List<triggerType> triggerEntries = new List<triggerType>();
        if (data.schedule is not null)
        {
            foreach (var schedule in data.schedule)
            {
                if (schedule is not null && schedule.trigger is not null)
                {
                    triggerEntries.AddRange(schedule.trigger);
                }
            }
        }

        logger.LogDebug("Found {TriggerCount} trigger definitions.", triggerEntries.Count);

        foreach (triggerType triggerNode in triggerEntries)
        {
            var triggerName = triggerNode.Item.name.TrimEmptyToNull()!;
            var triggerGroup = triggerNode.Item.group.TrimEmptyToNull() ?? Key<string>.DefaultGroup;
            var triggerDescription = triggerNode.Item.description.TrimEmptyToNull();
            var triggerCalendarRef = triggerNode.Item.calendarname.TrimEmptyToNull();
            string triggerJobName = triggerNode.Item.jobname.TrimEmptyToNull()!;
            string triggerJobGroup = triggerNode.Item.jobgroup.TrimEmptyToNull() ?? Key<string>.DefaultGroup;

            int triggerPriority = TriggerConstants.DefaultPriority;
            if (!string.IsNullOrWhiteSpace(triggerNode.Item.priority))
            {
                triggerPriority = Convert.ToInt32(triggerNode.Item.priority);
            }

            DateTimeOffset triggerStartTime = timeProvider.GetUtcNow();
            if (triggerNode.Item.Item is not null)
            {
                if (triggerNode.Item.Item is DateTime time)
                {
                    triggerStartTime = new DateTimeOffset(time);
                }
                else
                {
                    triggerStartTime = triggerStartTime.AddSeconds(Convert.ToInt32(triggerNode.Item.Item));
                }
            }

            DateTimeOffset? triggerEndTime = triggerNode.Item.endtimeSpecified ? new DateTimeOffset(triggerNode.Item.endtime) : null;

            IScheduleBuilder sched;

            if (triggerNode.Item is simpleTriggerType simpleTrigger)
            {
                var repeatCountString = simpleTrigger.repeatcount.TrimEmptyToNull();
                var repeatIntervalString = simpleTrigger.repeatinterval.TrimEmptyToNull();

                int repeatCount = ParseSimpleTriggerRepeatCount(repeatCountString!);
                TimeSpan repeatInterval = repeatIntervalString is null ? TimeSpan.Zero : TimeSpan.FromMilliseconds(Convert.ToInt64(repeatIntervalString));

                sched = SimpleScheduleBuilder.Create()
                    .WithInterval(repeatInterval)
                    .WithRepeatCount(repeatCount);

                if (!string.IsNullOrWhiteSpace(simpleTrigger.misfireinstruction))
                {
                    ((SimpleScheduleBuilder) sched).WithMisfireHandlingInstruction(ReadMisfireInstructionFromString(simpleTrigger.misfireinstruction));
                }
            }
            else if (triggerNode.Item is cronTriggerType cronTrigger)
            {
                var cronExpression = cronTrigger.cronexpression.TrimEmptyToNull();
                var timezoneString = cronTrigger.timezone.TrimEmptyToNull();

                TimeZoneInfo? tz = timezoneString is not null ? TimeZoneUtil.FindTimeZoneById(timezoneString) : null;
                sched = CronScheduleBuilder.CronSchedule(cronExpression!)
                    .InTimeZone(tz!);

                if (!string.IsNullOrWhiteSpace(cronTrigger.misfireinstruction))
                {
                    ((CronScheduleBuilder) sched).WithMisfireHandlingInstruction(ReadMisfireInstructionFromString(cronTrigger.misfireinstruction));
                }
            }
            else if (triggerNode.Item is calendarIntervalTriggerType)
            {
                calendarIntervalTriggerType calendarIntervalTrigger = (calendarIntervalTriggerType) triggerNode.Item;
                var repeatIntervalString = calendarIntervalTrigger.repeatinterval.TrimEmptyToNull();

                IntervalUnit intervalUnit = ParseDateIntervalTriggerIntervalUnit(calendarIntervalTrigger.repeatintervalunit.TrimEmptyToNull());
                int repeatInterval = repeatIntervalString is null ? 0 : Convert.ToInt32(repeatIntervalString);

                sched = CalendarIntervalScheduleBuilder.Create()
                    .WithInterval(repeatInterval, intervalUnit);

                if (!string.IsNullOrWhiteSpace(calendarIntervalTrigger.misfireinstruction))
                {
                    ((CalendarIntervalScheduleBuilder) sched).WithMisfireHandlingInstruction(ReadMisfireInstructionFromString(calendarIntervalTrigger.misfireinstruction));
                }
            }
            else
            {
                ThrowHelper.ThrowSchedulerConfigException("Unknown trigger type in XML configuration");
                return;
            }

            IMutableTrigger trigger = (IMutableTrigger) TriggerBuilder.Create()
                .WithIdentity(triggerName, triggerGroup)
                .WithDescription(triggerDescription)
                .ForJob(triggerJobName, triggerJobGroup)
                .StartAt(triggerStartTime)
                .EndAt(triggerEndTime)
                .WithPriority(triggerPriority)
                .ModifiedByCalendar(triggerCalendarRef)
                .WithSchedule(sched)
                .Build();

            if (triggerNode.Item.jobdatamap is not null && triggerNode.Item.jobdatamap.entry is not null)
            {
                foreach (entryType entry in triggerNode.Item.jobdatamap.entry)
                {
                    string key = entry.key.TrimEmptyToNull()!;
                    var value = entry.value.TrimEmptyToNull();
                    trigger.JobDataMap.Add(key, value!);
                }
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

    protected virtual int ParseSimpleTriggerRepeatCount(string repeatcount)
    {
        int value = Convert.ToInt32(repeatcount, CultureInfo.InvariantCulture);
        return value;
    }

    protected virtual int ReadMisfireInstructionFromString(string misfireinstruction)
    {
        Constants c = new Constants(typeof(MisfireInstruction), typeof(MisfireInstruction.CronTrigger),
            typeof(MisfireInstruction.SimpleTrigger));
        return c.AsNumber(misfireinstruction);
    }

    protected virtual IntervalUnit ParseDateIntervalTriggerIntervalUnit(string? intervalUnit)
    {
        if (string.IsNullOrEmpty(intervalUnit) || intervalUnit is null)
        {
            return IntervalUnit.Day;
        }

        if (!TryParseEnum(intervalUnit, out IntervalUnit retValue))
        {
            ThrowHelper.ThrowSchedulerConfigException("Unknown interval unit for DateIntervalTrigger: " + intervalUnit);
        }

        return retValue;
    }

    protected virtual bool TryParseEnum<T>(string str, out T value) where T : struct
    {
        var names = Enum.GetNames(typeof(T));
        value = (T) Enum.GetValues(typeof(T)).GetValue(0)!;
        foreach (var name in names)
        {
            if (name == str)
            {
                value = Enum.Parse<T>(name);
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

            using var stream = typeof(XMLSchedulingDataProcessor).Assembly.GetManifestResourceStream(QuartzXsdResourceName);

            if (stream is null)
            {
                ThrowHelper.ThrowArgumentException("Could not read XSD from embedded resource");
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
    /// <remarks>Note that we will set overWriteExistingJobs after the default xml is parsed.</remarks>
    public async ValueTask ProcessFileAndScheduleJobs(
        IScheduler sched,
        bool overWriteExistingJobs,
        CancellationToken cancellationToken = default)
    {
        await ProcessFile(QuartzXmlFileName, QuartzXmlFileName, cancellationToken).ConfigureAwait(false);
        // The overWriteExistingJobs flag was set by processFile() -> prepForProcessing(), then by xml parsing, and then now
        // we need to reset it again here by this method parameter to override it.
        OverWriteExistingData = overWriteExistingJobs;
        await ExecutePreProcessCommands(sched, cancellationToken).ConfigureAwait(false);
        await ScheduleJobs(sched, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Process the xml file in the default location, and schedule all of the
    /// jobs defined within it.
    /// </summary>
    public virtual ValueTask ProcessFileAndScheduleJobs(
        IScheduler sched,
        CancellationToken cancellationToken = default)
    {
        return ProcessFileAndScheduleJobs(QuartzXmlFileName, sched, cancellationToken);
    }

    /// <summary>
    /// Process the xml file in the given location, and schedule all of the
    /// jobs defined within it.
    /// </summary>
    /// <param name="fileName">meta data file name.</param>
    /// <param name="sched">The scheduler.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public virtual ValueTask ProcessFileAndScheduleJobs(
        string fileName,
        IScheduler sched,
        CancellationToken cancellationToken = default)
    {
        return ProcessFileAndScheduleJobs(fileName, fileName, sched, cancellationToken);
    }

    /// <summary>
    /// Process the xml file in the given location, and schedule all of the
    /// jobs defined within it.
    /// </summary>
    /// <param name="fileName">Name of the file.</param>
    /// <param name="systemId">The system id.</param>
    /// <param name="sched">The sched.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public virtual async ValueTask ProcessFileAndScheduleJobs(
        string fileName,
        string systemId,
        IScheduler sched,
        CancellationToken cancellationToken = default)
    {
        await ProcessFile(fileName, systemId, cancellationToken).ConfigureAwait(false);
        await ExecutePreProcessCommands(sched, cancellationToken).ConfigureAwait(false);
        await ScheduleJobs(sched, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Process the xml file in the given location, and schedule all of the
    /// jobs defined within it.
    /// </summary>
    /// <param name="stream">stream to read XML data from.</param>
    /// <param name="sched">The sched.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public virtual async ValueTask ProcessStreamAndScheduleJobs(
        Stream stream,
        IScheduler sched,
        CancellationToken cancellationToken = default)
    {
        using (var sr = new StreamReader(stream))
        {
            ProcessInternal(await sr.ReadToEndAsync(cancellationToken).ConfigureAwait(false));
        }

        await ExecutePreProcessCommands(sched, cancellationToken).ConfigureAwait(false);
        await ScheduleJobs(sched, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Schedules the given sets of jobs and triggers.
    /// </summary>
    /// <param name="sched">The sched.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public virtual async ValueTask ScheduleJobs(
        IScheduler sched,
        CancellationToken cancellationToken = default)
    {
        List<IJobDetail> jobs = new List<IJobDetail>(LoadedJobs);
        List<ITrigger> triggers = new List<ITrigger>(LoadedTriggers);

        logger.LogInformation("Adding {JobCount} jobs, {TriggerCount} triggers", jobs.Count, triggers.Count);

        IDictionary<JobKey, List<IMutableTrigger>> triggersByFQJobName = BuildTriggersByFQJobNameMap(triggers);

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
                dupeJ = await sched.GetJobDetail(detail.Key, cancellationToken).ConfigureAwait(false);
            }
            catch (JobPersistenceException e)
            {
                if (e.InnerException is TypeLoadException && OverWriteExistingData)
                {
                    // We are going to replace jobDetail anyway, so just delete it first.
                    logger.LogInformation("Removing job: {JobKey}", detail.Key);
                    await sched.DeleteJob(detail.Key, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    throw;
                }
            }

            if (dupeJ is not null)
            {
                if (!OverWriteExistingData && IgnoreDuplicates)
                {
                    logger.LogInformation("Not overwriting existing job: {JobKey}", dupeJ.Key);
                    continue; // just ignore the entry
                }

                if (!OverWriteExistingData && !IgnoreDuplicates)
                {
                    ThrowHelper.ThrowObjectAlreadyExistsException(detail);
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
                    ThrowHelper.ThrowSchedulerException(
                        "A new job defined without any triggers must be durable: " +
                        detail.Key);
                }

                if (dupeJ.Durable && (await sched.GetTriggersOfJob(detail.Key, cancellationToken).ConfigureAwait(false)).Count == 0)
                {
                    ThrowHelper.ThrowSchedulerException(
                        "Can't change existing durable job without triggers to non-durable: " +
                        detail.Key);
                }
            }

            if (dupeJ is not null || detail.Durable)
            {
                if (triggersOfJob is not null && triggersOfJob.Count > 0)
                {
                    await sched.AddJob(detail, true, true, cancellationToken).ConfigureAwait(false); // add the job regardless is durable or not b/c we have trigger to add
                }
                else
                {
                    await sched.AddJob(detail, true, false, cancellationToken).ConfigureAwait(false); // add the job only if a replacement or durable, else exception will throw!
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

                    ITrigger? dupeT = await sched.GetTrigger(trigger.Key, cancellationToken).ConfigureAwait(false);
                    if (dupeT is not null)
                    {
                        if (OverWriteExistingData)
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
                            ThrowHelper.ThrowObjectAlreadyExistsException(trigger);
                        }

                        if (!dupeT.JobKey.Equals(trigger.JobKey))
                        {
                            ReportDuplicateTrigger(trigger);
                        }

                        await DoRescheduleJob(sched, trigger, dupeT, cancellationToken).ConfigureAwait(false);
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
                                await sched.ScheduleJob(detail, trigger, cancellationToken).ConfigureAwait(false); // add the job if it's not in yet...
                                addJobWithFirstSchedule = false;
                            }
                            else
                            {
                                await sched.ScheduleJob(trigger, cancellationToken).ConfigureAwait(false);
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
                            var oldTrigger = await sched.GetTrigger(trigger.Key, cancellationToken).ConfigureAwait(false);
                            await DoRescheduleJob(sched, trigger, oldTrigger, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }
        }

        // add triggers that weren't associated with a new job... (those we already handled were removed above)
        foreach (IMutableTrigger trigger in triggers)
        {
            ITrigger? dupeT = await sched.GetTrigger(trigger.Key, cancellationToken).ConfigureAwait(false);
            if (dupeT is not null)
            {
                if (OverWriteExistingData)
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
                    ThrowHelper.ThrowObjectAlreadyExistsException(trigger);
                }

                if (!dupeT.JobKey.Equals(trigger.JobKey))
                {
                    ReportDuplicateTrigger(trigger);
                }

                await DoRescheduleJob(sched, trigger, dupeT, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug("Scheduling job: {JobKey} with trigger: {TriggerKey}", trigger.JobKey, trigger.Key);
                }

                try
                {
                    await sched.ScheduleJob(trigger, cancellationToken).ConfigureAwait(false);
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
                    var oldTrigger = await sched.GetTrigger(trigger.Key, cancellationToken).ConfigureAwait(false);
                    await DoRescheduleJob(sched, trigger, oldTrigger, cancellationToken).ConfigureAwait(false);
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
        IScheduler sched,
        IMutableTrigger trigger,
        ITrigger? oldTrigger,
        CancellationToken cancellationToken = default)
    {
        // if this is a trigger with default start time we can consider relative scheduling
        if (oldTrigger is not null && trigger.StartTimeUtc - timeProvider.GetUtcNow() < TimeSpan.FromSeconds(5) && ScheduleTriggerRelativeToReplacedTrigger)
        {
            logger.LogDebug("Using relative scheduling for trigger with key {TriggerKey}", trigger.Key);

            var oldTriggerPreviousFireTime = oldTrigger.GetPreviousFireTimeUtc();
            trigger.StartTimeUtc = oldTrigger.StartTimeUtc;
            ((IOperableTrigger) trigger).SetPreviousFireTimeUtc(oldTriggerPreviousFireTime);
            // if oldTriggerPreviousFireTime is null then NextFireTime should be set relative to oldTrigger.StartTimeUtc
            // to be able to handle misfiring for an existing trigger that has never been executed before.
            ((IOperableTrigger) trigger).SetNextFireTimeUtc(trigger.GetFireTimeAfter(oldTriggerPreviousFireTime ?? oldTrigger.StartTimeUtc));
        }

        return sched.RescheduleJob(trigger.Key, trigger, cancellationToken);
    }

    protected virtual IDictionary<JobKey, List<IMutableTrigger>> BuildTriggersByFQJobNameMap(List<ITrigger> triggers)
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

    protected async ValueTask ExecutePreProcessCommands(
        IScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        foreach (string group in jobGroupsToDelete)
        {
            if (group == "*")
            {
                logger.LogInformation("Deleting all jobs in ALL groups.");
                foreach (string groupName in await scheduler.GetJobGroupNames(cancellationToken).ConfigureAwait(false))
                {
                    if (!jobGroupsToNeverDelete.Contains(groupName))
                    {
                        foreach (JobKey key in await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(groupName), cancellationToken).ConfigureAwait(false))
                        {
                            await scheduler.DeleteJob(key, cancellationToken).ConfigureAwait(false);
                        }
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
                foreach (string groupName in await scheduler.GetTriggerGroupNames(cancellationToken).ConfigureAwait(false))
                {
                    if (!triggerGroupsToNeverDelete.Contains(groupName))
                    {
                        foreach (TriggerKey key in await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals(groupName), cancellationToken).ConfigureAwait(false))
                        {
                            await scheduler.UnscheduleJob(key, cancellationToken).ConfigureAwait(false);
                        }
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
    /// Throws a ValidationException if the number of validationExceptions
    /// detected is greater than zero.
    /// </summary>
    /// <exception cref="ValidationException">
    /// DTD validation exception.
    /// </exception>
    protected virtual void MaybeThrowValidationException()
    {
        if (validationExceptions.Count > 0)
        {
            throw new ValidationException(validationExceptions);
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

    /// <summary>
    /// Helper class to map constant names to their values.
    /// </summary>
    internal sealed class Constants
    {
        private readonly Type[] types;

        public Constants(params Type[] reflectedTypes)
        {
            types = reflectedTypes;
        }

        public int AsNumber(string field)
        {
            foreach (Type type in types)
            {
                FieldInfo? fi = type.GetField(field);
                if (fi is not null)
                {
                    return Convert.ToInt32(fi.GetValue(null), CultureInfo.InvariantCulture);
                }
            }

            // not found
            ThrowHelper.ThrowArgumentException($"Unknown field '{field}'");
            return 0;
        }
    }
}