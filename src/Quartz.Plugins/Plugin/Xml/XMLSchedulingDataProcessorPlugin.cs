#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Impl;
using Quartz.Impl.Triggers;
using Quartz.Job;
using Quartz.Logging;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Util;
using Quartz.Xml;

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
    public class XMLSchedulingDataProcessorPlugin : ISchedulerPlugin, IFileScanListener
    {
        private const int MaxJobTriggerNameLength = 80;
        private const string JobInitializationPluginName = "XMLSchedulingDataProcessorPlugin";
        private const char FileNameDelimiter = ',';

        // Populated by initialization
        private readonly List<KeyValuePair<string, JobFile>> jobFiles = new List<KeyValuePair<string, JobFile>>();

        private bool started;

        private readonly HashSet<string> jobTriggerNameSet = new HashSet<string>();

        /// <summary>
        /// Initializes a new instance of the <see cref="XMLSchedulingDataProcessorPlugin"/> class.
        /// </summary>
        public XMLSchedulingDataProcessorPlugin()
        {
            Log = LogProvider.GetLogger(typeof (XMLSchedulingDataProcessorPlugin));
        }

        /// <summary>
        /// Gets the log.
        /// </summary>
        /// <value>The log.</value>
        private ILog Log { get; }

        public string Name { get; private set; } = null!;

        public IScheduler Scheduler { get; private set; } = null!;

        protected ITypeLoadHelper TypeLoadHelper { get; private set; } = null!;

        /// <summary>
        /// Comma separated list of file names (with paths) to the XML files that should be read.
        /// </summary>
        public string FileNames { get; set; } = XMLSchedulingDataProcessor.QuartzXmlFileName;

        /// <summary>
        /// The interval at which to scan for changes to the file.
        /// If the file has been changed, it is re-loaded and parsed.   The default
        /// value for the interval is 0, which disables scanning.
        /// </summary>
        [TimeSpanParseRule(TimeSpanParseRule.Seconds)]
        public TimeSpan ScanInterval { get; set; } = TimeSpan.Zero;

        /// <summary>
        /// Whether or not initialization of the plugin should fail (throw an
        /// exception) if the file cannot be found. Default is <see langword="true" />.
        /// </summary>
        public bool FailOnFileNotFound { get; set; } = true;

        /// <summary>
        /// Whether or not starting of the plugin should fail (throw an
        /// exception) if the file cannot be handled. Default is <see langword="false" />.
        /// </summary>
        public virtual bool FailOnSchedulingError { get; set; }

        public IReadOnlyCollection<KeyValuePair<string, JobFile>> JobFiles => jobFiles;

        public virtual Task FileUpdated(
            string fName,
            CancellationToken cancellationToken = default)
        {
            if (started)
            {
                return ProcessFile(fName, cancellationToken);
            }

            return TaskUtil.CompletedTask;
        }

        /// <summary>
        /// Called during creation of the <see cref="IScheduler"/> in order to give
        /// the <see cref="ISchedulerPlugin"/> a chance to initialize.
        /// </summary>
        public virtual async Task Initialize(
            string pluginName,
            IScheduler scheduler,
            CancellationToken cancellationToken = default)
        {
            Name = pluginName;
            Scheduler = scheduler;
            TypeLoadHelper = new SimpleTypeLoadHelper();
            TypeLoadHelper.Initialize();

            Log.Info("Registering Quartz Job Initialization Plug-in.");

            // Create JobFile objects
            var tokens = FileNames.Split(FileNameDelimiter).Select(x => x.TrimStart());

            foreach (string token in tokens)
            {
                JobFile jobFile = new JobFile(this, token);
                await jobFile.Initialize(cancellationToken).ConfigureAwait(false);
                jobFiles.Add(new KeyValuePair<string, JobFile>(jobFile.FilePath, jobFile));
            }
        }

        /// <summary>
        /// Called when the associated <see cref="IScheduler"/> is started, in order
        /// to let the plug-in know it can now make calls into the scheduler if it
        /// needs to.
        /// </summary>
        public virtual async Task Start(CancellationToken cancellationToken = default)
        {
            try
            {
                if (jobFiles.Count > 0)
                {
                    if (ScanInterval > TimeSpan.Zero)
                    {
                        Scheduler.Context.Put(JobInitializationPluginName + '_' + Name, this);
                    }

                    foreach (KeyValuePair<string, JobFile> pair in jobFiles)
                    {
                        JobFile jobFile = pair.Value;

                        if (ScanInterval > TimeSpan.Zero)
                        {
                            string jobTriggerName = BuildJobTriggerName(jobFile.FileBasename);

                            TriggerKey tKey = new TriggerKey(jobTriggerName, JobInitializationPluginName);

                            // remove pre-existing job/trigger, if any
                            await Scheduler.UnscheduleJob(tKey, cancellationToken).ConfigureAwait(false);

                            // TODO: convert to use builder
                            var trig = new SimpleTriggerImpl();
                            trig.Name = jobTriggerName;
                            trig.Group = JobInitializationPluginName;
                            trig.StartTimeUtc = SystemTime.UtcNow();
                            trig.EndTimeUtc = null;
                            trig.RepeatCount = SimpleTriggerImpl.RepeatIndefinitely;
                            trig.RepeatInterval = ScanInterval;

                            // TODO: convert to use builder
                            JobDetailImpl job = new JobDetailImpl(
                                jobTriggerName,
                                JobInitializationPluginName,
                                typeof (FileScanJob));

                            job.JobDataMap.Put(FileScanJob.FileName, jobFile.FilePath);
                            job.JobDataMap.Put(FileScanJob.FileScanListenerName, JobInitializationPluginName + '_' + Name);

                            await Scheduler.ScheduleJob(job, trig, cancellationToken).ConfigureAwait(false);
                            Log.DebugFormat("Scheduled file scan job for data file: {0}, at interval: {1}", jobFile.FileName, ScanInterval);
                        }

                        await ProcessFile(jobFile, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (SchedulerException se)
            {
                if (FailOnSchedulingError)
                {
                    throw;
                }
                Log.ErrorException("Error starting background-task for watching jobs file.", se);
            }
            finally
            {
                started = true;
            }
        }

        /// <summary>
        /// Helper method for generating unique job/trigger name for the
        /// file scanning jobs (one per FileJob).  The unique names are saved
        /// in jobTriggerNameSet.
        /// </summary>
        /// <param name="fileBasename"></param>
        /// <returns></returns>
        private string BuildJobTriggerName(string fileBasename)
        {
            // Name w/o collisions will be prefix + _ + filename (with '.' of filename replaced with '_')
            // For example: JobInitializationPlugin_jobInitializer_myjobs_xml
            string jobTriggerName = JobInitializationPluginName + '_' + Name + '_' + fileBasename.Replace('.', '_');

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

                string numericSuffix = "_" + currentIndex++;

                // If the numeric suffix would make the name too long, then make room for it.
                if (jobTriggerName.Length > MaxJobTriggerNameLength - numericSuffix.Length)
                {
                    jobTriggerName = jobTriggerName.Substring(0, MaxJobTriggerNameLength - numericSuffix.Length);
                }

                jobTriggerName += numericSuffix;
            }

            return jobTriggerName;
        }

        /// <summary>
        /// Called in order to inform the <see cref="ISchedulerPlugin" /> that it
        /// should free up all of it's resources because the scheduler is shutting
        /// down.
        /// </summary>
        public virtual Task Shutdown(CancellationToken cancellationToken = default)
        {
            // nothing to do
            return TaskUtil.CompletedTask;
        }

        private async Task ProcessFile(JobFile? jobFile, CancellationToken cancellationToken = default)
        {
            if (jobFile == null || jobFile.FileFound == false)
            {
                return;
            }

            try
            {
                XMLSchedulingDataProcessor processor = new XMLSchedulingDataProcessor(TypeLoadHelper);

                processor.AddJobGroupToNeverDelete(JobInitializationPluginName);
                processor.AddTriggerGroupToNeverDelete(JobInitializationPluginName);

                await processor.ProcessFileAndScheduleJobs(
                    jobFile.FileName,
                    jobFile.FileName, // systemId
                    Scheduler,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                var message = "Could not schedule jobs and triggers from file " + jobFile.FileName + ": " + e.Message;
                if (FailOnSchedulingError)
                {
                    throw new SchedulerException(message, e);
                }
                else
                {
                    Log.ErrorException(message, e);
                }
            }
        }

        public Task ProcessFile(string filePath, CancellationToken cancellationToken = default)
        {
            JobFile? file = null;
            int idx = jobFiles.FindIndex(pair => pair.Key == filePath);
            if (idx >= 0)
            {
                file = jobFiles[idx].Value;
            }
            return ProcessFile(file, cancellationToken);
        }

        /// <summary>
        /// Information about a file that should be processed by <see cref="XMLSchedulingDataProcessor" />.
        /// </summary>
        public class JobFile
        {
            // These are set by initialize()
            private readonly XMLSchedulingDataProcessorPlugin plugin;

            public JobFile(XMLSchedulingDataProcessorPlugin plugin, string fileName)
            {
                this.plugin = plugin;
                FileName = fileName;
            }

            public string FileName { get; }

            public bool FileFound { get; private set; }

            public string FilePath { get; private set; } = null!;

            public string FileBasename { get; private set; } = null!;

            public Task Initialize(CancellationToken cancellationToken = default)
            {
                Stream? f = null;
                try
                {
                    string? furl = null;
                    var fName = FileName;

                    // check for special lookup
                    fName = FileUtil.ResolveFile(fName)!;

                    FileInfo file = new FileInfo(fName);
                    if (file.Exists)
                    {
                        try
                        {
                            f = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
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
                            plugin.Log.Warn($"File named '{FileName}' does not exist.");
                        }
                    }
                    else
                    {
                        FileFound = true;
                    }
                    FilePath = furl ?? file.FullName;
                    FileBasename = file.Name;
                }
                finally
                {
                    try
                    {
                        f?.Dispose();
                    }
                    catch (IOException ioe)
                    {
                        plugin.Log.WarnException("Error closing jobs file " + FileName, ioe);
                    }
                }

                return Task.FromResult(true);
            }
        }
    }
}