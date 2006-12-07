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
using System.Threading;
using log4net;

namespace Quartz.Xml
{
	/// <summary> 
	/// Parses an XML file that declares Jobs and their schedules (Triggers).
	/// 
	/// The xml document must conform to the format defined in
	/// "job_scheduling_data_1_2.dtd" or "job_scheduling_data_1_2.xsd"
	/// 
	/// After creating an instance of this class, you should call one of the <code>processFile()</code>
	/// functions, after which you may call the <code>getScheduledJobs()</code>
	/// function to get a handle to the defined Jobs and Triggers, which can then be
	/// scheduled with the <code>Scheduler</code>. Alternatively, you could call
	/// the <code>processFileAndScheduleJobs()</code> function to do all of this
	/// in one step.
	/// 
	/// The same instance can be used again and again, with the list of defined Jobs
	/// being cleared each time you call a <code>processFile</code> method,
	/// however a single instance is not thread-safe.
	/// 
	/// </summary>
	/// <author><a href="mailto:bonhamcm@thirdeyeconsulting.com">Chris Bonham</a></author>
	/// <author>James House</author>
	/// <author>Marko Lahma (.NET)</author>
	public class JobSchedulingDataProcessor
	{
		private static readonly ILog Log = LogManager.GetLogger(typeof(JobSchedulingDataProcessor));


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

		/*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Constants.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/


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
		/// </summary>
		protected internal const string XSD_DATE_FORMAT = "yyyy-MM-dd'T'hh:mm:ss";


		/*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Data members.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

		protected internal IDictionary scheduledJobs = new Hashtable();

		//UPGRADE_TODO: Class 'java.util.LinkedList' was converted to 'System.Collections.ArrayList' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilLinkedList_3"'
		protected internal IList jobsToSchedule = new ArrayList();
		//UPGRADE_TODO: Class 'java.util.LinkedList' was converted to 'System.Collections.ArrayList' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilLinkedList_3"'
		protected internal IList calsToSchedule = new ArrayList();
		//UPGRADE_TODO: Class 'java.util.LinkedList' was converted to 'System.Collections.ArrayList' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilLinkedList_3"'
		protected internal IList listenersToSchedule = new ArrayList();

		protected internal ArrayList validationExceptions = new ArrayList();

		private bool overWriteExistingJobs = true;

		//private LocalDataStoreSlot schedLocal = Thread.AllocateDataSlot();

		/*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Constructors.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

		/// <summary> Constructor for QuartzMetaDataProcessor.</summary>
		public JobSchedulingDataProcessor() : this(true, true, true)
		{
		}

		/// <summary> Constructor for QuartzMetaDataProcessor.
		/// 
		/// </summary>
		/// <param name="useContextClassLoader">whether or not to use the context class loader.
		/// </param>
		/// <param name="validating">       whether or not to validate XML.
		/// </param>
		/// <param name="validatingSchema"> whether or not to validate XML schema.
		/// </param>
		public JobSchedulingDataProcessor(bool useContextClassLoader, bool validating, bool validatingSchema)
		{
		}




		/// <summary> Initializes the digester for XML Schema validation.
		/// 
		/// </summary>
		/// <param name="validating">   whether or not to validate XML.
		/// </param>
		protected internal virtual void initSchemaValidation(bool validatingSchema)
		{
			if (validatingSchema)
			{
				string schemaUri = null;
				GetType();
				//UPGRADE_TODO: Method 'java.lang.Class.getResource' was converted to 'System.Uri' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javalangClassgetResource_javalangString_3"'
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

		/*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Interface.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

		/// <summary> Process the xml file in the default location (a file named
		/// "quartz_jobs.xml" in the current working directory).
		/// 
		/// </summary>
		public virtual void processFile()
		{
			processFile(QUARTZ_XML_FILE_NAME);
		}

		/// <summary> Process the xml file named <code>fileName</code>.
		/// 
		/// </summary>
		/// <param name="">fileName
		/// meta data file name.
		/// </param>
		public virtual void processFile(String fileName)
		{
			processFile(fileName, fileName);
		}

		/// <summary> Process the xmlfile named <code>fileName</code> with the given system
		/// ID.
		/// 
		/// </summary>
		/// <param name="">fileName
		/// meta data file name.
		/// </param>
		/// <param name="">systemId
		/// system ID.
		/// </param>
		public virtual void processFile(String fileName, string systemId)
		{
			clearValidationExceptions();

			scheduledJobs.Clear();
			jobsToSchedule.Clear();
			calsToSchedule.Clear();

			Log.Info("Parsing XML file: " + fileName + " with systemId: " + systemId + " validating: " + digester.getValidating() +
			         " validating schema: " + digester.getSchema());
			XmlSourceSupport is_Renamed = new XmlSourceSupport(getInputStream(fileName));
			is_Renamed.Uri = systemId;
			digester.push(this);
			digester.parse(is_Renamed);

			maybeThrowValidationException();
		}

		/// <summary> Process the xmlfile named <code>fileName</code> with the given system
		/// ID.
		/// 
		/// </summary>
		/// <param name="">stream
		/// an input stream containing the xml content.
		/// </param>
		/// <param name="">systemId
		/// system ID.
		/// </param>
		public virtual void processStream(Stream stream, string systemId)
		{
			clearValidationExceptions();

			scheduledJobs.Clear();
			jobsToSchedule.Clear();
			calsToSchedule.Clear();

			Log.Info("Parsing XML from stream with systemId: " + systemId + " validating: " + digester.getValidating() +
			         " validating schema: " + digester.getSchema());
			XmlSourceSupport is_Renamed = new XmlSourceSupport(stream);
			is_Renamed.Uri = systemId;
			digester.push(this);
			digester.parse(is_Renamed);

			maybeThrowValidationException();
		}

		/// <summary> Process the xml file in the default location, and schedule all of the
		/// jobs defined within it.
		/// 
		/// </summary>
		public virtual void processFileAndScheduleJobs(IScheduler sched, bool overWriteExistingJobs)
		{
			processFileAndScheduleJobs(QUARTZ_XML_FILE_NAME, sched, overWriteExistingJobs);
		}

		/// <summary> Process the xml file in the given location, and schedule all of the
		/// jobs defined within it.
		/// 
		/// </summary>
		/// <param name="">fileName
		/// meta data file name.
		/// </param>
		public virtual void processFileAndScheduleJobs(String fileName, IScheduler sched, bool overWriteExistingJobs)
		{
			processFileAndScheduleJobs(fileName, fileName, sched, overWriteExistingJobs);
		}

		/// <summary> Process the xml file in the given location, and schedule all of the
		/// jobs defined within it.
		/// 
		/// </summary>
		/// <param name="">fileName
		/// meta data file name.
		/// </param>
		public virtual void processFileAndScheduleJobs(String fileName, string systemId, IScheduler sched,
		                                               bool overWriteExistingJobs)
		{
			Thread.SetData(schedLocal, sched);
			try
			{
				processFile(fileName, systemId);
				scheduleJobs(ScheduledJobs, sched, overWriteExistingJobs);
			}
			finally
			{
				Thread.SetData(schedLocal, null);
			}
		}

		/// <summary> Add the Jobs and Triggers defined in the given map of <code>JobSchedulingBundle</code>
		/// s to the given scheduler.
		/// 
		/// </summary>
		/// <param name="">jobBundles
		/// </param>
		/// <param name="">sched
		/// </param>
		/// <param name="">overWriteExistingJobs
		/// </param>
		/// <throws>  Exception </throws>
		public virtual void scheduleJobs(IDictionary jobBundles, IScheduler sched, bool overWriteExistingJobs)
		{
			Log.Info("Scheduling " + jobsToSchedule.Count + " parsed jobs.");

			IEnumerator itr = calsToSchedule.GetEnumerator();
			//UPGRADE_TODO: Method 'java.util.Iterator.hasNext' was converted to 'System.Collections.IEnumerator.MoveNext' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratorhasNext_3"'
			while (itr.MoveNext())
			{
				//UPGRADE_TODO: Method 'java.util.Iterator.next' was converted to 'System.Collections.IEnumerator.Current' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratornext_3"'
				CalendarBundle bndle = (CalendarBundle) itr.Current;
				addCalendar(sched, bndle);
			}

			itr = jobsToSchedule.GetEnumerator();
			//UPGRADE_TODO: Method 'java.util.Iterator.hasNext' was converted to 'System.Collections.IEnumerator.MoveNext' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratorhasNext_3"'
			while (itr.MoveNext())
			{
				//UPGRADE_TODO: Method 'java.util.Iterator.next' was converted to 'System.Collections.IEnumerator.Current' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratornext_3"'
				JobSchedulingBundle bndle = (JobSchedulingBundle) itr.Current;
				scheduleJob(bndle, sched, overWriteExistingJobs);
			}

			itr = listenersToSchedule.GetEnumerator();
			//UPGRADE_TODO: Method 'java.util.Iterator.hasNext' was converted to 'System.Collections.IEnumerator.MoveNext' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratorhasNext_3"'
			while (itr.MoveNext())
			{
				//UPGRADE_TODO: Method 'java.util.Iterator.next' was converted to 'System.Collections.IEnumerator.Current' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratornext_3"'
				IJobListener listener = (IJobListener) itr.Current;
				Log.Info("adding listener " + listener.Name + " of class " + listener.GetType().FullName);
				sched.AddJobListener(listener);
			}
			Log.Info(jobBundles.Count + " scheduled jobs.");
		}

		/// <summary> Returns a <code>JobSchedulingBundle</code> for the job name.
		/// 
		/// </summary>
		/// <param name="">name
		/// job name.
		/// </param>
		/// <returns> a <code>JobSchedulingBundle</code> for the job name.
		/// </returns>
		public virtual JobSchedulingBundle getScheduledJob(String name)
		{
			return (JobSchedulingBundle) ScheduledJobs[name];
		}

		/// <summary> Returns an <code>InputStream</code> from the fileName as a resource.
		/// 
		/// </summary>
		/// <param name="">fileName
		/// file name.
		/// </param>
		/// <returns> an <code>InputStream</code> from the fileName as a resource.
		/// </returns>
		protected internal virtual Stream getInputStream(String fileName)
		{
			//UPGRADE_ISSUE: Class 'java.lang.ClassLoader' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javalangClassLoader_3"'
			//UPGRADE_ISSUE: Method 'java.lang.Thread.getContextClassLoader' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javalangThreadgetContextClassLoader_3"'
			ClassLoader cl = SupportClass.QuartzThread.Current().getContextClassLoader();

			//UPGRADE_ISSUE: Method 'java.lang.ClassLoader.getResourceAsStream' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javalangClassLoader_3"'
			Stream is_Renamed = cl.getResourceAsStream(fileName);

			return is_Renamed;
		}

		/// <summary> 
		/// Schedules a given job and trigger (both wrapped by a <code>JobSchedulingBundle</code>).
		/// </summary>
		/// <param name="job">
		/// job wrapper.
		/// </param>
		/// <exception cref="SchedulerException"> 
		/// if the Job or Trigger cannot be added to the Scheduler, or
		/// there is an internal Scheduler error.
		/// </exception>
		public virtual void scheduleJob(JobSchedulingBundle job)
		{
			scheduleJob(job, (IScheduler) Thread.GetData(schedLocal), OverWriteExistingJobs);
		}


		public virtual void addJobToSchedule(JobSchedulingBundle job)
		{
			jobsToSchedule.Add(job);
		}

		public virtual void addCalendarToSchedule(CalendarBundle cal)
		{
			calsToSchedule.Add(cal);
		}

		public virtual void addListenerToSchedule(IJobListener listener)
		{
			listenersToSchedule.Add(listener);
		}

		/// <summary> Schedules a given job and trigger (both wrapped by a <code>JobSchedulingBundle</code>).
		/// 
		/// </summary>
		/// <param name="">job
		/// job wrapper.
		/// </param>
		/// <param name="">sched
		/// job scheduler.
		/// </param>
		/// <param name="">localOverWriteExistingJobs
		/// locally overwrite existing jobs.
		/// </param>
		/// <exception cref=""> SchedulerException
		/// if the Job or Trigger cannot be added to the Scheduler, or
		/// there is an internal Scheduler error.
		/// </exception>
		public virtual void scheduleJob(JobSchedulingBundle job, IScheduler sched, bool localOverWriteExistingJobs)
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

				//UPGRADE_TODO: Method 'java.util.Iterator.hasNext' was converted to 'System.Collections.IEnumerator.MoveNext' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratorhasNext_3"'
				for (IEnumerator iter = job.Triggers.GetEnumerator(); iter.MoveNext();)
				{
					//UPGRADE_TODO: Method 'java.util.Iterator.next' was converted to 'System.Collections.IEnumerator.Current' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratornext_3"'
					Trigger trigger = (Trigger) iter.Current;

					Trigger dupeT = sched.GetTrigger(trigger.Name, trigger.Group);

					trigger.JobName = detail.Name;
					trigger.JobGroup = detail.Group;

					//UPGRADE_TODO: The 'DateTime' structure does not have an equivalent to NULL. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1291_3"'
					if (trigger.StartTime == null)
					{
						trigger.StartTime = DateTime.Now;
					}

					if (dupeT != null)
					{
						Log.debug("Rescheduling job: " + detail.FullName + " with updated trigger: " + trigger.FullName);
						if (!dupeT.JobGroup.Equals(trigger.JobGroup) || !dupeT.JobName.Equals(trigger.JobName))
						{
							Log.warn("Possibly duplicately named triggers in jobs xml file!");
						}
						sched.RescheduleJob(trigger.Name, trigger.Group, trigger);
					}
					else
					{
						Log.debug("Scheduling job: " + detail.FullName + " with trigger: " + trigger.FullName);
						sched.ScheduleJob(trigger);
					}
				}

				addScheduledJob(job);
			}
		}

		/// <summary> Adds a scheduled job.
		/// 
		/// </summary>
		/// <param name="">job
		/// job wrapper.
		/// </param>
		protected internal virtual void addScheduledJob(JobSchedulingBundle job)
		{
			scheduledJobs[job.FullName] = job;
		}

		/// <summary> Adds a calendar.
		/// 
		/// </summary>
		/// <param name="calendarBundle">calendar bundle.
		/// </param>
		/// <throws>  SchedulerException if the Calendar cannot be added to the Scheduler, or </throws>
		/// <summary>              there is an internal Scheduler error.
		/// </summary>
		public virtual void addCalendar(IScheduler sched, CalendarBundle calendarBundle)
		{
			sched.AddCalendar(calendarBundle.CalendarName, calendarBundle.Calendar, calendarBundle.Replace, true);
		}

		/// <summary> EntityResolver interface.
		/// <p/>
		/// Allow the application to resolve external entities.
		/// <p/>
		/// Until <code>quartz.dtd</code> has a public ID, it must resolved as a
		/// system ID. Here's the order of resolution (if one fails, continue to the
		/// next).
		/// <ol>
		/// <li>Tries to resolve the <code>systemId</code> with <code>ClassLoader.getResourceAsStream(String)</code>.
		/// </li>
		/// <li>If the <code>systemId</code> starts with <code>QUARTZ_SYSTEM_ID_PREFIX</code>,
		/// then resolve the part after <code>QUARTZ_SYSTEM_ID_PREFIX</code> with
		/// <code>ClassLoader.getResourceAsStream(String)</code>.</li>
		/// <li>Else try to resolve <code>systemId</code> as a URL.
		/// <li>If <code>systemId</code> has a colon in it, create a new <code>URL</code>
		/// </li>
		/// <li>Else resolve <code>systemId</code> as a <code>File</code> and
		/// then call <code>File.toURL()</code>.</li>
		/// </li>
		/// </ol>
		/// <p/>
		/// If the <code>publicId</code> does exist, resolve it as a URL.  If the
		/// <code>publicId</code> is the Quartz public ID, then resolve it locally.
		/// 
		/// </summary>
		/// <param name="">publicId
		/// The public identifier of the external entity being referenced,
		/// or null if none was supplied.
		/// </param>
		/// <param name="">systemId
		/// The system identifier of the external entity being referenced.
		/// </param>
		/// <returns> An InputSource object describing the new input source, or null
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
		public override XmlSourceSupport resolveEntity(String publicId, string systemId)
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
							is_Renamed = getInputStream(systemId);

							if (is_Renamed == null)
							{
								int start = systemId.IndexOf(QUARTZ_SYSTEM_ID_PREFIX);

								if (start > - 1)
								{
									String fileName = systemId.Substring(QUARTZ_SYSTEM_ID_PREFIX.Length);
									is_Renamed = getInputStream(fileName);
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
		public override void warning(XmlException e)
		{
			addValidationException(e);
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
		public override void error(XmlException e)
		{
			addValidationException(e);
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
		public override void fatalError(XmlException e)
		{
			addValidationException(e);
		}

		/// <summary> Adds a detected validation exception.
		/// 
		/// </summary>
		/// <param name="">SAXException
		/// SAX exception.
		/// </param>
		//UPGRADE_TODO: Class 'org.xml.sax.SAXException' was converted to 'System.Xml.XmlException' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_3"'
		protected internal virtual void addValidationException(XmlException e)
		{
			validationExceptions.Add(e);
		}

		/// <summary> Resets the the number of detected validation exceptions.</summary>
		protected internal virtual void clearValidationExceptions()
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
		protected internal virtual void maybeThrowValidationException()
		{
			if (validationExceptions.Count > 0)
			{
				throw new ValidationException(validationExceptions);
			}
		}

		/// <summary> 
		/// RuleSet for common Calendar tags. 
		/// </summary>
		/// <author>  <a href="mailto:bonhamcm@thirdeyeconsulting.com">Chris Bonham</a>
		/// </author>
		public class CalendarRuleSet : RuleSetBase
		{
			private void InitBlock(JobSchedulingDataProcessor enclosingInstance)
			{
				this.enclosingInstance = enclosingInstance;
			}

			private JobSchedulingDataProcessor enclosingInstance;

			public JobSchedulingDataProcessor Enclosing_Instance
			{
				get { return enclosingInstance; }
			}

			protected internal string prefix;
			protected internal string setNextMethodName;

			public CalendarRuleSet(JobSchedulingDataProcessor enclosingInstance, string prefix, string setNextMethodName)
				: base()
			{
				InitBlock(enclosingInstance);
				this.prefix = prefix;
				this.setNextMethodName = setNextMethodName;
			}

			public virtual void addRuleInstances(Digester digester)
			{
				digester.addObjectCreate(prefix, typeof (CalendarBundle));
				digester.addSetProperties(prefix, TAG_CLASS_NAME, "className");
				digester.addBeanPropertySetter(prefix + "/" + TAG_NAME, "calendarName");
				digester.addBeanPropertySetter(prefix + "/" + TAG_DESCRIPTION, "description");
				digester.addSetNext(prefix, setNextMethodName);
			}
		}

		//UPGRADE_NOTE: Field 'EnclosingInstance' was added to class 'TriggerRuleSet' to access its enclosing instance. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1019_3"'
		/// <summary> RuleSet for common Trigger tags. 
		/// 
		/// </summary>
		/// <author>  <a href="mailto:bonhamcm@thirdeyeconsulting.com">Chris Bonham</a>
		/// </author>
		public class TriggerRuleSet //: RuleSetBase
		{
			private void InitBlock(JobSchedulingDataProcessor enclosingInstance)
			{
				this.enclosingInstance = enclosingInstance;
			}

			private JobSchedulingDataProcessor enclosingInstance;

			public JobSchedulingDataProcessor Enclosing_Instance
			{
				get { return enclosingInstance; }
			}

			protected internal string prefix;
			protected internal Type clazz;

			public TriggerRuleSet(JobSchedulingDataProcessor enclosingInstance, string prefix, Type clazz) : base()
			{
				InitBlock(enclosingInstance);
				this.prefix = prefix;
				if (!typeof (Trigger).IsAssignableFrom(clazz))
				{
					throw new ArgumentException("Class must be an instance of Trigger");
				}
				this.clazz = clazz;
			}

			public virtual void addRuleInstances(Digester digester)
			{
				digester.addObjectCreate(prefix, clazz);
				digester.addBeanPropertySetter(prefix + "/" + TAG_NAME, "name");
				digester.addBeanPropertySetter(prefix + "/" + TAG_GROUP, "group");
				digester.addBeanPropertySetter(prefix + "/" + TAG_DESCRIPTION, "description");
				digester.addBeanPropertySetter(prefix + "/" + TAG_VOLATILITY, "volatility");
				digester.addRule(prefix + "/" + TAG_MISFIRE_INSTRUCTION,
				                 new MisfireInstructionRule(enclosingInstance, "misfireInstruction"));
				digester.addBeanPropertySetter(prefix + "/" + TAG_CALENDAR_NAME, "calendarName");
				digester.addBeanPropertySetter(prefix + "/" + TAG_JOB_NAME, "jobName");
				digester.addBeanPropertySetter(prefix + "/" + TAG_JOB_GROUP, "jobGroup");
				Converter converter = new DateConverter(enclosingInstance, new string[] {XSD_DATE_FORMAT, DTD_DATE_FORMAT});
				digester.addRule(prefix + "/" + TAG_START_TIME, new SimpleConverterRule("startTime", converter, typeof (DateTime)));
				digester.addRule(prefix + "/" + TAG_END_TIME, new SimpleConverterRule("endTime", converter, typeof (DateTime)));
			}
		}

		//UPGRADE_NOTE: Field 'EnclosingInstance' was added to class 'SimpleConverterRule' to access its enclosing instance. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1019_3"'
		/// <summary> This rule is needed to fix <a href="http://jira.the original author or authors..com/browse/QUARTZ-153">QUARTZ-153</a>.
		/// <p>
		/// Since the Jakarta Commons BeanUtils 1.6.x <code>ConvertUtils</code> class uses static utility 
		/// methods, the <code>DateConverter</code> and <code>TimeZoneConverter</code> were
		/// overriding any previously registered converters for <code>java.util.Date</code> and
		/// <code>java.util.TimeZone</code>.
		/// <p>
		/// Jakarta Commons BeanUtils 1.7.x fixes this issue by internally using per-context-classloader
		/// pseudo-singletons (see <a href="http://jakarta.apache.org/commons/beanutils/commons-beanutils-1.7.0/RELEASE-NOTES.txt">
		/// http://jakarta.apache.org/commons/beanutils/commons-beanutils-1.7.0/RELEASE-NOTES.txt</a>).
		/// This ensures web applications in the same JVM are using independent converters
		/// based on their classloaders.  However, the environment for QUARTZ-153 started Quartz
		/// using the <code>QuartzInitializationServlet</code> which started <code>JobInitializationPlugin</code>.  
		/// In this case, the web classloader instances would be the same.
		/// <p>
		/// To make sure the converters aren't overridden by the <code>JobSchedulingDataProcessor</code>,
		/// it's easier to just override <code>BeanPropertySetterRule.end()</code> to convert the
		/// body text to the specified class using the specified converter.
		/// 
		/// </summary>
		/// <author>  <a href="mailto:bonhamcm@thirdeyeconsulting.com">Chris Bonham</a>
		/// </author>
		public class SimpleConverterRule //: BeanPropertySetterRule
		{

			
			private JobSchedulingDataProcessor enclosingInstance;

			public JobSchedulingDataProcessor Enclosing_Instance
			{
				get { return enclosingInstance; }
			}

			private Converter converter;
			private Type clazz;

			/// <summary> <p>Construct rule that sets the given property from the body text.</p>
			/// 
			/// </summary>
			/// <param name="propertyName">name of property to set
			/// </param>
			/// <param name="converter">   converter to use
			/// </param>
			/// <param name="clazz">       class to convert to
			/// </param>
			public SimpleConverterRule(JobSchedulingDataProcessor enclosingInstance, string propertyName, Converter converter,
			                           Type clazz)
			{
				InitBlock(enclosingInstance);
				this.propertyName = propertyName;
				if (converter == null)
				{
					throw new ArgumentException("Converter must not be null");
				}
				this.converter = converter;
				if (clazz == null)
				{
					throw new ArgumentException("Class must not be null");
				}
				this.clazz = clazz;
			}

			/// <summary> Process the end of this element.
			/// 
			/// </summary>
			/// <param name="namespace">the namespace URI of the matching element, or an 
			/// empty string if the parser is not namespace aware or the element has
			/// no namespace
			/// </param>
			/// <param name="name">the local name if the parser is namespace aware, or just 
			/// the element name otherwise
			/// 
			/// </param>
			/// <exception cref=""> NoSuchMethodException if the bean does not
			/// have a writeable property of the specified name
			/// </exception>
			public virtual void end(String namespace_Renamed, string name)
			{
				String property = propertyName;

				if (property == null)
				{
					// If we don't have a specific property name,
					// use the element name.
					property = name;
				}

				// Get a reference to the top object
				object top = this.digester.peek();

				// log some debugging information
				if (getDigester().getLogger().isDebugEnabled())
				{
					//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Class.getName' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
					getDigester().getLogger().debug("[BeanPropertySetterRule]{" + getDigester().getMatch() + "} Set " +
					                                top.GetType().FullName + " property " + property + " with text " + bodyText);
				}

				// Force an exception if the property does not exist
				// (BeanUtils.setProperty() silently returns in this case)
				if (top is DynaBean)
				{
					DynaProperty desc = ((DynaBean) top).getDynaClass().getDynaProperty(property);
					if (desc == null)
					{
						throw new MethodAccessException("Bean has no property named " + property);
					}
				}
					/* this is a standard JavaBean */
				else
				{
					PropertyDescriptor desc = PropertyUtils.getPropertyDescriptor(top, property);
					if (desc == null)
					{
						throw new MethodAccessException("Bean has no property named " + property);
					}
				}

				// Set the property only using this converter
				object value_Renamed = converter.convert(clazz, bodyText);
				PropertyUtils.setProperty(top, property, value_Renamed);
			}
		}

		/// <summary> 
		/// This rule translates the trigger misfire instruction constant name into its
		/// corresponding value.
		/// 
		/// </summary>
		/// <TODO>  Consider removing this class and using a </TODO>
		/// <summary> <code>org.apache.commons.digester.Substitutor</code> strategy once
		/// Jakarta Commons Digester 1.6 is final.  
		/// 
		/// </summary>
		/// <author>  <a href="mailto:bonhamcm@thirdeyeconsulting.com">Chris Bonham</a>
		/// </author>
		public class MisfireInstructionRule //: BeanPropertySetterRule
		{
			private void InitBlock(JobSchedulingDataProcessor enclosingInstance)
			{
				this.enclosingInstance = enclosingInstance;
			}

			private JobSchedulingDataProcessor enclosingInstance;

			public JobSchedulingDataProcessor Enclosing_Instance
			{
				get { return enclosingInstance; }
			}

			/// <summary> <p>Construct rule that sets the given property from the body text.</p>
			/// 
			/// </summary>
			/// <param name="propertyName">name of property to set
			/// </param>
			public MisfireInstructionRule(JobSchedulingDataProcessor enclosingInstance, string propertyName)
			{
				InitBlock(enclosingInstance);
				this.propertyName = propertyName;
			}

			/// <summary> Process the body text of this element.
			/// 
			/// </summary>
			/// <param name="namespace">the namespace URI of the matching element, or an 
			/// empty string if the parser is not namespace aware or the element has
			/// no namespace
			/// </param>
			/// <param name="name">the local name if the parser is namespace aware, or just 
			/// the element name otherwise
			/// </param>
			/// <param name="text">The text of the body of this element
			/// </param>
			public virtual void body(String namespace_Renamed, string name, string text)
			{
				base.body(namespace_Renamed, name, text);
				this.bodyText = getConstantValue(bodyText);
			}

			/// <summary> Returns the value for the constant name.
			/// If the constant can't be found or any exceptions occur,
			/// return 0.
			/// 
			/// </summary>
			/// <param name="constantName"> constant name.
			/// </param>
			/// <returns> the value for the constant name.
			/// </returns>
			private string getConstantValue(String constantName)
			{
				String value_Renamed = "0";

				object top = this.digester.peek();
				if (top != null)
				{
					Type clazz = top.GetType();
					try
					{
						//UPGRADE_TODO: The differences in the expected value  of parameters for method 'java.lang.Class.getField'  may cause compilation errors.  'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1092_3"'
						FieldInfo field = clazz.GetField(constantName, BindingFlags.Instance | BindingFlags.Public);
						object fieldValue = field.GetValue(top);
						if (fieldValue != null)
						{
							//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Object.toString' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
							value_Renamed = fieldValue.ToString();
						}
					}
					catch (Exception e)
					{
						// ignore
					}
				}

				return value_Renamed;
			}
		}

	}
}