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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

using Quartz.Impl.Matchers;
using Quartz.Logging;
using Quartz.Spi;
using Quartz.Util;
using Quartz.Xml.JobSchedulingData20;

namespace Quartz.Xml
{
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

        /// <summary>
        /// Constructor for XMLSchedulingDataProcessor.
        /// </summary>
        public XMLSchedulingDataProcessor(ITypeLoadHelper typeLoadHelper)
        {
            OverWriteExistingData = true;
            IgnoreDuplicates = false;
            Log = LogProvider.GetLogger(GetType());
            TypeLoadHelper = typeLoadHelper;
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
        public bool OverWriteExistingData { get; set; }

        /// <summary>
        /// If true (and <see cref="OverWriteExistingData" /> is false) then any
        /// job/triggers encountered in this file that have names that already exist
        /// in the scheduler will be ignored, and no error will be produced.
        /// </summary>
        /// <seealso cref="OverWriteExistingData"/>
        public bool IgnoreDuplicates { get; set; }

        /// <summary>
        /// If true (and <see cref="OverWriteExistingData" /> is true) then any
        /// job/triggers encountered in this file that already exist is scheduler
        /// will be updated with start time relative to old trigger. Effectively
        /// new trigger's last fire time will be updated to old trigger's last fire time
        /// and trigger's next fire time will updated to be next from this last fire time.
        /// </summary>
        public bool ScheduleTriggerRelativeToReplacedTrigger { get; set; }

        /// <summary>
        /// Gets the log.
        /// </summary>
        /// <value>The log.</value>
        private ILog Log { get; }

        protected IReadOnlyList<IJobDetail> LoadedJobs => loadedJobs.AsReadOnly();

        protected IReadOnlyList<ITrigger> LoadedTriggers => loadedTriggers.AsReadOnly();

        protected ITypeLoadHelper TypeLoadHelper { get; }

        /// <summary>
        /// Process the xml file in the default location (a file named
        /// "quartz_jobs.xml" in the current working directory).
        /// </summary>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        public virtual Task ProcessFile(CancellationToken cancellationToken = default)
        {
            return ProcessFile(QuartzXmlFileName, cancellationToken);
        }

        /// <summary>
        /// Process the xml file named <see param="fileName" />.
        /// </summary>
        /// <param name="fileName">meta data file name.</param>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        public virtual Task ProcessFile(
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
        public virtual async Task ProcessFile(
            string fileName,
            string systemId,
            CancellationToken cancellationToken = default)
        {
            // resolve file name first
            fileName = FileUtil.ResolveFile(fileName);

            Log.InfoFormat("Parsing XML file: {0} with systemId: {1}", fileName, systemId);

            using (var stream = File.Open(fileName, FileMode.Open, FileAccess.Read))
            using (StreamReader sr = new StreamReader(stream))
            {
                ProcessInternal(await sr.ReadToEndAsync().ConfigureAwait(false));
            }
        }

        /// <summary>
        /// Process the xmlfile named <see param="fileName" /> with the given system
        /// ID.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <param name="systemId">The system id.</param>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        public virtual async Task ProcessStream(
            Stream stream,
            string systemId,
            CancellationToken cancellationToken = default)
        {
            Log.InfoFormat("Parsing XML from stream with systemId: {0}", systemId);
            using (StreamReader sr = new StreamReader(stream))
            {
                ProcessInternal(await sr.ReadToEndAsync().ConfigureAwait(false));
            }
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
            XmlSerializer xs = new XmlSerializer(typeof (QuartzXmlConfiguration20));
            QuartzXmlConfiguration20 data = (QuartzXmlConfiguration20) xs.Deserialize(new StringReader(xml));

            if (data == null)
            {
                throw new SchedulerConfigException("Job definition data from XML was null after deserialization");
            }

            //
            // Extract pre-processing commands
            //
            if (data.preprocessingcommands != null)
            {
                foreach (preprocessingcommandsType command in data.preprocessingcommands)
                {
                    if (command.deletejobsingroup != null)
                    {
                        foreach (string s in command.deletejobsingroup)
                        {
                            string deleteJobGroup = s.NullSafeTrim();
                            if (!string.IsNullOrEmpty(deleteJobGroup))
                            {
                                jobGroupsToDelete.Add(deleteJobGroup);
                            }
                        }
                    }
                    if (command.deletetriggersingroup != null)
                    {
                        foreach (string s in command.deletetriggersingroup)
                        {
                            string deleteTriggerGroup = s.NullSafeTrim();
                            if (!string.IsNullOrEmpty(deleteTriggerGroup))
                            {
                                triggerGroupsToDelete.Add(deleteTriggerGroup);
                            }
                        }
                    }
                    if (command.deletejob != null)
                    {
                        foreach (preprocessingcommandsTypeDeletejob s in command.deletejob)
                        {
                            string name = s.name.TrimEmptyToNull();
                            string group = s.group.TrimEmptyToNull();

                            if (name == null)
                            {
                                throw new SchedulerConfigException("Encountered a 'delete-job' command without a name specified.");
                            }
                            jobsToDelete.Add(new JobKey(name, group));
                        }
                    }
                    if (command.deletetrigger != null)
                    {
                        foreach (preprocessingcommandsTypeDeletetrigger s in command.deletetrigger)
                        {
                            string name = s.name.TrimEmptyToNull();
                            string group = s.group.TrimEmptyToNull();

                            if (name == null)
                            {
                                throw new SchedulerConfigException("Encountered a 'delete-trigger' command without a name specified.");
                            }
                            triggersToDelete.Add(new TriggerKey(name, group));
                        }
                    }
                }
            }

            if (Log.IsDebugEnabled())
            {
                Log.Debug("Found " + jobGroupsToDelete.Count + " delete job group commands.");
                Log.Debug("Found " + triggerGroupsToDelete.Count + " delete trigger group commands.");
                Log.Debug("Found " + jobsToDelete.Count + " delete job commands.");
                Log.Debug("Found " + triggersToDelete.Count + " delete trigger commands.");
            }

            //
            // Extract directives
            //
            if (data.processingdirectives != null && data.processingdirectives.Length > 0)
            {
                bool overWrite = data.processingdirectives[0].overwriteexistingdata;
                Log.Debug("Directive 'overwrite-existing-data' specified as: " + overWrite);
                OverWriteExistingData = overWrite;
            }
            else
            {
                Log.Debug("Directive 'overwrite-existing-data' not specified, defaulting to " + OverWriteExistingData);
            }

            if (data.processingdirectives != null && data.processingdirectives.Length > 0)
            {
                bool ignoreduplicates = data.processingdirectives[0].ignoreduplicates;
                Log.Debug("Directive 'ignore-duplicates' specified as: " + ignoreduplicates);
                IgnoreDuplicates = ignoreduplicates;
            }
            else
            {
                Log.Debug("Directive 'ignore-duplicates' not specified, defaulting to " + IgnoreDuplicates);
            }

            if (data.processingdirectives != null && data.processingdirectives.Length > 0)
            {
                bool scheduleRelative = data.processingdirectives[0].scheduletriggerrelativetoreplacedtrigger;
                Log.Debug("Directive 'schedule-trigger-relative-to-replaced-trigger' specified as: " + scheduleRelative);
                ScheduleTriggerRelativeToReplacedTrigger = scheduleRelative;
            }
            else
            {
                Log.Debug("Directive 'schedule-trigger-relative-to-replaced-trigger' not specified, defaulting to " + ScheduleTriggerRelativeToReplacedTrigger);
            }

            //
            // Extract Job definitions...
            //
            List<jobdetailType> jobNodes = new List<jobdetailType>();
            if (data.schedule != null)
            {
                foreach (var schedule in data.schedule)
                {
                    if (schedule?.job != null)
                    {
                        jobNodes.AddRange(schedule.job);
                    }
                }
            }

            Log.Debug("Found " + jobNodes.Count + " job definitions.");

            foreach (jobdetailType jobDetailType in jobNodes)
            {
                string jobName = jobDetailType.name.TrimEmptyToNull();
                string jobGroup = jobDetailType.group.TrimEmptyToNull();
                string jobDescription = jobDetailType.description.TrimEmptyToNull();
                string jobTypeName = jobDetailType.jobtype.TrimEmptyToNull();
                bool jobDurability = jobDetailType.durable;
                bool jobRecoveryRequested = jobDetailType.recover;

                Type jobType = TypeLoadHelper.LoadType(jobTypeName);


                IJobDetail jobDetail = JobBuilder.Create(jobType)
                    .WithIdentity(jobName, jobGroup)
                    .WithDescription(jobDescription)
                    .StoreDurably(jobDurability)
                    .RequestRecovery(jobRecoveryRequested)
                    .Build();

                if (jobDetailType.jobdatamap != null && jobDetailType.jobdatamap.entry != null)
                {
                    foreach (entryType entry in jobDetailType.jobdatamap.entry)
                    {
                        string key = entry.key.TrimEmptyToNull();
                        string value = entry.value.TrimEmptyToNull();
                        jobDetail.JobDataMap.Add(key, value);
                    }
                }

                if (Log.IsDebugEnabled())
                {
                    Log.Debug("Parsed job definition: " + jobDetail);
                }

                AddJobToSchedule(jobDetail);
            }

            //
            // Extract Trigger definitions...
            //

            List<triggerType> triggerEntries = new List<triggerType>();
            if (data.schedule != null)
            {
                foreach (var schedule in data.schedule)
                {
                    if (schedule != null && schedule.trigger != null)
                    {
                        triggerEntries.AddRange(schedule.trigger);
                    }
                }
            }

            Log.Debug("Found " + triggerEntries.Count + " trigger definitions.");

            foreach (triggerType triggerNode in triggerEntries)
            {
                string triggerName = triggerNode.Item.name.TrimEmptyToNull();
                string triggerGroup = triggerNode.Item.group.TrimEmptyToNull();
                string triggerDescription = triggerNode.Item.description.TrimEmptyToNull();
                string triggerCalendarRef = triggerNode.Item.calendarname.TrimEmptyToNull();
                string triggerJobName = triggerNode.Item.jobname.TrimEmptyToNull();
                string triggerJobGroup = triggerNode.Item.jobgroup.TrimEmptyToNull();

                int triggerPriority = TriggerConstants.DefaultPriority;
                if (!triggerNode.Item.priority.IsNullOrWhiteSpace())
                {
                    triggerPriority = Convert.ToInt32(triggerNode.Item.priority);
                }

                DateTimeOffset triggerStartTime = SystemTime.UtcNow();
                if (triggerNode.Item.Item != null)
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

                DateTime? triggerEndTime = triggerNode.Item.endtimeSpecified ? triggerNode.Item.endtime : (DateTime?) null;

                IScheduleBuilder sched;

                if (triggerNode.Item is simpleTriggerType simpleTrigger)
                {
                    string repeatCountString = simpleTrigger.repeatcount.TrimEmptyToNull();
                    string repeatIntervalString = simpleTrigger.repeatinterval.TrimEmptyToNull();

                    int repeatCount = ParseSimpleTriggerRepeatCount(repeatCountString);
                    TimeSpan repeatInterval = repeatIntervalString == null ? TimeSpan.Zero : TimeSpan.FromMilliseconds(Convert.ToInt64(repeatIntervalString));

                    sched = SimpleScheduleBuilder.Create()
                        .WithInterval(repeatInterval)
                        .WithRepeatCount(repeatCount);

                    if (!simpleTrigger.misfireinstruction.IsNullOrWhiteSpace())
                    {
                        ((SimpleScheduleBuilder) sched).WithMisfireHandlingInstruction(ReadMisfireInstructionFromString(simpleTrigger.misfireinstruction));
                    }
                }
                else if (triggerNode.Item is cronTriggerType)
                {
                    cronTriggerType cronTrigger = (cronTriggerType) triggerNode.Item;
                    string cronExpression = cronTrigger.cronexpression.TrimEmptyToNull();
                    string timezoneString = cronTrigger.timezone.TrimEmptyToNull();

                    TimeZoneInfo tz = timezoneString != null ? TimeZoneUtil.FindTimeZoneById(timezoneString) : null;
                    sched = CronScheduleBuilder.CronSchedule(cronExpression)
                        .InTimeZone(tz);

                    if (!cronTrigger.misfireinstruction.IsNullOrWhiteSpace())
                    {
                        ((CronScheduleBuilder) sched).WithMisfireHandlingInstruction(ReadMisfireInstructionFromString(cronTrigger.misfireinstruction));
                    }
                }
                else if (triggerNode.Item is calendarIntervalTriggerType)
                {
                    calendarIntervalTriggerType calendarIntervalTrigger = (calendarIntervalTriggerType) triggerNode.Item;
                    string repeatIntervalString = calendarIntervalTrigger.repeatinterval.TrimEmptyToNull();

                    IntervalUnit intervalUnit = ParseDateIntervalTriggerIntervalUnit(calendarIntervalTrigger.repeatintervalunit.TrimEmptyToNull());
                    int repeatInterval = repeatIntervalString == null ? 0 : Convert.ToInt32(repeatIntervalString);

                    sched = CalendarIntervalScheduleBuilder.Create()
                        .WithInterval(repeatInterval, intervalUnit);

                    if (!calendarIntervalTrigger.misfireinstruction.IsNullOrWhiteSpace())
                    {
                        ((CalendarIntervalScheduleBuilder) sched).WithMisfireHandlingInstruction(ReadMisfireInstructionFromString(calendarIntervalTrigger.misfireinstruction));
                    }
                }
                else
                {
                    throw new SchedulerConfigException("Unknown trigger type in XML configuration");
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

                if (triggerNode.Item.jobdatamap != null && triggerNode.Item.jobdatamap.entry != null)
                {
                    foreach (entryType entry in triggerNode.Item.jobdatamap.entry)
                    {
                        string key = entry.key.TrimEmptyToNull();
                        string value = entry.value.TrimEmptyToNull();
                        trigger.JobDataMap.Add(key, value);
                    }
                }

                if (Log.IsDebugEnabled())
                {
                    Log.Debug("Parsed trigger definition: " + trigger);
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
            Constants c = new Constants(typeof (MisfireInstruction), typeof (MisfireInstruction.CronTrigger),
                                        typeof (MisfireInstruction.SimpleTrigger));
            return c.AsNumber(misfireinstruction);
        }

        protected virtual IntervalUnit ParseDateIntervalTriggerIntervalUnit(string intervalUnit)
        {
            if (string.IsNullOrEmpty(intervalUnit))
            {
                return IntervalUnit.Day;
            }

            if (!TryParseEnum(intervalUnit, out IntervalUnit retValue))
            {
                throw new SchedulerConfigException("Unknown interval unit for DateIntervalTrigger: " + intervalUnit);
            }
            return retValue;
        }

        protected virtual bool TryParseEnum<T>(string str, out T value) where T : struct
        {
            var names = Enum.GetNames(typeof (T));
            value = (T) Enum.GetValues(typeof (T)).GetValue(0);
            foreach (var name in names)
            {
                if (name == str)
                {
                    value = (T) Enum.Parse(typeof (T), name);
                    return true;
                }
            }
            return false;
        }

        private void ValidateXml(string xml)
        {
            try
            {
                XmlReaderSettings settings = new XmlReaderSettings();
                settings.ValidationType = ValidationType.Schema;
                settings.ValidationFlags |= XmlSchemaValidationFlags.ProcessInlineSchema;
                settings.ValidationFlags |= XmlSchemaValidationFlags.ProcessSchemaLocation;
                settings.ValidationFlags |= XmlSchemaValidationFlags.ReportValidationWarnings;

                using (Stream stream = typeof(XMLSchedulingDataProcessor).Assembly.GetManifestResourceStream(QuartzXsdResourceName))
                {
                    XmlSchema schema = XmlSchema.Read(stream, XmlValidationCallBack);
                    settings.Schemas.Add(schema);
                    settings.ValidationEventHandler += XmlValidationCallBack;

                    // stream to validate
                    using (XmlReader reader = XmlReader.Create(new StringReader(xml), settings))
                    {
                        while (reader.Read())
                        {
                        }
                    }
                }
                }
            catch (Exception ex)
            {
                Log.WarnException("Unable to validate XML with schema: " + ex.Message, ex);
            }
        }

        private void XmlValidationCallBack(object sender, ValidationEventArgs e)
        {
            if (e.Severity == XmlSeverityType.Error)
            {
                validationExceptions.Add(e.Exception);
            }
            else
            {
                Log.Warn(e.Message);
            }
        }

        /// <summary>
        /// Process the xml file in the default location, and schedule all of the jobs defined within it.
        /// </summary>
        /// <remarks>Note that we will set overWriteExistingJobs after the default xml is parsed.</remarks>
        public async Task ProcessFileAndScheduleJobs(
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
        public virtual Task ProcessFileAndScheduleJobs(
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
        public virtual Task ProcessFileAndScheduleJobs(
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
        public virtual async Task ProcessFileAndScheduleJobs(
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
        public virtual async Task ProcessStreamAndScheduleJobs(
            Stream stream,
            IScheduler sched,
            CancellationToken cancellationToken = default)
        {
            using (var sr = new StreamReader(stream))
            {
                ProcessInternal(await sr.ReadToEndAsync().ConfigureAwait(false));
            }
            await ExecutePreProcessCommands(sched, cancellationToken).ConfigureAwait(false);
            await ScheduleJobs(sched, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Schedules the given sets of jobs and triggers.
        /// </summary>
        /// <param name="sched">The sched.</param>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        public virtual async Task ScheduleJobs(
            IScheduler sched,
            CancellationToken cancellationToken = default)
        {
            List<IJobDetail> jobs = new List<IJobDetail>(LoadedJobs);
            List<ITrigger> triggers = new List<ITrigger>(LoadedTriggers);

            Log.Info("Adding " + jobs.Count + " jobs, " + triggers.Count + " triggers.");

            IDictionary<JobKey, List<IMutableTrigger>> triggersByFQJobName = BuildTriggersByFQJobNameMap(triggers);

            // add each job, and it's associated triggers
            while (jobs.Count > 0)
            {
                // remove jobs as we handle them...
                IJobDetail detail = jobs[0];
                jobs.Remove(detail);

                IJobDetail dupeJ = null;
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
                        Log.Info("Removing job: " + detail.Key);
                        await sched.DeleteJob(detail.Key, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        throw;
                    }
                }

                if (dupeJ != null)
                {
                    if (!OverWriteExistingData && IgnoreDuplicates)
                    {
                        Log.Info("Not overwriting existing job: " + dupeJ.Key);
                        continue; // just ignore the entry
                    }
                    if (!OverWriteExistingData && !IgnoreDuplicates)
                    {
                        throw new ObjectAlreadyExistsException(detail);
                    }
                }

                if (dupeJ != null)
                {
                    Log.Info("Replacing job: " + detail.Key);
                }
                else
                {
                    Log.Info("Adding job: " + detail.Key);
                }

                triggersByFQJobName.TryGetValue(detail.Key, out var triggersOfJob);

                if (!detail.Durable && (triggersOfJob == null || triggersOfJob.Count == 0))
                {
                    if (dupeJ == null)
                    {
                        throw new SchedulerException(
                            "A new job defined without any triggers must be durable: " +
                            detail.Key);
                    }

                    if (dupeJ.Durable && (await sched.GetTriggersOfJob(detail.Key, cancellationToken).ConfigureAwait(false)).Count == 0)
                    {
                        throw new SchedulerException(
                            "Can't change existing durable job without triggers to non-durable: " +
                            detail.Key);
                    }
                }


                if (dupeJ != null || detail.Durable)
                {
                    if (triggersOfJob != null && triggersOfJob.Count > 0)
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
                    while (triggersOfJob.Count > 0)
                    {
                        IMutableTrigger trigger = triggersOfJob[0];
                        // remove triggers as we handle them...
                        triggersOfJob.Remove(trigger);

                        ITrigger dupeT = await sched.GetTrigger(trigger.Key, cancellationToken).ConfigureAwait(false);
                        if (dupeT != null)
                        {
                            if (OverWriteExistingData)
                            {
                                if (Log.IsDebugEnabled())
                                {
                                    Log.DebugFormat("Rescheduling job: {0} with updated trigger: {1}", trigger.JobKey, trigger.Key);
                                }
                            }
                            else if (IgnoreDuplicates)
                            {
                                Log.Info("Not overwriting existing trigger: " + dupeT.Key);
                                continue; // just ignore the trigger (and possibly job)
                            }
                            else
                            {
                                throw new ObjectAlreadyExistsException(trigger);
                            }

                            if (!dupeT.JobKey.Equals(trigger.JobKey))
                            {
                                Log.WarnFormat("Possibly duplicately named ({0}) triggers in jobs xml file! ", trigger.Key);
                            }

                            await DoRescheduleJob(sched, trigger, dupeT, cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            if (Log.IsDebugEnabled())
                            {
                                Log.DebugFormat("Scheduling job: {0} with trigger: {1}", trigger.JobKey, trigger.Key);
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
                                if (Log.IsDebugEnabled())
                                {
                                    Log.DebugFormat("Adding trigger: {0} for job: {1} failed because the trigger already existed.  "
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
                ITrigger dupeT = await sched.GetTrigger(trigger.Key, cancellationToken).ConfigureAwait(false);
                if (dupeT != null)
                {
                    if (OverWriteExistingData)
                    {
                        if (Log.IsDebugEnabled())
                        {
                            Log.DebugFormat("Rescheduling job: " + trigger.JobKey + " with updated trigger: " + trigger.Key);
                        }
                    }
                    else if (IgnoreDuplicates)
                    {
                        Log.Info("Not overwriting existing trigger: " + dupeT.Key);
                        continue; // just ignore the trigger
                    }
                    else
                    {
                        throw new ObjectAlreadyExistsException(trigger);
                    }

                    if (!dupeT.JobKey.Equals(trigger.JobKey))
                    {
                        Log.WarnFormat("Possibly duplicately named ({0}) triggers in jobs xml file! ", trigger.Key);
                    }

                    await DoRescheduleJob(sched, trigger, dupeT, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    if (Log.IsDebugEnabled())
                    {
                        Log.DebugFormat("Scheduling job: {0} with trigger: {1}", trigger.JobKey, trigger.Key);
                    }

                    try
                    {
                        await sched.ScheduleJob(trigger, cancellationToken).ConfigureAwait(false);
                    }
                    catch (ObjectAlreadyExistsException)
                    {
                        if (Log.IsDebugEnabled())
                        {
                            Log.Debug(
                                "Adding trigger: " + trigger.Key + " for job: " + trigger.JobKey +
                                " failed because the trigger already existed.  " +
                                "This is likely due to a race condition between multiple instances " +
                                "in the cluster.  Will try to reschedule instead.");
                        }
                        // Let's rescheduleJob one more time.
                        var oldTrigger = await sched.GetTrigger(trigger.Key, cancellationToken).ConfigureAwait(false);
                        await DoRescheduleJob(sched, trigger, oldTrigger, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }

        private Task DoRescheduleJob(
            IScheduler sched,
            IMutableTrigger trigger,
            ITrigger oldTrigger,
            CancellationToken cancellationToken = default)
        {
            // if this is a trigger with default start time we can consider relative scheduling
            if (oldTrigger != null && trigger.StartTimeUtc - SystemTime.UtcNow() < TimeSpan.FromSeconds(5) && ScheduleTriggerRelativeToReplacedTrigger)
            {
                Log.DebugFormat("Using relative scheduling for trigger with key {0}", trigger.Key);

                var oldTriggerPreviousFireTime = oldTrigger.GetPreviousFireTimeUtc();
                trigger.StartTimeUtc = oldTrigger.StartTimeUtc;
                ((IOperableTrigger)trigger).SetPreviousFireTimeUtc(oldTriggerPreviousFireTime);
                ((IOperableTrigger)trigger).SetNextFireTimeUtc(trigger.GetFireTimeAfter(oldTriggerPreviousFireTime));
            }

            return sched.RescheduleJob(trigger.Key, trigger, cancellationToken);
        }

        protected virtual IDictionary<JobKey, List<IMutableTrigger>> BuildTriggersByFQJobNameMap(List<ITrigger> triggers)
        {
            IDictionary<JobKey, List<IMutableTrigger>> triggersByFQJobName = new Dictionary<JobKey, List<IMutableTrigger>>();

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

        protected async Task ExecutePreProcessCommands(
            IScheduler scheduler,
            CancellationToken cancellationToken = default)
        {
            foreach (string group in jobGroupsToDelete)
            {
                if (group.Equals("*"))
                {
                    Log.Info("Deleting all jobs in ALL groups.");
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
                        Log.InfoFormat("Deleting all jobs in group: {0}", group);
                        foreach (JobKey key in await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(group), cancellationToken).ConfigureAwait(false))
                        {
                            await scheduler.DeleteJob(key, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }

            foreach (string group in triggerGroupsToDelete)
            {
                if (group.Equals("*"))
                {
                    Log.Info("Deleting all triggers in ALL groups.");
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
                        Log.InfoFormat("Deleting all triggers in group: {0}", group);
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
                    Log.InfoFormat("Deleting job: {0}", key);
                    await scheduler.DeleteJob(key, cancellationToken).ConfigureAwait(false);
                }
            }

            foreach (TriggerKey key in triggersToDelete)
            {
                if (!triggerGroupsToNeverDelete.Contains(key.Group))
                {
                    Log.InfoFormat("Deleting trigger: {0}", key);
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
        internal class Constants
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
                    FieldInfo fi = type.GetField(field);
                    if (fi != null)
                    {
                        return Convert.ToInt32(fi.GetValue(null), CultureInfo.InvariantCulture);
                    }
                }

                // not found
                throw new Exception($"Unknown field '{field}'");
            }
        }
    }
}