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
using System.IO;
using System.Net;
using System.Web;

using Common.Logging;

using Quartz.Job;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Plugins.Xml
{
	/// <summary> This plugin loads an XML file to add jobs and schedule them with triggers
	/// as the scheduler is initialized, and can optionally periodically scan the
	/// file for changes.
	/// 
	/// </summary>
	/// <author>  James House
	/// </author>
	/// <author>  Pierre Awaragi
	/// </author>
	public class JobInitializationPlugin : ISchedulerPlugin, IFileScanListener
	{
		private static readonly ILog Log = LogManager.GetLogger(typeof (JobInitializationPlugin));

		/// <summary> The file name (and path) to the XML file that should be read.
		/// 
		/// </summary>
		/// <returns>
		/// </returns>
		/// <summary> The file name (and path) to the XML file that should be read.
		/// 
		/// </summary>
		public virtual string FileName
		{
			get { return fileName; }
			set { fileName = value; }
		}

		/// <summary> 
		/// Whether or not jobs defined in the XML file should be overwrite existing
		/// jobs with the same name.
		/// </summary>
		public virtual bool OverWriteExistingJobs
		{
			get { return overWriteExistingJobs; }
			set { overWriteExistingJobs = value; }
		}

		/// <summary> 
		/// The interval (in seconds) at which to scan for changes to the file.  
		/// If the file has been changed, it is re-loaded and parsed.   The default 
		/// value for the interval is 0, which disables scanning.
		/// </summary>
		public virtual long ScanInterval
		{
			get { return scanInterval/1000; }
			set { scanInterval = value*1000; }
		}

		/// <summary> 
		/// Whether or not initialization of the plugin should fail (throw an
		/// exception) if the file cannot be found. Default is <code>true</code>.
		/// </summary>
		public virtual bool FailOnFileNotFound
		{
			get { return failOnFileNotFound; }
			set { failOnFileNotFound = value; }
		}

		/// <summary> 
		/// Whether or not the context class loader should be used. Default is <code>true</code>.
		/// </summary>
		public virtual bool UseContextClassLoader
		{
			get { return useContextClassLoader; }
			set { useContextClassLoader = value; }
		}

		/// <summary> 
		/// Whether or not the XML should be validated. Default is <code>false</code>.
		/// </summary>
		public virtual bool Validating
		{
			get { return validating; }
			set { validating = value; }
		}

		/// <summary> 
		/// Whether or not the XML schema should be validated. Default is <code>true</code>.
		/// </summary>
		public virtual bool ValidatingSchema
		{
			get { return validatingSchema; }
			set { validatingSchema = value; }
		}


		private string FilePath
		{
			get
			{
				if (filePath == null)
				{
					findFile();
				}
				return filePath;
			}
		}


		private string name;
		private IScheduler scheduler;
		private bool overWriteExistingJobs = false;
		private bool failOnFileNotFound = true;
		private bool fileFound = false;
		private string fileName;
		private string filePath = null;
		private bool useContextClassLoader = true;
		private bool validating = false;
		private bool validatingSchema = true;
		private long scanInterval = 0;
		internal bool initializing = true;
		internal bool started = false;
		protected internal IClassLoadHelper classLoadHelper = null;


		public JobInitializationPlugin()
		{
			fileName = null; // TODO JobSchedulingDataProcessor.QUARTZ_XML_FILE_NAME;
		}

		/// <summary> <p>
		/// Called during creation of the <code>Scheduler</code> in order to give
		/// the <code>SchedulerPlugin</code> a chance to initialize.
		/// </p>
		/// 
		/// </summary>
		/// <throws>  SchedulerConfigException </throws>
		/// <summary>           if there is an error initializing.
		/// </summary>
		public virtual void Initialize(String pluginName, IScheduler s)
		{
			initializing = true;

			classLoadHelper = new CascadingClassLoadHelper();
			classLoadHelper.Initialize();

			try
			{
				name = pluginName;
				scheduler = s;

				Log.Info("Registering Quartz Job Initialization Plug-in.");

				findFile();
			}
			finally
			{
				initializing = false;
			}
		}

		/// <summary> </summary>
		private void findFile()
		{
			Stream f = null;
			String furl = null;

			FileInfo file = new FileInfo(FileName); // files in filesystem

			bool tmpBool;
			if (File.Exists(file.FullName))
			{
				tmpBool = true;
			}
			else
			{
				tmpBool = Directory.Exists(file.FullName);
			}
			if (!tmpBool)
			{
				Uri url = classLoadHelper.GetResource(FileName);
				if (url != null)
				{
					// we need jdk 1.3 compatibility, so we abandon this code...
					//                try {
					//                    furl = URLDecoder.decode(url.getPath(), "UTF-8");
					//                } catch (UnsupportedEncodingException e) {
					//                    furl = url.getPath();
					//                }
					furl = HttpUtility.UrlDecode(url.AbsolutePath);
					file = new FileInfo(furl);
					try
					{
						f = WebRequest.Create(url).GetResponse().GetResponseStream();
					}
					catch (IOException)
					{
						// Swallow the exception
					}
				}
			}
			else
			{
				try
				{
					f = new FileStream(file.FullName, FileMode.Open, FileAccess.Read);
				}
				catch (FileNotFoundException)
				{
					// ignore
				}
			}

			if (f == null && FailOnFileNotFound)
			{
				throw new SchedulerException("File named '" + FileName + "' does not exist.");
			}
			else if (f == null)
			{
				Log.Warn("File named '" + FileName + "' does not exist.");
			}
			else
			{
				fileFound = true;
				try
				{
					if (furl != null)
					{
						filePath = furl;
					}
					else
					{
						filePath = file.FullName;
					}
					f.Close();
				}
				catch (IOException ioe)
				{
					Log.Warn("Error closing jobs file " + FileName, ioe);
				}
			}
		}

		public virtual void Start()
		{
			if (scanInterval > 0)
			{
				try
				{
					SimpleTrigger trig =
						new SimpleTrigger("JobInitializationPlugin_" + name, "JobInitializationPlugin", DateTime.Now, null,
						                  SimpleTrigger.REPEAT_INDEFINITELY, scanInterval);
					trig.Volatility = true;
					JobDetail job = new JobDetail("JobInitializationPlugin_" + name, "JobInitializationPlugin", typeof (FileScanJob));
					job.Volatility = true;
					job.JobDataMap.Put(FileScanJob.FILE_NAME, FilePath);
					job.JobDataMap.Put(FileScanJob.FILE_SCAN_LISTENER_NAME, "JobInitializationPlugin_" + name);

					scheduler.Context.Put("JobInitializationPlugin_" + name, this);
					scheduler.ScheduleJob(job, trig);
				}
				catch (SchedulerException se)
				{
					Log.Error("Error starting background-task for watching jobs file.", se);
				}
			}

			try
			{
				ProcessFile();
			}
			finally
			{
				started = true;
			}
		}

		/// <summary> <p>
		/// Called in order to inform the <code>SchedulerPlugin</code> that it
		/// should free up all of it's resources because the scheduler is shutting
		/// down.
		/// </p>
		/// </summary>
		public virtual void Shutdown()
		{
			// nothing to do
		}


		public virtual void ProcessFile()
		{
			if (!fileFound)
			{
				return;
			}

			/*
			TODO
			JobSchedulingDataProcessor processor =
				new JobSchedulingDataProcessor(UseContextClassLoader, Validating, ValidatingSchema);
			*/
			try
			{
				// TODO processor.ProcessFileAndScheduleJobs(fileName, scheduler, OverWriteExistingJobs);
			}
			catch (Exception e)
			{
				Log.Error("Error scheduling jobs: " + e.Message, e);
			}
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="fName"></param>
		public virtual void FileUpdated(string fName)
		{
			if (started)
			{
				ProcessFile();
			}
		}
	}

}
