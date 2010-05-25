#region License
/* 
 * Copyright 2001-2009 Terracotta, Inc. 
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

using System;

#if NET_35
using TimeZone = System.TimeZoneInfo;
#endif
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

using Common.Logging;

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
		private readonly ILog log;
	    private readonly bool validateXml;
	    private readonly bool validateSchema;

		public const string PropertyQuartzSystemIdDir = "quartz.system.id.dir";
		public const string QuartzXmlFileName = "quartz_jobs.xml";
		public const string QuartzSchema = "http://quartznet.sourceforge.net/xml/job_scheduling_data.xsd";
		public const string QuartzXsdResourceName = "Quartz.Quartz.Xml.job_scheduling_data.xsd";
		
		protected const string ThreadLocalKeyScheduler = "quartz_scheduler";
		
		/// <summary> 
		/// XML Schema dateTime datatype format.
		/// <p>
		/// See <a href="http://www.w3.org/TR/2001/REC-xmlschema-2-20010502/#dateTime">
		/// http://www.w3.org/TR/2001/REC-xmlschema-2-20010502/#dateTime</a>
		/// </p>
		/// </summary>
		protected const string XsdDateFormat = "yyyy-MM-dd'T'hh:mm:ss";

	    private IDictionary<string, JobSchedulingBundle> scheduledJobs = new Dictionary<string, JobSchedulingBundle>();
	    private IList<JobSchedulingBundle> jobsToSchedule = new List<JobSchedulingBundle>();
	    private IList<CalendarBundle> calsToSchedule = new List<CalendarBundle>();
	    private IList<IJobListener> listenersToSchedule = new List<IJobListener>();
	    private IList<ITriggerListener> triggerListenersToSchedule = new List<ITriggerListener>();

	    private List<Exception> validationExceptions = new List<Exception>();

		private bool overwriteExistingJobs = true;

		
		/// <summary> 
		/// Gets or sets whether to overwrite existing jobs.
		/// </summary>
		public virtual bool OverwriteExistingJobs
		{
			get { return overwriteExistingJobs; }
			set { overwriteExistingJobs = value; }
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
        /// Returns a <see cref="IDictionary{TKey,TValue}" /> of scheduled jobs.
		/// <p>
		/// The key is the job name and the value is a <see cref="JobSchedulingBundle" />
		/// containing the <see cref="JobDetail" /> and <see cref="Trigger" />.
		/// </p>
		/// </summary>
        /// <returns> a <see cref="IDictionary{TKey,TValue}" /> of scheduled jobs.
		/// </returns>
		public virtual IDictionary<string, JobSchedulingBundle> ScheduledJobs
		{
			get { return scheduledJobs; }
		}


		/// <summary>
		/// Constructor for JobSchedulingDataProcessor.
		/// </summary>
		public JobSchedulingDataProcessor() : this(true, true)
		{
		}

		/// <summary>
		/// Constructor for JobSchedulingDataProcessor.
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
			ProcessFile(QuartzXmlFileName);
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
			Log.Info(string.Format(CultureInfo.InvariantCulture, "Parsing XML file: {0} with systemId: {1} validating: {2} validating schema: {3}", fileName, systemId, validateXml, validateSchema));
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
			Log.Info(string.Format(CultureInfo.InvariantCulture, "Parsing XML from stream with systemId: {0} validating: {1} validating schema: {2}", systemId, validateXml, validateSchema));
            using (StreamReader sr = new StreamReader(stream))
            {
                ProcessInternal(sr.ReadToEnd());
            }
		}

        protected internal virtual void ProcessInternal(string xml)
        {
            ClearValidationExceptions();

            scheduledJobs.Clear();
            jobsToSchedule.Clear();
            calsToSchedule.Clear();

            ValidateXmlIfNeeded(xml);
            
            // deserialize as object model
            XmlSerializer xs = new XmlSerializer(typeof(QuartzXmlConfiguration));
            QuartzXmlConfiguration data = (QuartzXmlConfiguration) xs.Deserialize(new StringReader(xml));

            // process data
            overwriteExistingJobs = data.overwriteexistingjobs;

            // add calendars
            if (data.calendar != null)
            {
                foreach (calendarType ct in data.calendar)
                {
                    CalendarBundle c = CreateCalendarFromXmlObject(ct);
                    AddCalendarToSchedule(c);
                }
            }

            // add job scheduling bundles
            ProcessJobs(data);

            if (data.joblistener != null)
            {
                // go through listeners
                foreach (joblistenerType jt in data.joblistener)
                {
                    Type listenerType = Type.GetType(jt.type);
                    if (listenerType == null)
                    {
                        throw new SchedulerConfigException("Unknown job listener type " + jt.type);
                    }
                    IJobListener listener = ObjectUtils.InstantiateType<IJobListener>(listenerType);
                    // set name of trigger with reflection, this might throw errors
                    NameValueCollection properties = new NameValueCollection();
                    properties.Add("Name", jt.name);

                    try
                    {
                        ObjectUtils.SetObjectProperties(listener, properties);
                    }
                    catch (Exception)
                    {
                        throw new SchedulerConfigException(string.Format("Could not set name for job listener of type '{0}', do you have public set method defined for property 'Name'?", jt.type));
                    }
                    AddListenerToSchedule(listener);
                }
            }

			ProcessTriggerListeners(data);
			
            MaybeThrowValidationException();
        }

	    private void ProcessJobs(QuartzXmlConfiguration data)
	    {
            if (data.job == null)
            {
                // no jobs to process, file is empty
                return;
            }

	        foreach (jobType jt in data.job)
	        {
	            JobSchedulingBundle jsb = new JobSchedulingBundle();
	            jobdetailType j = jt.jobdetail;
	            Type jobType = Type.GetType(j.jobtype);
                if (jobType == null)
                {
                    throw new SchedulerConfigException("Unknown job type " + j.jobtype);
                }

	            JobDetail jd = new JobDetail(j.name, j.group, jobType, j.@volatile, j.durable, j.recover);
	            jd.Description = j.description;

                if (j.joblistenerref != null && j.joblistenerref.Trim().Length > 0)
                {
                    jd.AddJobListener(j.joblistenerref);
                }
                
                jsb.JobDetail = jd;

                // read job data map
                if (j.jobdatamap != null && j.jobdatamap.entry != null)
                {
                    foreach (entryType entry in j.jobdatamap.entry)
                    {
                        jd.JobDataMap.Put(entry.key, entry.value);
                    }
                }

	            triggerType[] tArr = jt.trigger ?? new triggerType[0];
	            foreach (triggerType t in tArr)
	            {
	                Trigger trigger;
	                if (t.Item is cronType)
	                {
	                    cronType c = (cronType) t.Item;

                        DateTime startTime = (c.starttime == DateTime.MinValue ? SystemTime.UtcNow() : c.starttime);
                        DateTime? endTime = (c.endtime == DateTime.MinValue ? null : (DateTime?)c.endtime);

	                    string jobName = c.jobname != null ? c.jobname : j.name;
	                    string jobGroup = c.jobgroup != null ? c.jobgroup : j.group;

                        CronTrigger ct = new CronTrigger(
                            c.name,
                            c.group,
                            jobName,
                            jobGroup,
                            startTime,
                            endTime,
                            c.cronexpression);

	                    if (c.timezone != null && c.timezone.Trim().Length > 0)
	                    {
#if NET_35
                            ct.TimeZone = TimeZoneInfo.FindSystemTimeZoneById(c.timezone);
#else
	                        throw new ArgumentException(
	                            "Specifying time zone for cron trigger is only supported in .NET 3.5 builds");
#endif
	                    }
	                    trigger = ct;
	                }
	                else if (t.Item is simpleType)
	                {
	                    simpleType s = (simpleType) t.Item;
	                    
	                    DateTime startTime = (s.starttime == DateTime.MinValue ? SystemTime.UtcNow() : s.starttime);
                        DateTime? endTime = (s.endtime == DateTime.MinValue ? null : (DateTime?)s.endtime);

                        string jobName = s.jobname != null ? s.jobname : j.name;
                        string jobGroup = s.jobgroup != null ? s.jobgroup : j.group;

                        SimpleTrigger st = new SimpleTrigger(
                            s.name, 
                            s.group, 
                            jobName, 
                            jobGroup,
                            startTime, 
                            endTime, 
                            ParseSimpleTriggerRepeatCount(s.repeatcount), 
                            TimeSpan.FromMilliseconds(Convert.ToInt64(s.repeatinterval, CultureInfo.InvariantCulture)));

	                    trigger = st;
	                }
	                else
	                {
	                    throw new ArgumentException("Unknown trigger type in XML");
	                }
                    
                    trigger.Description = t.Item.description;
                    trigger.CalendarName = t.Item.calendarname;
                    
                    if (t.Item.misfireinstruction != null)
                    {
                        trigger.MisfireInstruction = ReadMisfireInstructionFromString(t.Item.misfireinstruction);
                    }
                    if (t.Item.jobdatamap != null && t.Item.jobdatamap.entry != null)
                    {
                        foreach (entryType entry in t.Item.jobdatamap.entry)
                        {
                            if (trigger.JobDataMap.ContainsKey(entry.key))
                            {
                                Log.Warn("Overriding key '" + entry.key + "' with another value in same trigger job data map");
                            }
                            trigger.JobDataMap[entry.key] = entry.value;
                        }
                    }
					if (t.Item.triggerlistenerref != null && t.Item.triggerlistenerref.Trim().Length > 0)
					{
						trigger.AddTriggerListener(t.Item.triggerlistenerref);
					}
					
	                jsb.Triggers.Add(trigger);
	            }

	            AddJobToSchedule(jsb);
	        }
	    }

		private void ProcessTriggerListeners(QuartzXmlConfiguration data)
		{
			if (data.triggerlistener != null)
			{
				// go through listeners
				foreach (triggerlistenerType lt in data.triggerlistener)
				{
					Type listenerType = Type.GetType(lt.type);
					if (listenerType == null)
					{
						throw new SchedulerConfigException("Unknown trigger listener type " + lt.type);
					}
					ITriggerListener listener = ObjectUtils.InstantiateType<ITriggerListener>(listenerType);
					// set name of trigger with reflection, this might throw errors
					NameValueCollection properties = new NameValueCollection();
					properties.Add("Name", lt.name);

					try
					{
						ObjectUtils.SetObjectProperties(listener, properties);
					}
					catch (Exception)
					{
						throw new SchedulerConfigException(string.Format("Could not set name for job listener of type '{0}', do you have public set method defined for property 'Name'?", lt.type));
					}
					AddTriggerListenerToSchedule(listener);
				}
			}
		}
	    private static int ParseSimpleTriggerRepeatCount(string repeatcount)
	    {
	        int value;
	        if (repeatcount == "RepeatIndefinitely")
	        {
	            value = SimpleTrigger.RepeatIndefinitely;
	        }
            else
	        {
                value = Convert.ToInt32(repeatcount, CultureInfo.InvariantCulture);
	        }

            return value;
	    }

	    private static int ReadMisfireInstructionFromString(string misfireinstruction)
	    {
	       Constants c = new Constants(typeof(MisfireInstruction), typeof(MisfireInstruction.CronTrigger), typeof(MisfireInstruction.SimpleTrigger));
	       return c.AsNumber(misfireinstruction);
	    }

	    private static CalendarBundle CreateCalendarFromXmlObject(calendarType ct)
	    {
            CalendarBundle c = new CalendarBundle(); 
            
            // set type name first as it creates the actual inner instance
	        c.TypeName = ct.type;
            c.Description = ct.description;
            c.CalendarName = ct.name;
            c.Replace = ct.replace;

            if (ct.basecalendar != null)
            {
                c.CalendarBase = CreateCalendarFromXmlObject(ct.basecalendar);
            }
            return c;
	    }

	    private void ValidateXmlIfNeeded(string xml)
	    {
            if (validateXml)
            {
                // stream to validate
                using (StringReader stringReader = new StringReader(xml))
                {
                    XmlTextReader xmlr = new XmlTextReader(stringReader);
                    XmlValidatingReader xmlvread = new XmlValidatingReader(xmlr);

                    // Set the validation event handler
                    xmlvread.ValidationEventHandler += new ValidationEventHandler(XmlValidationCallBack);

                    // Read XML data
                    while (xmlvread.Read()) { }

                    //Close the reader.
                    xmlvread.Close();
                }
            }
	    }

	    private void XmlValidationCallBack(object sender, ValidationEventArgs e)
	    {
	        validationExceptions.Add(e.Exception);
	    }


	    /// <summary> 
		/// Process the xml file in the default location, and schedule all of the
		/// jobs defined within it.
		/// </summary>
        public virtual void ProcessFileAndScheduleJobs(IScheduler sched, bool overwriteExistingJobs)
		{
            ProcessFileAndScheduleJobs(QuartzXmlFileName, sched, overwriteExistingJobs);
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
			LogicalThreadContext.SetData(ThreadLocalKeyScheduler, sched);
			try
			{
				ProcessFile(fileName, systemId);
                ScheduleJobs(ScheduledJobs, sched, overwriteExistingJobs);
			}
			finally
			{
				LogicalThreadContext.FreeNamedDataSlot(ThreadLocalKeyScheduler);
			}
		}

		/// <summary>
		/// Add the Jobs and Triggers defined in the given map of <see cref="JobSchedulingBundle" />
		/// s to the given scheduler.
		/// </summary>
		/// <param name="jobBundles">The job bundles.</param>
		/// <param name="sched">The sched.</param>
		/// <param name="overwriteExistingJobs">if set to <c>true</c> [over write existing jobs].</param>
		public virtual void ScheduleJobs(IDictionary<string, JobSchedulingBundle> jobBundles, IScheduler sched, bool overwriteExistingJobs)
		{
			Log.Info(string.Format(CultureInfo.InvariantCulture, "Scheduling {0} parsed jobs.", jobsToSchedule.Count));

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
				Log.Info(string.Format(CultureInfo.InvariantCulture, "adding listener {0} of type {1}", listener.Name, listener.GetType().FullName));
				sched.AddJobListener(listener);
			}

			foreach (ITriggerListener listener in triggerListenersToSchedule)
			{
				Log.Info(string.Format(CultureInfo.InvariantCulture, "adding listener {0} of type {1}", listener.Name, listener.GetType().FullName));
				sched.AddTriggerListener(listener);
			}
			Log.Info(string.Format(CultureInfo.InvariantCulture, "{0} scheduled jobs.", jobBundles.Count));
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
		    JobSchedulingBundle bundle;
		    ScheduledJobs.TryGetValue(name, out bundle);
		    return bundle;
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
			ScheduleJob(job, LogicalThreadContext.GetData<IScheduler>(ThreadLocalKeyScheduler), OverwriteExistingJobs);
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

		public virtual void AddTriggerListenerToSchedule(ITriggerListener listener)
		{
			triggerListenersToSchedule.Add(listener);
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
					Log.Info(string.Format(CultureInfo.InvariantCulture, "Replacing job: {0}", detail.FullName));
				}
				else
				{
					Log.Info(string.Format(CultureInfo.InvariantCulture, "Adding job: {0}", detail.FullName));
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
						trigger.StartTimeUtc = SystemTime.UtcNow();
					}

					if (dupeT != null)
					{
						Log.Debug(string.Format(CultureInfo.InvariantCulture, "Rescheduling job: {0} with updated trigger: {1}", detail.FullName, trigger.FullName));
						if (!dupeT.JobGroup.Equals(trigger.JobGroup) || !dupeT.JobName.Equals(trigger.JobName))
						{
							Log.Warn("Possibly duplicately named triggers in jobs xml file!");
						}
						sched.RescheduleJob(trigger.Name, trigger.Group, trigger);
					}
					else
					{
						Log.Debug(string.Format(CultureInfo.InvariantCulture, "Scheduling job: {0} with trigger: {1}", detail.FullName, trigger.FullName));
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
            throw new Exception(string.Format(CultureInfo.InvariantCulture, "Unknown field '{0}'", field));
        }
    }
}
