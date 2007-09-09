/* 
* Copyright 2004-2005 OpenSymphony 
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

/*
* Previously Copyright (c) 2001-2004 James House
*/

using System;
using System.Collections;
using System.IO;
using System.Xml;

using Common.Logging;

using Quartz;
using Quartz.Util;

namespace Quartz.Xml
{
	/// <summary> 
	/// Parses an XML file that declares Jobs and their schedules (Triggers).
	/// </summary>
	/// <remarks>
	/// <p>
	/// The xml document must conform to the format defined in
	/// "job_scheduling_data.xsd"
	/// </p>
	/// 
	/// <p>
	/// After creating an instance of this class, you should call one of the <see cref="ProcessFile()" />
	/// functions, after which you may call the <see cref="ScheduledJobs()" />
	/// function to get a handle to the defined Jobs and Triggers, which can then be
	/// scheduled with the <see cref="IScheduler" />. Alternatively, you could call
	/// the <see cref="ProcessFileAndScheduleJobs(IScheduler,bool)" /> function to do all of this
	/// in one step.
	/// </p>
	/// 
	/// <p>
	/// The same instance can be used again and again, with the list of defined Jobs
	/// being cleared each time you call a <see cref="ProcessFile()" /> method,
	/// however a single instance is not thread-safe.
	/// </p>
    /// </remarks>
	/// <author><a href="mailto:bonhamcm@thirdeyeconsulting.com">Chris Bonham</a></author>
	/// <author>James House</author>
	/// <author>Marko Lahma (.NET)</author>
	public class JobSchedulingDataProcessor
	{
		private ILog log;
	    private bool validateXml;
	    private bool validateSchema;

		public const string QUARTZ_SYSTEM_ID_DIR_PROP = "quartz.system.id.dir";
		public const string QUARTZ_XML_FILE_NAME = "quartz_jobs.xml";
		public const string QUARTZ_SCHEMA = "http://quartznet.sourceforge.net/xml/job_scheduling_data.xsd";
		public const string QUARTZ_XSD = "Quartz.Quartz.Xml.job_scheduling_data.xsd";
		
		protected const string THREAD_LOCAL_KEY_SCHEDULDER = "quartz_scheduler";

		protected const string TAG_QUARTZ = "quartz";
		protected const string TAG_OVERWRITE_EXISTING_JOBS = "overwrite-existing-jobs";
		protected const string TAG_JOB_LISTENER = "job-listener";
		protected const string TAG_CALENDAR = "calendar";
		protected const string TAG_CLASS_NAME = "type-name";
		protected const string TAG_DESCRIPTION = "description";
		protected const string TAG_BASE_CALENDAR = "base-calendar";
		protected const string TAG_MISFIRE_INSTRUCTION = "misfire-instruction";
		protected const string TAG_CALENDAR_NAME = "calendar-name";
		protected const string TAG_JOB = "job";
		protected const string TAG_JOB_DETAIL = "job-detail";
		protected const string TAG_NAME = "name";
		protected const string TAG_GROUP = "group";
		protected const string TAG_JOB_CLASS = "job-type";
		protected const string TAG_JOB_LISTENER_REF = "job-listener-ref";
		protected const string TAG_VOLATILITY = "volatility";
		protected const string TAG_DURABILITY = "durability";
		protected const string TAG_RECOVER = "recover";
		protected const string TAG_JOB_DATA_MAP = "job-data-map";
		protected const string TAG_ENTRY = "entry";
		protected const string TAG_KEY = "key";
		protected const string TAG_ALLOWS_TRANSIENT_DATA = "allows-transient-data";
		protected const string TAG_VALUE = "value";
		protected const string TAG_TRIGGER = "trigger";
		protected const string TAG_SIMPLE = "simple";
		protected const string TAG_CRON = "cron";
		protected const string TAG_JOB_NAME = "job-name";
		protected const string TAG_JOB_GROUP = "job-group";
		protected const string TAG_START_TIME = "start-time";
		protected const string TAG_END_TIME = "end-time";
		protected const string TAG_REPEAT_COUNT = "repeat-count";
		protected const string TAG_REPEAT_INTERVAL = "repeat-interval";
		protected const string TAG_CRON_EXPRESSION = "cron-expression";
		protected const string TAG_TIME_ZONE = "time-zone";

		/// <summary> 
		/// XML Schema dateTime datatype format.
		/// <p>
		/// See <a href="http://www.w3.org/TR/2001/REC-xmlschema-2-20010502/#dateTime">
		/// http://www.w3.org/TR/2001/REC-xmlschema-2-20010502/#dateTime</a>
		/// </p>
		/// </summary>
		protected const string XSD_DATE_FORMAT = "yyyy-MM-dd'T'hh:mm:ss";

		protected IDictionary scheduledJobs = new Hashtable();
		protected IList jobsToSchedule = new ArrayList();
		protected IList calsToSchedule = new ArrayList();
		protected IList listenersToSchedule = new ArrayList();

		protected ArrayList validationExceptions = new ArrayList();

		private bool overWriteExistingJobs = true;
		
		/// <summary> 
		/// Gets or sets whether to overwrite existing jobs.
		/// </summary>
		public virtual bool OverWriteExistingJobs
		{
			get { return overWriteExistingJobs; }
			set { overWriteExistingJobs = value; }
		}


        /// <summary>
        /// Gets the log.
        /// </summary>
        /// <value>The log.</value>
	    protected internal ILog Log
	    {
	        get { return log; }
	    }

	    /// <summary> 
        /// Returns a <see cref="IDictionary" /> of scheduled jobs.
		/// <p>
		/// The key is the job name and the value is a <see cref="JobSchedulingBundle" />
		/// containing the <see cref="JobDetail" /> and <see cref="Trigger" />.
		/// </p>
		/// </summary>
        /// <returns> a <see cref="IDictionary" /> of scheduled jobs.
		/// </returns>
		public virtual IDictionary ScheduledJobs
		{
			get
			{
				return scheduledJobs;
			}
		}


		/// <summary>
		/// Constructor for QuartzMetaDataProcessor.
		/// </summary>
		public JobSchedulingDataProcessor() : this(true, true)
		{
		}

		/// <summary>
		/// Constructor for QuartzMetaDataProcessor.
		/// </summary>
		/// <param name="validateXml">whether or not to validate XML.</param>
		/// <param name="validateSchema">whether or not to validate XML schema.</param>
		public JobSchedulingDataProcessor(bool validateXml, bool validateSchema)
		{
		    this.validateXml = validateXml;
		    this.validateSchema = validateSchema;
		    log = LogManager.GetLogger(GetType());
		}


		/// <summary> 
		/// Process the xml file in the default location (a file named
		/// "quartz_jobs.xml" in the current working directory).
		/// </summary>
		public virtual void ProcessFile()
		{
			ProcessFile(QUARTZ_XML_FILE_NAME);
		}

		/// <summary>
		/// Process the xml file named <see param="fileName" />.
		/// </summary>
		/// <param name="fileName">meta data file name.</param>
		public virtual void ProcessFile(string fileName)
		{
			ProcessFile(fileName, fileName);
		}

		/// <summary>
		/// Process the xmlfile named <see param="fileName" /> with the given system
		/// ID.
		/// </summary>
		/// <param name="fileName">Name of the file.</param>
		/// <param name="systemId">The system id.</param>
		public virtual void ProcessFile(string fileName, string systemId)
		{
			Log.Info(string.Format("Parsing XML file: {0} with systemId: {1} validating: {2} validating schema: {3}", fileName, systemId, validateXml, validateSchema));
            using (StreamReader sr = new StreamReader(fileName))
            {
                ProcessInternal(sr.ReadToEnd());
            }
		}

		/// <summary>
		/// Process the xmlfile named <see param="fileName" /> with the given system
		/// ID.
		/// </summary>
		/// <param name="stream">The stream.</param>
		/// <param name="systemId">The system id.</param>
		public virtual void ProcessStream(Stream stream, string systemId)
		{
			Log.Info(string.Format("Parsing XML from stream with systemId: {0} validating: {1} validating schema: {2}", systemId, validateXml, validateSchema));
            using (StreamReader sr = new StreamReader(stream))
            {
                ProcessInternal(sr.ReadToEnd());
            }
		}

        protected virtual void ProcessInternal(string xml)
        {
            ClearValidationExceptions();

            scheduledJobs.Clear();
            jobsToSchedule.Clear();
            calsToSchedule.Clear();

            ValidateXmlIfNeeded(xml);
            

            MaybeThrowValidationException();
        }

	    private void ValidateXmlIfNeeded(string xml)
	    {
            if (validateXml)
            {
                // stream to validate
                using (StringReader stringReader = new StringReader(xml))
                {
                    // schema to validate against
                    /* XmlReaderSettings settings = new XmlReaderSettings();
                    string messageSchemaData = "";
                    settings.Schemas.Add(null, XmlReader.Create(new StringReader(messageSchemaData)));
                    settings.ValidationType = ValidationType.Schema;

                    // read document to ensure validity
                    using (XmlReader xmlReader = XmlReader.Create(stringReader, settings))
                    {
                        while (xmlReader.Read())
                        {
                        }
                    }
					*/
                }
            }
	    }


	    /// <summary> 
		/// Process the xml file in the default location, and schedule all of the
		/// jobs defined within it.
		/// </summary>
        public virtual void ProcessFileAndScheduleJobs(IScheduler sched, bool overwriteExistingJobs)
		{
            ProcessFileAndScheduleJobs(QUARTZ_XML_FILE_NAME, sched, overwriteExistingJobs);
		}

		/// <summary>
		/// Process the xml file in the given location, and schedule all of the
		/// jobs defined within it.
		/// </summary>
		/// <param name="fileName">meta data file name.</param>
		/// <param name="sched">The scheduler.</param>
		/// <param name="overwriteExistingJobs">if set to <c>true</c> overwrite existing jobs.</param>
		public virtual void ProcessFileAndScheduleJobs(string fileName, IScheduler sched, bool overwriteExistingJobs)
		{
			ProcessFileAndScheduleJobs(fileName, fileName, sched, overwriteExistingJobs);
		}

		/// <summary>
		/// Process the xml file in the given location, and schedule all of the
		/// jobs defined within it.
		/// </summary>
		/// <param name="fileName">Name of the file.</param>
		/// <param name="systemId">The system id.</param>
		/// <param name="sched">The sched.</param>
        /// <param name="overwriteExistingJobs">if set to <c>true</c> [over write existing jobs].</param>
		public virtual void ProcessFileAndScheduleJobs(string fileName, string systemId, IScheduler sched,
                                                       bool overwriteExistingJobs)
		{
			LogicalThreadContext.SetData(THREAD_LOCAL_KEY_SCHEDULDER, sched);
			try
			{
				ProcessFile(fileName, systemId);
                ScheduleJobs(ScheduledJobs, sched, overwriteExistingJobs);
			}
			finally
			{
				LogicalThreadContext.FreeNamedDataSlot(THREAD_LOCAL_KEY_SCHEDULDER);
			}
		}

		/// <summary>
		/// Add the Jobs and Triggers defined in the given map of <see cref="JobSchedulingBundle" />
		/// s to the given scheduler.
		/// </summary>
		/// <param name="jobBundles">The job bundles.</param>
		/// <param name="sched">The sched.</param>
		/// <param name="overwriteExistingJobs">if set to <c>true</c> [over write existing jobs].</param>
		public virtual void ScheduleJobs(IDictionary jobBundles, IScheduler sched, bool overwriteExistingJobs)
		{
			Log.Info("Scheduling " + jobsToSchedule.Count + " parsed jobs.");

			foreach (CalendarBundle bndle in calsToSchedule)
			{
				AddCalendar(sched, bndle);
			}

			foreach (JobSchedulingBundle bndle in jobsToSchedule)
			{
				ScheduleJob(bndle, sched, overwriteExistingJobs);
			}

			foreach (IJobListener listener in listenersToSchedule)
			{
				Log.Info("adding listener " + listener.Name + " of type " + listener.GetType().FullName);
				sched.AddJobListener(listener);
			}
			Log.Info(jobBundles.Count + " scheduled jobs.");
		}

		/// <summary>
		/// Returns a <see cref="JobSchedulingBundle" /> for the job name.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns>
		/// a <see cref="JobSchedulingBundle" /> for the job name.
		/// </returns>
		public virtual JobSchedulingBundle GetScheduledJob(string name)
		{
			return (JobSchedulingBundle) ScheduledJobs[name];
		}

		/// <summary>
        /// Returns an <see cref="Stream" /> from the fileName as a resource.
		/// </summary>
		/// <param name="fileName">Name of the file.</param>
		/// <returns>
        /// an <see cref="Stream" /> from the fileName as a resource.
		/// </returns>
		protected virtual Stream GetInputStream(string fileName)
		{
			return new StreamReader(fileName).BaseStream;
		}

		/// <summary>
		/// Schedules a given job and trigger (both wrapped by a <see cref="JobSchedulingBundle" />).
		/// </summary>
		/// <param name="job">job wrapper.</param>
		/// <exception cref="SchedulerException">
		/// if the Job or Trigger cannot be added to the Scheduler, or
		/// there is an internal Scheduler error.
		/// </exception>
		public virtual void ScheduleJob(JobSchedulingBundle job)
		{
			ScheduleJob(job, (IScheduler) LogicalThreadContext.GetData(THREAD_LOCAL_KEY_SCHEDULDER), OverWriteExistingJobs);
		}


		public virtual void AddJobToSchedule(JobSchedulingBundle job)
		{
			jobsToSchedule.Add(job);
		}

		public virtual void AddCalendarToSchedule(CalendarBundle cal)
		{
			calsToSchedule.Add(cal);
		}

		public virtual void AddListenerToSchedule(IJobListener listener)
		{
			listenersToSchedule.Add(listener);
		}

		/// <summary>
		/// Schedules a given job and trigger (both wrapped by a <see cref="JobSchedulingBundle" />).
		/// </summary>
		/// <param name="job">The job.</param>
		/// <param name="sched">The sched.</param>
		/// <param name="localOverWriteExistingJobs">if set to <c>true</c> [local over write existing jobs].</param>
		/// <exception cref="SchedulerException"> 
		/// if the Job or Trigger cannot be added to the Scheduler, or
		/// there is an internal Scheduler error.
		/// </exception>
		public virtual void ScheduleJob(JobSchedulingBundle job, IScheduler sched, bool localOverWriteExistingJobs)
		{
			if ((job != null) && job.Valid)
			{
				JobDetail detail = job.JobDetail;

				JobDetail dupeJ = sched.GetJobDetail(detail.Name, detail.Group);

				if ((dupeJ != null) && !localOverWriteExistingJobs)
				{
					Log.Info("Not overwriting existing job: " + dupeJ.FullName);
					return;
				}

				if (dupeJ != null)
				{
					Log.Info("Replacing job: " + detail.FullName);
				}
				else
				{
					Log.Info("Adding job: " + detail.FullName);
				}

				if (job.Triggers.Count == 0 && !job.JobDetail.Durable)
				{
					throw new SchedulerException("A Job defined without any triggers must be durable");
				}
				
				sched.AddJob(detail, true);

					
				foreach(Trigger trigger in job.Triggers)
				{
					Trigger dupeT = sched.GetTrigger(trigger.Name, trigger.Group);

					trigger.JobName = detail.Name;
					trigger.JobGroup = detail.Group;

					if (trigger.StartTimeUtc == DateTime.MinValue)
					{
						trigger.StartTimeUtc = DateTime.UtcNow;
					}

					if (dupeT != null)
					{
						Log.Debug("Rescheduling job: " + detail.FullName + " with updated trigger: " + trigger.FullName);
						if (!dupeT.JobGroup.Equals(trigger.JobGroup) || !dupeT.JobName.Equals(trigger.JobName))
						{
							Log.Warn("Possibly duplicately named triggers in jobs xml file!");
						}
						sched.RescheduleJob(trigger.Name, trigger.Group, trigger);
					}
					else
					{
						Log.Debug("Scheduling job: " + detail.FullName + " with trigger: " + trigger.FullName);
						sched.ScheduleJob(trigger);
					}
				}

				AddScheduledJob(job);
			}
		}

		/// <summary>
		/// Adds a scheduled job.
		/// </summary>
		/// <param name="job">The job.</param>
		protected virtual void AddScheduledJob(JobSchedulingBundle job)
		{
			scheduledJobs[job.FullName] = job;
		}

		/// <summary>
		/// Adds a calendar.
		/// </summary>
		/// <param name="sched">The sched.</param>
		/// <param name="calendarBundle">calendar bundle.</param>
		/// <throws>  SchedulerException if the Calendar cannot be added to the Scheduler, or </throws>
		public virtual void AddCalendar(IScheduler sched, CalendarBundle calendarBundle)
		{
			sched.AddCalendar(calendarBundle.CalendarName, calendarBundle.Calendar, calendarBundle.Replace, true);
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
		/// Resets the the number of detected validation exceptions.
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


	}
}