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

using Microsoft.Extensions.Logging;
using Quartz.Diagnostics;
using Quartz.Impl.Triggers;
using Quartz.Jobs;
using Quartz.Impl;
using Quartz.Extensibility;
using Quartz.Util;
using Quartz.Xml;

namespace Quartz.Plugins.Xml;

/// <summary>
/// This plugin loads XML file(s) to add jobs and schedule them with triggers
/// as the scheduler is initialized, and can optionally periodically scan the
/// file for changes.
///</summary>
/// <remarks>
/// <para>
/// The XML format is frozen at <c>job_scheduling_data_2_0.xsd</c>: it declares <c>simple</c>,
/// <c>cron</c> and <c>calendar-interval</c> triggers and nothing else, and it will not gain the
/// trigger kinds or the trigger settings added since. Files that exist keep working for the life of
/// 4.x; a schedule that needs anything the schema cannot spell is written as JSON and read by
/// <see cref="Quartz.Plugins.Json.JsonSchedulingDataProcessorPlugin" />, which is the maintained
/// format.
/// </para>
/// <para>
/// The periodically scanning of files for changes is not currently supported in a
/// clustered environment.
/// </para>
/// </remarks>
/// <author>James House</author>
/// <author>Pierre Awaragi</author>
public sealed class XmlSchedulingDataProcessorPlugin : ISchedulerPlugin, IFileScanListener
{
    private const int MaxJobTriggerNameLength = 80;
    private const string JobInitializationPluginName = "XmlSchedulingDataProcessorPlugin";
    private const char FileNameDelimiter = ',';

    // Populated by initialization
    private readonly List<KeyValuePair<string, JobFile>> jobFiles = new List<KeyValuePair<string, JobFile>>();

    private bool started;

    private readonly HashSet<string> jobTriggerNameSet = new HashSet<string>();
    private readonly ILogger<XmlSchedulingDataProcessorPlugin> logger;
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="XmlSchedulingDataProcessorPlugin"/> class.
    /// </summary>
    public XmlSchedulingDataProcessorPlugin()
        : this(LogProvider.CreateLogger<XmlSchedulingDataProcessorPlugin>(), new SimpleTypeLoader(), TimeProvider.System)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="XmlSchedulingDataProcessorPlugin"/> class.
    /// </summary>
    public XmlSchedulingDataProcessorPlugin(
        ILogger<XmlSchedulingDataProcessorPlugin> logger,
        ITypeLoader typeLoader,
        TimeProvider timeProvider)
    {
        this.logger = logger;
        this.timeProvider = timeProvider;
        TypeLoader = typeLoader;
    }

    internal string Name { get; private set; } = null!;

    internal IScheduler Scheduler { get; private set; } = null!;

    private ITypeLoader TypeLoader { get; }

    /// <summary>
    /// Comma separated list of file names (with paths) to the XML files that should be read.
    /// </summary>
    /// <remarks>
    /// Delimited rather than a collection because <c>quartz.plugin.&lt;name&gt;.fileNames</c> writes
    /// it as one string. <see cref="FileSchedulingOptions.Files" /> is what a caller says it in code,
    /// and this is what that option is joined into.
    /// </remarks>
    internal string FileNames { get; set; } = XmlSchedulingDataProcessor.QuartzXmlFileName;

    /// <summary>
    /// The interval at which to scan for changes to the file.
    /// If the file has been changed, it is re-loaded and parsed.   The default
    /// value for the interval is 0, which disables scanning.
    /// </summary>
    [TimeSpanParseRule(TimeSpanParseRule.Seconds)]
    internal TimeSpan ScanInterval { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Whether or not initialization of the plugin should fail (throw an
    /// exception) if the file cannot be found. Default is <see langword="true" />.
    /// </summary>
    internal bool FailOnFileNotFound { get; set; } = true;

    /// <summary>
    /// Whether or not starting of the plugin should fail (throw an
    /// exception) if the file cannot be handled. Default is <see langword="false" />.
    /// </summary>
    internal bool FailOnSchedulingError { get; set; }

    internal IReadOnlyCollection<KeyValuePair<string, JobFile>> JobFiles => jobFiles;

    /// <inheritdoc />
    public ValueTask FileUpdated(
        string fileName,
        CancellationToken cancellationToken = default)
    {
        if (started)
        {
            return ProcessFile(fileName, cancellationToken);
        }

        return default;
    }

    /// <summary>
    /// Called during creation of the <see cref="IScheduler"/> in order to give
    /// the <see cref="ISchedulerPlugin"/> a chance to initialize.
    /// </summary>
    public async ValueTask Initialize(
        string pluginName,
        IScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        Name = pluginName;
        Scheduler = scheduler;

        logger.PluginRegistered();

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
    public async ValueTask Start(CancellationToken cancellationToken = default)
    {
        try
        {
            if (jobFiles.Count > 0)
            {
                if (ScanInterval > TimeSpan.Zero)
                {
                    Scheduler.Context[JobInitializationPluginName + '_' + Name] = this;
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
                        var trig = new SimpleTriggerImpl(timeProvider);
                        trig.Key = tKey;
                        trig.StartTimeUtc = timeProvider.GetUtcNow();
                        trig.EndTimeUtc = null;
                        trig.RepeatCount = SimpleTriggerImpl.RepeatIndefinitely;
                        trig.RepeatInterval = ScanInterval;

                        var job = JobBuilder.Create<FileScanJob>()
                            .WithIdentity(new JobKey(jobTriggerName, JobInitializationPluginName))
                            .Build();

                        job.JobDataMap[FileScanJob.FileName] = jobFile.FilePath;
                        job.JobDataMap[FileScanJob.FileScanListenerName] = JobInitializationPluginName + '_' + Name;

                        await Scheduler.ScheduleJob(job, trig, cancellationToken: cancellationToken).ConfigureAwait(false);
                        logger.FileScanJobScheduled(jobFile.FileName, ScanInterval);
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
            logger.FileWatchStartFailed(se);
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

    private async ValueTask NotifySchedulerListenersOfError(
        string message,
        SchedulerException schedulerException,
        CancellationToken cancellationToken = default)
    {
        var listeners = Scheduler.ListenerManager.GetSchedulerListeners();

        // No keys: the file names many jobs and triggers, and the failure is the file rather than any
        // one of them.
        SchedulerErrorContext errorContext = new()
        {
            Message = message,
            Exception = schedulerException,
        };

        foreach (var listener in listeners)
        {
            try
            {
                await listener.SchedulerError(Scheduler, errorContext, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.ListenerNotificationOfErrorFailed(ex);
                logger.OriginalErrorForNotification(message, schedulerException);
            }
        }
    }

    private async ValueTask ProcessFile(JobFile? jobFile, CancellationToken cancellationToken = default)
    {
        if (jobFile is null || jobFile.FileFound == false)
        {
            return;
        }

        try
        {
            XmlSchedulingDataProcessor processor = new(
                LogProvider.CreateLogger<XmlSchedulingDataProcessor>(),
                TypeLoader,
                timeProvider);

            processor.AddJobGroupToNeverDelete(JobInitializationPluginName);
            processor.AddTriggerGroupToNeverDelete(JobInitializationPluginName);

            await processor.ProcessFileAndScheduleJobs(
                jobFile.FileName,
                jobFile.FileName, // systemId
                Scheduler,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            var message = $"Could not schedule jobs and triggers from file {jobFile.FileName}: {e.Message}";
            var schedulerException = new SchedulerException(message, e);

            logger.FileProcessingFailed(jobFile.FileName, e.Message, e);

            await NotifySchedulerListenersOfError(message, schedulerException, cancellationToken).ConfigureAwait(false);

            if (FailOnSchedulingError)
            {
                throw schedulerException;
            }
        }
    }

    private ValueTask ProcessFile(string filePath, CancellationToken cancellationToken = default)
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
    /// Information about a file that should be processed by <see cref="XmlSchedulingDataProcessor" />.
    /// </summary>
    internal sealed class JobFile
    {
        // These are set by initialize()
        private readonly XmlSchedulingDataProcessorPlugin plugin;

        public JobFile(XmlSchedulingDataProcessorPlugin plugin, string fileName)
        {
            this.plugin = plugin;
            FileName = fileName;
        }

        public string FileName { get; }

        public bool FileFound { get; private set; }

        public string FilePath { get; private set; } = null!;

        public string FileBasename { get; private set; } = null!;

        public async ValueTask Initialize(CancellationToken cancellationToken = default)
        {
            Stream? f = null;
            try
            {
                string? furl = null;
                var fName = FileName;

                // check for special lookup
                fName = FileUtil.ResolveFile(fName) ?? fName;

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
                        throw new SchedulerException(
                            "File named '" + FileName + "' does not exist.");
                    }
                    else
                    {
                        plugin.logger.FileNotFound(FileName);
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
                    if (f is not null)
                    {
                        await f.DisposeAsync().ConfigureAwait(false);
                    }
                }
                catch (IOException ioe)
                {
                    plugin.logger.FileCloseFailed(FileName, ioe);
                }
            }
        }
    }
}
