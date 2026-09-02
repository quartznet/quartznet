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

namespace Quartz.Plugins.Json;

/// <summary>
/// This plugin loads JSON file(s) to add jobs and schedule them with triggers
/// as the scheduler is initialized, and can optionally periodically scan the
/// file for changes.
/// </summary>
/// <remarks>
/// <para>
/// This is the JSON analog of <see cref="Quartz.Plugins.Xml.XmlSchedulingDataProcessorPlugin"/>, and
/// the maintained one: JSON is where a scheduling file gains what the schedule gains — daily time
/// interval triggers, retry policies and execution groups are readable here and are not in the frozen
/// XML schema. The two plugins have one surface deliberately, so a file format is the only thing that
/// differs between them.
/// </para>
/// <para>
/// The periodically scanning of files for changes is not currently supported in a
/// clustered environment.
/// </para>
/// </remarks>
public sealed class JsonSchedulingDataProcessorPlugin : ISchedulerPlugin, IFileScanListener
{
    private const int MaxJobTriggerNameLength = 80;
    private const string PluginName = "JsonSchedulingDataProcessorPlugin";
    private const char FileNameDelimiter = ',';

    private readonly List<KeyValuePair<string, JobFile>> jobFiles = [];
    private readonly HashSet<string> jobTriggerNameSet = [];
    private readonly ILogger<JsonSchedulingDataProcessorPlugin> logger;
    private readonly TimeProvider timeProvider;
    private bool started;

    /// <summary>
    /// Creates the plugin with the static logger, a reflecting type loader and the system clock, for a
    /// plugin the loader built from a <c>quartz.plugin.&lt;name&gt;.type</c> key and so had nothing to
    /// inject into.
    /// </summary>
    public JsonSchedulingDataProcessorPlugin()
        : this(LogProvider.CreateLogger<JsonSchedulingDataProcessorPlugin>(), new SimpleTypeLoader(), TimeProvider.System)
    {
    }

    /// <summary>
    /// Creates the plugin with what a container resolved, which is what
    /// <c>UseJsonSchedulingConfiguration</c> uses.
    /// </summary>
    /// <param name="logger">Where the plugin reports what it loaded and what it could not.</param>
    /// <param name="typeLoader">Resolves the job types the file names as strings.</param>
    /// <param name="timeProvider">The clock the scanned files' timestamps are compared against.</param>
    public JsonSchedulingDataProcessorPlugin(
        ILogger<JsonSchedulingDataProcessorPlugin> logger,
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
    /// Comma separated list of file names (with paths) to the JSON files that should be read.
    /// </summary>
    /// <inheritdoc cref="Quartz.Plugins.Xml.XmlSchedulingDataProcessorPlugin.FileNames" path="/remarks" />
    internal string FileNames { get; set; } = JsonSchedulingDataProcessor.QuartzJsonFileName;

    /// <summary>
    /// The interval at which to scan for changes to the file. Zero, the default, disables scanning.
    /// </summary>
    [TimeSpanParseRule(TimeSpanParseRule.Seconds)]
    internal TimeSpan ScanInterval { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Whether initialization of the plugin fails when a file cannot be found.
    /// </summary>
    internal bool FailOnFileNotFound { get; set; } = true;

    /// <summary>
    /// Whether starting of the plugin fails when a file cannot be handled.
    /// </summary>
    internal bool FailOnSchedulingError { get; set; }

    internal IReadOnlyCollection<KeyValuePair<string, JobFile>> JobFiles => jobFiles;

    /// <inheritdoc />
    public ValueTask FileUpdated(string fileName, CancellationToken cancellationToken = default)
    {
        if (started)
        {
            return ProcessFile(fileName, cancellationToken);
        }

        return default;
    }

    /// <inheritdoc />
    public async ValueTask Initialize(string pluginName, IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        Name = pluginName;
        Scheduler = scheduler;

        logger.PluginRegistered();

        var tokens = FileNames
            .Split([FileNameDelimiter], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .ToArray();

        foreach (var token in tokens)
        {
            var jobFile = new JobFile(this, token);
            await jobFile.Initialize(cancellationToken).ConfigureAwait(false);
            jobFiles.Add(new KeyValuePair<string, JobFile>(jobFile.FilePath, jobFile));
        }
    }

    /// <inheritdoc />
    public async ValueTask Start(CancellationToken cancellationToken = default)
    {
        try
        {
            if (jobFiles.Count > 0)
            {
                if (ScanInterval > TimeSpan.Zero)
                {
                    Scheduler.Context[PluginName + '_' + Name] = this;
                }

                foreach (var (_, jobFile) in jobFiles)
                {
                    if (ScanInterval > TimeSpan.Zero)
                    {
                        var jobTriggerName = BuildJobTriggerName(jobFile.FileBasename);
                        var tKey = new TriggerKey(jobTriggerName, PluginName);

                        await Scheduler.UnscheduleJob(tKey, cancellationToken).ConfigureAwait(false);

                        var trig = new SimpleTriggerImpl(timeProvider);
                        trig.Key = tKey;
                        trig.StartTimeUtc = timeProvider.GetUtcNow();
                        trig.EndTimeUtc = null;
                        trig.RepeatCount = SimpleTriggerImpl.RepeatIndefinitely;
                        trig.RepeatInterval = ScanInterval;

                        var job = JobBuilder.Create<FileScanJob>()
                            .WithIdentity(new JobKey(jobTriggerName, PluginName))
                            .Build();

                        job.JobDataMap[FileScanJob.FileName] = jobFile.FilePath;
                        job.JobDataMap[FileScanJob.FileScanListenerName] = PluginName + '_' + Name;

                        await Scheduler.ScheduleJob(job, trig, cancellationToken: cancellationToken).ConfigureAwait(false);
                        logger.FileScanJobScheduled(jobFile.FileName, ScanInterval);
                    }

                    await ProcessFile(jobFile, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (SchedulerException ex)
        {
            if (FailOnSchedulingError) throw;
            logger.FileWatchStartFailed(ex);
        }
        finally
        {
            started = true;
        }
    }

    private string BuildJobTriggerName(string fileBasename)
    {
        var jobTriggerName = PluginName + '_' + Name + '_' + fileBasename.Replace('.', '_');

        if (jobTriggerName.Length > MaxJobTriggerNameLength)
        {
            jobTriggerName = jobTriggerName[..MaxJobTriggerNameLength];
        }

        var currentIndex = 1;
        while (!jobTriggerNameSet.Add(jobTriggerName))
        {
            if (currentIndex > 1)
            {
                jobTriggerName = jobTriggerName[..jobTriggerName.LastIndexOf('_')];
            }

            var numericSuffix = "_" + currentIndex++;

            if (jobTriggerName.Length > MaxJobTriggerNameLength - numericSuffix.Length)
            {
                jobTriggerName = jobTriggerName[..(MaxJobTriggerNameLength - numericSuffix.Length)];
            }

            jobTriggerName += numericSuffix;
        }

        return jobTriggerName;
    }

    private async ValueTask ProcessFile(JobFile? jobFile, CancellationToken cancellationToken = default)
    {
        if (jobFile is null || !jobFile.FileFound) return;

        try
        {
            var processor = new JsonSchedulingDataProcessor(
                LogProvider.CreateLogger<JsonSchedulingDataProcessor>(), TypeLoader, timeProvider);

            processor.AddJobGroupToNeverDelete(PluginName);
            processor.AddTriggerGroupToNeverDelete(PluginName);
            processor.ProtectJobGroup(PluginName);
            processor.ProtectTriggerGroup(PluginName);

            await processor.ProcessJsonFileAndScheduleJobs(jobFile.FileName, Scheduler, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception e)
        {
            var message = "Could not schedule jobs and triggers from JSON file " + jobFile.FileName + ": " + e.Message;
            var schedulerException = new SchedulerException(message, e);

            logger.FileProcessingFailed(jobFile.FileName, e);

            // No keys: the file names many jobs and triggers, and the failure is the file rather than
            // any one of them.
            SchedulerErrorContext errorContext = new()
            {
                Message = message,
                Exception = schedulerException,
            };

            foreach (var listener in Scheduler.ListenerManager.GetSchedulerListeners())
            {
                try
                {
                    await listener.SchedulerError(Scheduler, errorContext, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    logger.ListenerNotificationOfErrorFailed(ex);
                }
            }

            if (FailOnSchedulingError) throw schedulerException;
        }
    }

    private ValueTask ProcessFile(string filePath, CancellationToken cancellationToken = default)
    {
        var idx = jobFiles.FindIndex(pair => pair.Key == filePath);
        return ProcessFile(idx >= 0 ? jobFiles[idx].Value : null, cancellationToken);
    }

    /// <summary>
    /// Information about a file that should be processed by <see cref="JsonSchedulingDataProcessor" />.
    /// </summary>
    internal sealed class JobFile
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

        public async ValueTask Initialize(CancellationToken cancellationToken = default)
        {
            Stream? f = null;
            try
            {
                var fName = FileUtil.ResolveFile(FileName) ?? FileName;
                var file = new FileInfo(fName);

                if (file.Exists)
                {
                    try { f = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); }
                    catch (FileNotFoundException) { }
                }

                if (f is null)
                {
                    if (plugin.FailOnFileNotFound) throw new SchedulerException("File named '" + FileName + "' does not exist.");
                    else plugin.logger.FileNotFound(FileName);
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
                    if (f is not null)
                    {
                        await f.DisposeAsync().ConfigureAwait(false);
                    }
                }
                catch (IOException ioe) { plugin.logger.FileCloseFailed(FileName, ioe); }
            }
        }
    }
}
