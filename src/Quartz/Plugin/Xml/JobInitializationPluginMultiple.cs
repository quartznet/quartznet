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

using Common.Logging;

using Quartz.Job;
using Quartz.Spi;
using Quartz.Xml;

namespace Quartz.Plugins.Xml
{
	/// <summary> 
	/// This plugin loads XML files to add jobs and schedule them with triggers
	/// as the scheduler is initialized, and can optionally periodically scan the
	/// file for changes.
	/// </summary>
	/// <author> Brooke Hedrick</author>
	public class JobInitializationPluginMultiple : ISchedulerPlugin, IFileScanListener
	{
		private static readonly ILog Log = LogManager.GetLogger(typeof (JobInitializationPluginMultiple));

		private string name;
		private IScheduler scheduler;
		private bool overWriteExistingJobs = true;
		private bool failOnFileNotFound = true;
		private string fileName;
		private ArrayList files = ArrayList.Synchronized(new ArrayList(10));
		private bool validating = true;
		private bool validatingSchema = true;
		private long scanInterval = 0;
		internal bool initializing = true;
		internal bool started = false;
		
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
		/// The file name (and path) to the XML file that should be read.
		/// </summary>
		public virtual string FileName
		{
			get { return fileName; }
			set { fileName = value; }
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
		/// Whether or not the XML should be validated. Default is <code>true</code>.
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




		public JobInitializationPluginMultiple()
		{
			fileName = null; // TODO JobSchedulingDataProcessor.QUARTZ_XML_FILE_NAME;
		}


		/// <summary>
		/// Called during creation of the <code>Scheduler</code> in order to give
		/// the <code>SchedulerPlugin</code> a chance to initialize.
		/// </summary>
		/// <param name="pluginName">The name by which the plugin is identified.</param>
		/// <param name="sched">The scheduler to which the plugin is registered.</param>
		/// <throws>  SchedulerConfigException </throws>
		public virtual void Initialize(String pluginName, IScheduler sched)
		{
			initializing = true;
			try
			{
				name = pluginName;
				scheduler = sched;

				Log.Info("Registering Quartz Job Initialization Plug-in.");

				UpdateJobFileList();
			}
			finally
			{
				initializing = false;
			}
		}

		private void UpdateJobFileList()
		{
			string[] stok = fileName.Split(',');

			foreach (string s in stok)
			{
				JobFile jobFile = new JobFile(this, s);
				files.Add(jobFile);
			}
		}

		public virtual void Start()
		{
			//TODO:bth 6.3.2005 The way this works, I believe we only need one job scanning for changes per directory

			if (scanInterval > 0)
			{
				try
				{
					foreach (JobFile jobFile in files)
					{
						SimpleTrigger trig =
							new SimpleTrigger("JobInitializationPluginMultiple_" + name, "JobInitializationPluginMultiple", DateTime.Now,
							                  null, SimpleTrigger.REPEAT_INDEFINITELY, scanInterval);
						trig.Volatile = true;
						JobDetail job =
							new JobDetail("JobInitializationPluginMultiple_" + name, "JobInitializationPluginMultiple", typeof (FileScanJob));
						job.Volatile = true;
						job.JobDataMap.Put(FileScanJob.FILE_NAME, jobFile.FilePath);
						job.JobDataMap.Put(FileScanJob.FILE_SCAN_LISTENER_NAME, "JobInitializationPluginMultiple_" + name);


						scheduler.Context.Put("JobInitializationPluginMultiple_" + name, this);
						scheduler.ScheduleJob(job, trig);
					}
				}
				catch (SchedulerException se)
				{
					Log.Error("Error starting background-task for watching jobs file.", se);
				}
			}

			try
			{
				ProcessFiles();
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


		public virtual void ProcessFiles()
		{
			JobSchedulingDataProcessor processor = new JobSchedulingDataProcessor(Validating, ValidatingSchema);

			foreach (JobFile jobFile in files)
			{
				try
				{
					if (jobFile.FileFound)
					{
						processor.ProcessFileAndScheduleJobs(jobFile.FileName, scheduler, OverWriteExistingJobs);
					}
				}
				catch (Exception e)
				{
					Log.Error("Error scheduling jobs: " + e.Message, e);
				}
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
				ProcessFiles();
			}
		}

		internal class JobFile
		{
			private JobInitializationPluginMultiple enclosingInstance;

			protected internal virtual string FileName
			{
				get { return fileName; }
			}

			protected internal virtual bool FileFound
			{
				get
				{
					if (filePath == null)
					{
						findFile();
					}

					return fileFound;
				}
			}

			protected internal virtual string FilePath
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

			public JobInitializationPluginMultiple Enclosing_Instance
			{
				get { return enclosingInstance; }
			}

			private string fileName = null;

			private string filePath = null;

			private bool fileFound = false;

			protected internal JobFile(JobInitializationPluginMultiple enclosingInstance, string fileName)
			{
				this.enclosingInstance = enclosingInstance;
				this.fileName = fileName;
			}

			/// <summary> </summary>
			private void findFile()
			{
				Stream f = null;

				FileInfo file = new FileInfo(fileName); // files in filesystem
				bool tmpBool;
				if (File.Exists(file.FullName))
				{
					tmpBool = true;
				}
				else
				{
					tmpBool = Directory.Exists(file.FullName);
				}
				if (file == null || !tmpBool)
				{
					// files in classpath
					Uri url = null; // TODO SupportClass.QuartzThread.Current().getContextClassLoader().getResource(fileName);
					if (url != null)
					{
						file = new FileInfo(url.AbsolutePath);
					}
				}
				try
				{
					f = new FileStream(file.FullName, FileMode.Open, FileAccess.Read);
				}
				catch (FileNotFoundException)
				{
					// ignore
				}

				if (f == null && Enclosing_Instance.FailOnFileNotFound)
				{
					throw new SchedulerException("File named '" + fileName + "' does not exist.");
				}
				else if (f == null)
				{
					Log.Warn("File named '" + fileName + "' does not exist.");
				}
				else
				{
					fileFound = true;
					try
					{
						filePath = file.FullName;
						f.Close();
					}
					catch (IOException ioe)
					{
						Log.Warn("Error closing file named '" + fileName, ioe);
					}
				}
			}
		}
	}

}
