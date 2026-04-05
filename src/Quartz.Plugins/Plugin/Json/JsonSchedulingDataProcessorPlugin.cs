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

namespace Quartz.Plugin.Json;

/// <summary>
/// This plugin loads JSON file(s) to add jobs and schedule them with triggers
/// as the scheduler is initialized, and can optionally periodically scan the
/// file for changes.
/// </summary>
/// <remarks>
/// This is the JSON analog of <see cref="Xml.XMLSchedulingDataProcessorPlugin"/>.
/// The periodically scanning of files for changes is not currently supported in a
/// clustered environment.
/// </remarks>
public sealed class JsonSchedulingDataProcessorPlugin : ISchedulerPlugin, IFileScanListener
{
    private const int MaxJobTriggerNameLength = 80;
    private const string PluginName = "JsonSchedulingDataProcessorPlugin";
    private const char FileNameDelimiter = ',';

    private readonly List<KeyValuePair<string, JobFile>> jobFiles = new List<KeyValuePair<string, JobFile>>();
    private readonly HashSet<string> jobTriggerNameSet = new HashSet<string>();
    private bool started;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonSchedulingDataProcessorPlugin"/> class.
    /// </summary>
    public JsonSchedulingDataProcessorPlugin() : this(new SimpleTypeLoadHelper())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonSchedulingDataProcessorPlugin"/> class.
    /// </summary>
    public JsonSchedulingDataProcessorPlugin(ITypeLoadHelper typeLoadHelper)
    {
        Log = LogProvider.GetLogger(typeof(JsonSchedulingDataProcessorPlugin));
        TypeLoadHelper = typeLoadHelper;
    }

    private ILog Log { get; }

    public string Name { get; private set; } = null!;

    public IScheduler Scheduler { get; private set; } = null!;

    private ITypeLoadHelper TypeLoadHelper { get; }

    /// <summary>
    /// Comma separated list of file names (with paths) to the JSON files that should be read.
    /// </summary>
    public string FileNames { get; set; } = JsonSchedulingDataProcessor.QuartzJsonFileName;

    /// <summary>
    /// The interval at which to scan for changes to the file.
    /// If the file has been changed, it is re-loaded and parsed. The default
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
    public bool FailOnSchedulingError { get; set; }

    /// <inheritdoc />
    public Task FileUpdated(
        string fName,
        CancellationToken cancellationToken = default)
    {
        if (started)
        {
            return ProcessFile(fName, cancellationToken);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task Initialize(
        string pluginName,
        IScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        Name = pluginName;
        Scheduler = scheduler;

        Log.Info("Registering Quartz JSON Job Initialization Plug-in.");

        string[] tokens = FileNames
            .Split(new[] { FileNameDelimiter }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .ToArray();

        foreach (string token in tokens)
        {
            JobFile jobFile = new JobFile(this, token);
            await jobFile.Initialize(cancellationToken).ConfigureAwait(false);
            jobFiles.Add(new KeyValuePair<string, JobFile>(jobFile.FilePath, jobFile));
        }
    }

    /// <inheritdoc />
    public async Task Start(CancellationToken cancellationToken = default)
    {
        try
        {
            if (jobFiles.Count > 0)
            {
                if (ScanInterval > TimeSpan.Zero)
                {
                    Scheduler.Context.Put(PluginName + '_' + Name, this);
                }

                foreach (KeyValuePair<string, JobFile> pair in jobFiles)
                {
                    JobFile jobFile = pair.Value;

                    if (ScanInterval > TimeSpan.Zero)
                    {
                        string jobTriggerName = BuildJobTriggerName(jobFile.FileBasename);

                        TriggerKey tKey = new TriggerKey(jobTriggerName, PluginName);

                        await Scheduler.UnscheduleJob(tKey, cancellationToken).ConfigureAwait(false);

                        SimpleTriggerImpl trig = new SimpleTriggerImpl
                        {
                            Name = jobTriggerName,
                            Group = PluginName,
                            StartTimeUtc = SystemTime.UtcNow(),
                            EndTimeUtc = null,
                            RepeatCount = SimpleTriggerImpl.RepeatIndefinitely,
                            RepeatInterval = ScanInterval
                        };

                        JobDetailImpl job = new JobDetailImpl(
                            jobTriggerName,
                            PluginName,
                            typeof(FileScanJob));

                        job.JobDataMap.Put(FileScanJob.FileName, jobFile.FilePath);
                        job.JobDataMap.Put(FileScanJob.FileScanListenerName, PluginName + '_' + Name);

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
            Log.ErrorException("Error starting background-task for watching JSON jobs file.", se);
        }
        finally
        {
            started = true;
        }
    }

    /// <inheritdoc />
    public Task Shutdown(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    private string BuildJobTriggerName(string fileBasename)
    {
        string jobTriggerName = PluginName + '_' + Name + '_' + fileBasename.Replace('.', '_');

        if (jobTriggerName.Length > MaxJobTriggerNameLength)
        {
            jobTriggerName = jobTriggerName.Substring(0, MaxJobTriggerNameLength);
        }

        int currentIndex = 1;
        while (jobTriggerNameSet.Add(jobTriggerName) == false)
        {
            if (currentIndex > 1)
            {
                jobTriggerName = jobTriggerName.Substring(0, jobTriggerName.LastIndexOf('_'));
            }

            string numericSuffix = "_" + currentIndex++;

            if (jobTriggerName.Length > MaxJobTriggerNameLength - numericSuffix.Length)
            {
                jobTriggerName = jobTriggerName.Substring(0, MaxJobTriggerNameLength - numericSuffix.Length);
            }

            jobTriggerName += numericSuffix;
        }

        return jobTriggerName;
    }

    private async Task ProcessFile(JobFile? jobFile, CancellationToken cancellationToken = default)
    {
        if (jobFile is null || !jobFile.FileFound)
        {
            return;
        }

        try
        {
            JsonSchedulingDataProcessor processor = new JsonSchedulingDataProcessor(TypeLoadHelper);

            processor.AddJobGroupToNeverDelete(PluginName);
            processor.AddTriggerGroupToNeverDelete(PluginName);
            processor.ProtectJobGroup(PluginName);
            processor.ProtectTriggerGroup(PluginName);

            await processor.ProcessJsonFileAndScheduleJobs(
                jobFile.FileName,
                Scheduler,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            string message = "Could not schedule jobs and triggers from JSON file " + jobFile.FileName + ": " + e.Message;
            SchedulerException schedulerException = new SchedulerException(message, e);

            Log.ErrorException(message, e);

            IReadOnlyCollection<ISchedulerListener> listeners = Scheduler.ListenerManager.GetSchedulerListeners();
            foreach (ISchedulerListener listener in listeners)
            {
                try
                {
                    await listener.SchedulerError(message, schedulerException, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Log.ErrorException("Error while notifying SchedulerListener of error: ", ex);
                    Log.ErrorException("  Original error (for notification) was: " + message, schedulerException);
                }
            }

            if (FailOnSchedulingError)
            {
                throw schedulerException;
            }
        }
    }

    private Task ProcessFile(string filePath, CancellationToken cancellationToken = default)
    {
        JobFile? file = null;
        int idx = jobFiles.FindIndex(pair => pair.Key == filePath);
        if (idx >= 0)
        {
            file = jobFiles[idx].Value;
        }
        return ProcessFile(file, cancellationToken);
    }

    private sealed class JobFile
    {
        private readonly JsonSchedulingDataProcessorPlugin plugin;

        public JobFile(JsonSchedulingDataProcessorPlugin plugin, string fileName)
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
                string fName = FileUtil.ResolveFile(FileName) ?? FileName;
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

                if (f is null)
                {
                    if (plugin.FailOnFileNotFound)
                    {
                        throw new SchedulerException("File named '" + FileName + "' does not exist.");
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

                FilePath = file.FullName;
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

            return Task.CompletedTask;
        }
    }
}
