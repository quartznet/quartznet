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
using System.Globalization;
using System.IO;
using System.Net;
using System.Web;

using Common.Logging;

using Quartz.Collection;
using Quartz.Job;
using Quartz.Plugin.Xml;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Util;
using Quartz.Xml;

namespace Quartz
{
    /// <summary>
    /// Attribute to use with public <see cref="TimeSpan" /> properties that
    /// can be set with Quartz configuration. Attribute can be used to advice
    /// parsing to use correct type of time span (milliseconds, seconds, minutes, hours)
    /// as it may depend on property.
    /// </summary>
    /// <seealso cref="TimeSpanParseRuleAttribute" />
    public class TimeSpanParseRuleAttribute : Attribute
    {
        private readonly TimeSpanParseRule rule;

        /// <summary>
        /// Initializes a new instance of the <see cref="TimeSpanParseRuleAttribute"/> class.
        /// </summary>
        /// <param name="rule">The rule.</param>
        public TimeSpanParseRuleAttribute(TimeSpanParseRule rule)
        {
            this.rule = rule;
        }

        /// <summary>
        /// Gets the rule.
        /// </summary>
        /// <value>The rule.</value>
        public TimeSpanParseRule Rule
        {
            get { return rule; }
        }
    }

    /// <summary>
    /// Possible parse rules for <see cref="TimeSpan" />s.
    /// </summary>
    public enum TimeSpanParseRule
    {
        /// <summary>
        /// 
        /// </summary>
        Milliseconds = 0,
        
        /// <summary>
        /// 
        /// </summary>
        Seconds = 1,
        
        /// <summary>
        /// 
        /// </summary>
        Minutes = 2,
        
        /// <summary>
        /// 
        /// </summary>
        Hours = 3,
        
        /// <summary>
        /// 
        /// </summary>
        Days = 3
    }
}

namespace Quartz.Plugin.Xml
{
    /// <summary> 
    /// This plugin loads XML file(s) to add jobs and schedule them with triggers
    /// as the scheduler is initialized, and can optionally periodically scan the
    /// file for changes.
    ///</summary>
    /// <remarks>
    /// The periodically scanning of files for changes is not currently supported in a 
    /// clustered environment.
    /// </remarks> 
    /// <author>James House</author>
    /// <author>Pierre Awaragi</author>
    public class JobInitializationPlugin : ISchedulerPlugin, IFileScanListener
    {
        private readonly ILog log;
        private const int MaxJobTriggerNameLength = 80;
        private const string JobInitializationPluginName = "JobInitializationPlugin";
        private const char FileNameDelimiter = ',';

        private bool overwriteExistingJobs = false;
        private bool failOnFileNotFound = true;
        private string fileNames = JobSchedulingDataProcessor.QuartzXmlFileName;

        // Populated by initialization
        private readonly IDictionary jobFiles = new Hashtable();

        private bool useContextClassLoader = true;
        private bool validating = false;
        private bool validatingSchema = true;
        private TimeSpan scanInterval = TimeSpan.Zero;

        private bool started = false;

        protected ITypeLoadHelper classLoadHelper = null;

        private readonly ISet jobTriggerNameSet = new HashSet();
        private IScheduler scheduler;
        private string name;

        /// <summary>
        /// Initializes a new instance of the <see cref="JobInitializationPlugin"/> class.
        /// </summary>
        public JobInitializationPlugin()
        {
            log = LogManager.GetLogger(typeof (JobInitializationPlugin));
            fileNames = JobSchedulingDataProcessor.QuartzXmlFileName;
        }


        /// <summary>
        /// Gets the log.
        /// </summary>
        /// <value>The log.</value>
        protected ILog Log
        {
            get { return log; }
        }


        public string Name
        {
            get { return name; }
        }

        public IScheduler Scheduler
        {
            get { return scheduler; }
        }

        /// <summary> 
        /// Comma separated list of file names (with paths) to the XML files that should be read.
        /// </summary>
        public virtual string FileNames
        {
            get { return fileNames; }
            set { fileNames = value; }
        }

        /// <summary> 
        /// Whether or not jobs defined in the XML file should be overwrite existing
        /// jobs with the same name.
        /// </summary>
        public virtual bool OverwriteExistingJobs
        {
            get { return overwriteExistingJobs; }
            set { overwriteExistingJobs = value; }
        }

        /// <summary> 
        /// The interval at which to scan for changes to the file.  
        /// If the file has been changed, it is re-loaded and parsed.   The default 
        /// value for the interval is 0, which disables scanning.
        /// </summary>
        [TimeSpanParseRule(TimeSpanParseRule.Seconds)]
        public virtual TimeSpan ScanInterval
        {
            get { return scanInterval; }
            set { scanInterval = value; }
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


        #region IFileScanListener Members

        /// <summary>
        /// 
        /// </summary>
        /// <param name="fName"></param>
        public virtual void FileUpdated(string fName)
        {
            if (started)
            {
                ProcessFile(fName);
            }
        }

        #endregion

        #region ISchedulerPlugin Members

        /// <summary>
        /// Called during creation of the <see cref="IScheduler"/> in order to give
        /// the <see cref="ISchedulerPlugin"/> a chance to initialize.
        /// </summary>
        /// <param name="pluginName">The name.</param>
        /// <param name="sched">The scheduler.</param>
        /// <throws>SchedulerConfigException </throws>
        public virtual void Initialize(string pluginName, IScheduler sched)
        {
            name = pluginName;
            scheduler = sched;
            classLoadHelper = new CascadingClassLoadHelper();
            classLoadHelper.Initialize();

            Log.Info("Registering Quartz Job Initialization Plug-in.");

            // Create JobFile objects
            string[] tokens = fileNames.Split(FileNameDelimiter);

            foreach (string token in tokens)
            {
                JobFile jobFile = new JobFile(this, token);
                jobFiles.Add(jobFile.FilePath, jobFile);
            }
        }

        /// <summary>
        /// Called when the associated <see cref="IScheduler"/> is started, in order
        /// to let the plug-in know it can now make calls into the scheduler if it
        /// needs to.
        /// </summary>
        public virtual void Start()
        {
            try
            {
                if (jobFiles.Count > 0)
                {
                    if (scanInterval > TimeSpan.Zero)
                    {
                        scheduler.Context.Put(JobInitializationPluginName + '_' + Name, this);
                    }

                    foreach (JobFile jobFile in jobFiles.Values)
                    {

                        if (scanInterval > TimeSpan.Zero)
                        {
                            String jobTriggerName = BuildJobTriggerName(jobFile.FileBasename);

                            SimpleTrigger trig = new SimpleTrigger(
                                jobTriggerName,
                                JobInitializationPluginName,
                                DateTime.UtcNow, null,
                                SimpleTrigger.RepeatIndefinitely, scanInterval);
                            trig.Volatile = true;

                            JobDetail job = new JobDetail(
                                jobTriggerName,
                                JobInitializationPluginName,
                                typeof(FileScanJob));

                            job.Volatile = true;
                            job.JobDataMap.Put(FileScanJob.FileName, jobFile.FilePath);
                            job.JobDataMap.Put(FileScanJob.FileScanListenerName, JobInitializationPluginName + '_' + Name);

                            scheduler.ScheduleJob(job, trig);
                        }

                        ProcessFile(jobFile);
                    }
                }
            }
            catch (SchedulerException se)
            {
                Log.Error("Error starting background-task for watching jobs file.", se);
            }
            finally
            {
                started = true;
            }
        }

        /**
     * Helper method for generating unique job/trigger name for the  
     * file scanning jobs (one per FileJob).  The unique names are saved
     * in jobTriggerNameSet.
     */

        private string BuildJobTriggerName(string fileBasename)
        {
            // Name w/o collisions will be prefix + _ + filename (with '.' of filename replaced with '_')
            // For example: JobInitializationPlugin_jobInitializer_myjobs_xml
            String jobTriggerName = JobInitializationPluginName + '_' + Name + '_' + fileBasename.Replace('.', '_');

            // If name is too long (DB column is 80 chars), then truncate to max length
            if (jobTriggerName.Length > MaxJobTriggerNameLength)
            {
                jobTriggerName = jobTriggerName.Substring(0, MaxJobTriggerNameLength);
            }

            // Make sure this name is unique in case the same file name under different
            // directories is being checked, or had a naming collision due to length truncation.
            // If there is a conflict, keep incrementing a _# suffix on the name (being sure
            // not to get too long), until we find a unique name.
            int currentIndex = 1;
            while (jobTriggerNameSet.Add(jobTriggerName) == false)
            {
                // If not our first time through, then strip off old numeric suffix
                if (currentIndex > 1)
                {
                    jobTriggerName = jobTriggerName.Substring(0, jobTriggerName.LastIndexOf('_'));
                }

                String numericSuffix = "_" + currentIndex++;

                // If the numeric suffix would make the name too long, then make room for it.
                if (jobTriggerName.Length > (MaxJobTriggerNameLength - numericSuffix.Length))
                {
                    jobTriggerName = jobTriggerName.Substring(0, (MaxJobTriggerNameLength - numericSuffix.Length));
                }

                jobTriggerName += numericSuffix;
            }

            return jobTriggerName;
        }

        /// <summary>
        /// Called in order to inform the <code>SchedulerPlugin</code> that it
        /// should free up all of it's resources because the scheduler is shutting
        /// down.
        /// </summary>
        public virtual void Shutdown()
        {
            // nothing to do
        }

        #endregion


        private void ProcessFile(JobFile jobFile)
        {
            if ((jobFile == null) || (jobFile.FileFound == false))
            {
                return;
            }

            JobSchedulingDataProcessor processor = new JobSchedulingDataProcessor(Validating, ValidatingSchema);

            try
            {
                processor.ProcessFileAndScheduleJobs(
                    jobFile.FilePath,
                    jobFile.FilePath, // systemId 
                    scheduler,
                    OverwriteExistingJobs);
            }
            catch (Exception e)
            {
                Log.Error("Error scheduling jobs: " + e.Message, e);
            }
        }

        public void ProcessFile(string filePath)
        {
            ProcessFile((JobFile) jobFiles[filePath]);
        }

    internal class JobFile
    {
        private readonly string fileName;

        // These are set by initialize()
        private string filePath;
        private string fileBasename;
        private bool fileFound;
        private readonly JobInitializationPlugin plugin;

        public JobFile(JobInitializationPlugin plugin, string fileName)
        {
            this.plugin = plugin;
            this.fileName = fileName;
            Initialize();
        }

        public string FileName
        {
            get { return fileName; }
        }

        public bool FileFound
        {
            get { return fileFound; }
        }

        public string FilePath
        {
            get { return filePath; }
        }

        public string FileBasename
        {
            get { return fileBasename; }
        }

        public void Initialize()
        {
            Stream f = null;
            try
            {
                string furl = null;

                string fName = FileName;
                
                // check for special lookup
                fName = FileUtil.ResolveFile(fName);

                FileInfo file = new FileInfo(fName); // files in filesystem
                if (!file.Exists)
                {
                    Uri url = plugin.classLoadHelper.GetResource(FileName);
                    if (url != null)
                    {
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

                if (f == null)
                {
                    if (plugin.FailOnFileNotFound)
                    {
                        throw new SchedulerException(
                            "File named '" + FileName + "' does not exist.");
                    }
                    else
                    {
                        plugin.Log.Warn(string.Format(CultureInfo.InvariantCulture, "File named '{0}' does not exist.", FileName));
                    }
                }
                else
                {
                    fileFound = true;
                    filePath = (furl != null) ? furl : file.FullName;
                    fileBasename = file.Name;
                }
            }
            finally
            {
                try
                {
                    if (f != null)
                    {
                        f.Close();
                    }
                }
                catch (IOException ioe)
                {
                    plugin.Log.Warn("Error closing jobs file " + FileName, ioe);
                }
            }
        }
    }

    }
}