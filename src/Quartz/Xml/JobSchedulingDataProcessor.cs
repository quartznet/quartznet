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
using System.Net;
using System.Reflection;
using System.Threading;
using System.Xml;

using Common.Logging;

namespace Quartz.Xml
{
	/// <summary> 
	/// Parses an XML file that declares Jobs and their schedules (Triggers).
	/// 
	/// <p>
	/// The xml document must conform to the format defined in
	/// "job_scheduling_data_1_5.xsd"
	/// </p>
	/// 
	/// <p>
	/// After creating an instance of this class, you should call one of the <code>ProcessFile()</code>
	/// functions, after which you may call the <code>getScheduledJobs()</code>
	/// function to get a handle to the defined Jobs and Triggers, which can then be
	/// scheduled with the <code>Scheduler</code>. Alternatively, you could call
	/// the <code>ProcessFileAndScheduleJobs()</code> function to do all of this
	/// in one step.
	/// </p>
	/// 
	/// <p>
	/// The same instance can be used again and again, with the list of defined Jobs
	/// being cleared each time you call a <code>ProcessFile</code> method,
	/// however a single instance is not thread-safe.
	/// </p>
	/// </summary>
	/// <author><a href="mailto:bonhamcm@thirdeyeconsulting.com">Chris Bonham</a></author>
	/// <author>James House</author>
	/// <author>Marko Lahma (.NET)</author>
	public class JobSchedulingDataProcessor
	{
		private static readonly ILog Log = LogManager.GetLogger(typeof(JobSchedulingDataProcessor));

		public const string QUARTZ_SYSTEM_ID_DIR_PROP = "quartz.system.id.dir";
		public const string QUARTZ_XML_FILE_NAME = "quartz_jobs.xml";

		protected internal const string TAG_QUARTZ = "quartz";
		protected internal const string TAG_OVERWRITE_EXISTING_JOBS = "overwrite-existing-jobs";
		protected internal const string TAG_JOB_LISTENER = "job-listener";
		protected internal const string TAG_CALENDAR = "calendar";
		protected internal const string TAG_CLASS_NAME = "class-name";
		protected internal const string TAG_DESCRIPTION = "description";
		protected internal const string TAG_BASE_CALENDAR = "base-calendar";
		protected internal const string TAG_MISFIRE_INSTRUCTION = "misfire-instruction";
		protected internal const string TAG_CALENDAR_NAME = "calendar-name";
		protected internal const string TAG_JOB = "job";
		protected internal const string TAG_JOB_DETAIL = "job-detail";
		protected internal const string TAG_NAME = "name";
		protected internal const string TAG_GROUP = "group";
		protected internal const string TAG_JOB_CLASS = "job-class";
		protected internal const string TAG_JOB_LISTENER_REF = "job-listener-ref";
		protected internal const string TAG_VOLATILITY = "volatility";
		protected internal const string TAG_DURABILITY = "durability";
		protected internal const string TAG_RECOVER = "recover";
		protected internal const string TAG_JOB_DATA_MAP = "job-data-map";
		protected internal const string TAG_ENTRY = "entry";
		protected internal const string TAG_KEY = "key";
		protected internal const string TAG_ALLOWS_TRANSIENT_DATA = "allows-transient-data";
		protected internal const string TAG_VALUE = "value";
		protected internal const string TAG_TRIGGER = "trigger";
		protected internal const string TAG_SIMPLE = "simple";
		protected internal const string TAG_CRON = "cron";
		protected internal const string TAG_JOB_NAME = "job-name";
		protected internal const string TAG_JOB_GROUP = "job-group";
		protected internal const string TAG_START_TIME = "start-time";
		protected internal const string TAG_END_TIME = "end-time";
		protected internal const string TAG_REPEAT_COUNT = "repeat-count";
		protected internal const string TAG_REPEAT_INTERVAL = "repeat-interval";
		protected internal const string TAG_CRON_EXPRESSION = "cron-expression";
		protected internal const string TAG_TIME_ZONE = "time-zone";

		/// <summary> 
		/// XML Schema dateTime datatype format.
		/// <p>
		/// See <a href="http://www.w3.org/TR/2001/REC-xmlschema-2-20010502/#dateTime">
		/// http://www.w3.org/TR/2001/REC-xmlschema-2-20010502/#dateTime</a>
		/// </p>
		/// </summary>
		protected internal const string XSD_DATE_FORMAT = "yyyy-MM-dd'T'hh:mm:ss";

		protected internal IDictionary scheduledJobs = new Hashtable();
		protected internal IList jobsToSchedule = new ArrayList();
		protected internal IList calsToSchedule = new ArrayList();
		protected internal IList listenersToSchedule = new ArrayList();

		protected internal ArrayList validationExceptions = new ArrayList();

		private bool overWriteExistingJobs = true;

		//private LocalDataStoreSlot schedLocal = Thread.AllocateDataSlot();
		
		/// <summary> 
		/// Gets or sets whether to overwrite existing jobs.
		/// </summary>
		public virtual bool OverWriteExistingJobs
		{
			get { return overWriteExistingJobs; }
			set { overWriteExistingJobs = value; }
		}

		/// <summary> 
		/// Returns a <code>Map</code> of scheduled jobs.
		/// <p>
		/// The key is the job name and the value is a <code>JobSchedulingBundle</code>
		/// containing the <code>JobDetail</code> and <code>Trigger</code>.
		/// </p>
		/// </summary>
		/// <returns> a <code>Map</code> of scheduled jobs.
		/// </returns>
		public virtual IDictionary ScheduledJobs
		{
			get
			{
				return scheduledJobs;
			}
		}


		/// <summary> Constructor for QuartzMetaDataProcessor.</summary>
		public JobSchedulingDataProcessor() : this(true, true)
		{
		}

		/// <summary>
		/// Constructor for QuartzMetaDataProcessor.
		/// </summary>
		/// <param name="validating">whether or not to validate XML.</param>
		/// <param name="validatingSchema">whether or not to validate XML schema.</param>
		public JobSchedulingDataProcessor(bool validating, bool validatingSchema)
		{
		}




		/// <summary>
		/// Initializes the digester for XML Schema validation.
		/// </summary>
		/// <param name="validatingSchema">if set to <c>true</c> [validating schema].</param>
		protected internal virtual void InitSchemaValidation(bool validatingSchema)
		{
			if (validatingSchema)
			{
				string schemaUri = null;
				GetType();
				Uri url = new Uri(Path.GetFullPath(QUARTZ_XSD));
				if (url != null)
				{
					schemaUri = url.ToString();
				}
				else
				{
					schemaUri = QUARTZ_SCHEMA;
				}
				
			}
		}


		/// <summary> 
		/// Process the xml file in the default location (a file named
		/// "quartz_jobs.xml" in the current working directory).
		/// </summary>
		public virtual void ProcessFile()
		{
			ProcessFile(QUARTZ_XML_FILE_NAME);
		}

		/// <summary> Process the xml file named <code>fileName</code>.
		/// 
		/// </summary>
		/// <param name="">fileName
		/// meta data file name.
		/// </param>
		public virtual void ProcessFile(String fileName)
		{
			ProcessFile(fileName, fileName);
		}

		/// <summary>
		/// Process the xmlfile named <code>fileName</code> with the given system
		/// ID.
		/// </summary>
		/// <param name="fileName">Name of the file.</param>
		/// <param name="systemId">The system id.</param>
		public virtual void ProcessFile(String fileName, string systemId)
		{
			ClearValidationExceptions();

			scheduledJobs.Clear();
			jobsToSchedule.Clear();
			calsToSchedule.Clear();

			Log.Info("Parsing XML file: " + fileName + " with systemId: " + systemId + " validating: [unknown]" +
			         " validating schema: [unknown]");
			
			XmlSourceSupport is_Renamed = new XmlSourceSupport(GetInputStream(fileName));
			is_Renamed.Uri = systemId;
			digester.push(this);
			digester.parse(is_Renamed);

			MaybeThrowValidationException();
		}

		/// <summary>
		/// Process the xmlfile named <code>fileName</code> with the given system
		/// ID.
		/// </summary>
		/// <param name="stream">The stream.</param>
		/// <param name="systemId">The system id.</param>
		public virtual void ProcessStream(Stream stream, string systemId)
		{
			ClearValidationExceptions();

			scheduledJobs.Clear();
			jobsToSchedule.Clear();
			calsToSchedule.Clear();

			Log.Info("Parsing XML from stream with systemId: " + systemId + " validating: " + digester.getValidating() +
			         " validating schema: " + digester.getSchema());
			XmlSourceSupport is_Renamed = new XmlSourceSupport(stream);
			is_Renamed.Uri = systemId;
			digester.push(this);
			digester.parse(is_Renamed);

			MaybeThrowValidationException();
		}

		/// <summary> Process the xml file in the default location, and schedule all of the
		/// jobs defined within it.
		/// 
		/// </summary>
		public virtual void ProcessFileAndScheduleJobs(IScheduler sched, bool overWriteExistingJobs)
		{
			ProcessFileAndScheduleJobs(QUARTZ_XML_FILE_NAME, sched, overWriteExistingJobs);
		}

		/// <summary> Process the xml file in the given location, and schedule all of the
		/// jobs defined within it.
		/// 
		/// </summary>
		/// <param name="">fileName
		/// meta data file name.
		/// </param>
		public virtual void ProcessFileAndScheduleJobs(String fileName, IScheduler sched, bool overWriteExistingJobs)
		{
			ProcessFileAndScheduleJobs(fileName, fileName, sched, overWriteExistingJobs);
		}

		/// <summary> Process the xml file in the given location, and schedule all of the
		/// jobs defined within it.
		/// 
		/// </summary>
		/// <param name="">fileName
		/// meta data file name.
		/// </param>
		public virtual void ProcessFileAndScheduleJobs(String fileName, string systemId, IScheduler sched,
		                                               bool overWriteExistingJobs)
		{
			Thread.SetData(schedLocal, sched);
			try
			{
				ProcessFile(fileName, systemId);
				ScheduleJobs(ScheduledJobs, sched, overWriteExistingJobs);
			}
			finally
			{
				Thread.SetData(schedLocal, null);
			}
		}

		/// <summary>
		/// Add the Jobs and Triggers defined in the given map of <code>JobSchedulingBundle</code>
		/// s to the given scheduler.
		/// </summary>
		/// <param name="jobBundles">The job bundles.</param>
		/// <param name="sched">The sched.</param>
		/// <param name="overWriteExistingJobs">if set to <c>true</c> [over write existing jobs].</param>
		public virtual void ScheduleJobs(IDictionary jobBundles, IScheduler sched, bool overWriteExistingJobs)
		{
			Log.Info("Scheduling " + jobsToSchedule.Count + " parsed jobs.");

			foreach (CalendarBundle bndle in calsToSchedule)
			{
				AddCalendar(sched, bndle);
			}

			foreach (JobSchedulingBundle bndle in jobsToSchedule)
			{
				ScheduleJob(bndle, sched, overWriteExistingJobs);
			}

			foreach (IJobListener listener in listenersToSchedule)
			{
				Log.Info("adding listener " + listener.Name + " of type " + listener.GetType().FullName);
				sched.AddJobListener(listener);
			}
			Log.Info(jobBundles.Count + " scheduled jobs.");
		}

		/// <summary>
		/// Returns a <code>JobSchedulingBundle</code> for the job name.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns>
		/// a <code>JobSchedulingBundle</code> for the job name.
		/// </returns>
		public virtual JobSchedulingBundle GetScheduledJob(String name)
		{
			return (JobSchedulingBundle) ScheduledJobs[name];
		}

		/// <summary>
		/// Returns an <code>InputStream</code> from the fileName as a resource.
		/// </summary>
		/// <param name="fileName">Name of the file.</param>
		/// <returns>
		/// an <code>InputStream</code> from the fileName as a resource.
		/// </returns>
		protected internal virtual Stream GetInputStream(String fileName)
		{
			// TODO
			return null;
		}

		/// <summary>
		/// Schedules a given job and trigger (both wrapped by a <code>JobSchedulingBundle</code>).
		/// </summary>
		/// <param name="job">job wrapper.</param>
		/// <exception cref="SchedulerException">
		/// if the Job or Trigger cannot be added to the Scheduler, or
		/// there is an internal Scheduler error.
		/// </exception>
		public virtual void ScheduleJob(JobSchedulingBundle job)
		{
			ScheduleJob(job, (IScheduler) Thread.GetData(schedLocal), OverWriteExistingJobs);
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
		/// Schedules a given job and trigger (both wrapped by a <code>JobSchedulingBundle</code>).
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

					if (trigger.StartTime == DateTime.MinValue)
					{
						trigger.StartTime = DateTime.Now;
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
		protected internal virtual void AddScheduledJob(JobSchedulingBundle job)
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
		/// EntityResolver interface.
		/// <p/>
		/// Allow the application to resolve external entities.
		/// <p/>
		/// Until <code>quartz.dtd</code> has a public ID, it must resolved as a
		/// system ID. Here's the order of resolution (if one fails, continue to the
		/// next).
		/// <ol>
		/// 		<li>Tries to resolve the <code>systemId</code> with <code>ClassLoader.getResourceAsStream(String)</code>.
		/// </li>
		/// 		<li>If the <code>systemId</code> starts with <code>QUARTZ_SYSTEM_ID_PREFIX</code>,
		/// then resolve the part after <code>QUARTZ_SYSTEM_ID_PREFIX</code> with
		/// <code>ClassLoader.getResourceAsStream(String)</code>.</li>
		/// 		<li>Else try to resolve <code>systemId</code> as a URL.
		/// <li>If <code>systemId</code> has a colon in it, create a new <code>URL</code>
		/// 			</li>
		/// 			<li>Else resolve <code>systemId</code> as a <code>File</code> and
		/// then call <code>File.toURL()</code>.</li>
		/// 		</li>
		/// 	</ol>
		/// 	<p/>
		/// If the <code>publicId</code> does exist, resolve it as a URL.  If the
		/// <code>publicId</code> is the Quartz public ID, then resolve it locally.
		/// </summary>
		/// <param name="publicId">The public id.</param>
		/// <param name="systemId">The system id.</param>
		/// <returns>
		/// An InputSource object describing the new input source, or null
		/// to request that the parser open a regular URI connection to the
		/// system identifier.
		/// </returns>
		/// <exception cref=""> SAXException
		/// Any SAX exception, possibly wrapping another exception.
		/// </exception>
		/// <exception cref=""> IOException
		/// A Java-specific IO exception, possibly the result of
		/// creating a new InputStream or Reader for the InputSource.
		/// </exception>
		public override XmlSourceSupport ResolveEntity(String publicId, string systemId)
		{
			XmlSourceSupport inputSource = null;

			Stream is_Renamed = null;

			Uri url = null;

			try
			{
				if (publicId == null)
				{
					if (systemId != null)
					{
						// resolve Quartz Schema locally
						if (QUARTZ_SCHEMA.Equals(systemId))
						{
							//UPGRADE_ISSUE: Method 'java.lang.Class.getResourceAsStream' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javalangClassgetResourceAsStream_javalangString_3"'
							is_Renamed = GetType().getResourceAsStream(QUARTZ_DTD);
						}
						else
						{
							is_Renamed = GetInputStream(systemId);

							if (is_Renamed == null)
							{
								int start = systemId.IndexOf(QUARTZ_SYSTEM_ID_PREFIX);

								if (start > - 1)
								{
									String fileName = systemId.Substring(QUARTZ_SYSTEM_ID_PREFIX.Length);
									is_Renamed = GetInputStream(fileName);
								}
								else
								{
									if (systemId.IndexOf((Char) ':') == - 1)
									{
										FileInfo file = new FileInfo(systemId);
										url = SupportClass.FileSupport.ToUri(file);
									}
									else
									{
										//UPGRADE_TODO: Class 'java.net.URL' was converted to a 'System.Uri' which does not throw an exception if a URL specifies an unknown protocol. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1132_3"'
										url = new Uri(systemId);
									}

									is_Renamed = WebRequest.Create(url).GetResponse().GetResponseStream();
								}
							}
						}
					}
				}
				else
				{
					// resolve Quartz DTD locally
					if (QUARTZ_PUBLIC_ID.Equals(publicId))
					{
						//UPGRADE_ISSUE: Method 'java.lang.Class.getResourceAsStream' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javalangClassgetResourceAsStream_javalangString_3"'
						is_Renamed = GetType().getResourceAsStream(QUARTZ_DTD);
					}
					else
					{
						//UPGRADE_TODO: Class 'java.net.URL' was converted to a 'System.Uri' which does not throw an exception if a URL specifies an unknown protocol. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1132_3"'
						url = new Uri(systemId);
						is_Renamed = WebRequest.Create(url).GetResponse().GetResponseStream();
					}
				}
			}
			catch (Exception e)
			{
				if (e is SchedulerException)
				{
					((SchedulerException) e).printStackTrace();
				}
				else
				{
					SupportClass.WriteStackTrace(e, Console.Error);
				}
			}
			finally
			{
				if (is_Renamed != null)
				{
					inputSource = new XmlSourceSupport(is_Renamed);
					//UPGRADE_ISSUE: Method 'org.xml.sax.InputSource.setPublicId' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_orgxmlsaxInputSourcesetPublicId_javalangString_3"'
					inputSource.setPublicId(publicId);
					inputSource.Uri = systemId;
				}
			}

			return inputSource;
		}

		/// <summary> ErrorHandler interface.
		/// 
		/// Receive notification of a warning.
		/// 
		/// </summary>
		/// <param name="">exception
		/// The error information encapsulated in a SAX parse exception.
		/// </param>
		/// <exception cref=""> SAXException
		/// Any SAX exception, possibly wrapping another exception.
		/// </exception>
		//UPGRADE_TODO: Class 'org.xml.sax.SAXParseException' was converted to 'System.xml.XmlException' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_3"'
		public override void Warning(XmlException e)
		{
			AddValidationException(e);
		}

		/// <summary> ErrorHandler interface.
		/// 
		/// Receive notification of a recoverable error.
		/// 
		/// </summary>
		/// <param name="">exception
		/// The error information encapsulated in a SAX parse exception.
		/// </param>
		/// <exception cref=""> SAXException
		/// Any SAX exception, possibly wrapping another exception.
		/// </exception>
		//UPGRADE_TODO: Class 'org.xml.sax.SAXParseException' was converted to 'System.xml.XmlException' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_3"'
		public override void Error(XmlException e)
		{
			AddValidationException(e);
		}

		/// <summary> ErrorHandler interface.
		/// 
		/// Receive notification of a non-recoverable error.
		/// 
		/// </summary>
		/// <param name="">exception
		/// The error information encapsulated in a SAX parse exception.
		/// </param>
		/// <exception cref=""> SAXException
		/// Any SAX exception, possibly wrapping another exception.
		/// </exception>
		//UPGRADE_TODO: Class 'org.xml.sax.SAXParseException' was converted to 'System.xml.XmlException' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_3"'
		public override void FatalError(XmlException e)
		{
			AddValidationException(e);
		}

		/// <summary> Adds a detected validation exception.
		/// 
		/// </summary>
		/// <param name="">SAXException
		/// SAX exception.
		/// </param>
		//UPGRADE_TODO: Class 'org.xml.sax.SAXException' was converted to 'System.Xml.XmlException' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_3"'
		protected internal virtual void AddValidationException(XmlException e)
		{
			validationExceptions.Add(e);
		}

		/// <summary> Resets the the number of detected validation exceptions.</summary>
		protected internal virtual void ClearValidationExceptions()
		{
			validationExceptions.Clear();
		}

		/// <summary> Throws a ValidationException if the number of validationExceptions
		/// detected is greater than zero.
		/// 
		/// </summary>
		/// <exception cref=""> ValidationException
		/// DTD validation exception.
		/// </exception>
		protected internal virtual void MaybeThrowValidationException()
		{
			if (validationExceptions.Count > 0)
			{
				throw new ValidationException(validationExceptions);
			}
		}


	}
}