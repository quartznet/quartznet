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

using Quartz.Impl.Triggers;
using Quartz.Diagnostics;
using Quartz.Job;
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
/// This is the JSON analog of <see cref="Quartz.Plugin.Xml.XMLSchedulingDataProcessorPlugin"/>.
/// The periodically scanning of files for changes is not currently supported in a
/// clustered environment.
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

    public JsonSchedulingDataProcessorPlugin()
        : this(LogProvider.CreateLogger<JsonSchedulingDataProcessorPlugin>(), new SimpleTypeLoadHelper(), TimeProvider.System)
    {
    }

    public JsonSchedulingDataProcessorPlugin(
        ILogger<JsonSchedulingDataProcessorPlugin> logger,
        ITypeLoadHelper typeLoadHelper,
        TimeProvider timeProvider)
    {
        this.logger = logger;
        this.timeProvider = timeProvider;
        TypeLoadHelper = typeLoadHelper;
    }

    public string Name { get; private set; } = null!;
    public IScheduler Scheduler { get; private set; } = null!;
    private ITypeLoadHelper TypeLoadHelper { get; }

    public string FileNames { get; set; } = JsonSchedulingDataProcessor.QuartzJsonFileName;

    [TimeSpanParseRule(TimeSpanParseRule.Seconds)]
    public TimeSpan ScanInterval { get; set; } = TimeSpan.Zero;

    public bool FailOnFileNotFound { get; set; } = true;
    public bool FailOnSchedulingError { get; set; }

    public ValueTask FileUpdated(string fName, CancellationToken cancellationToken = default)
    {
        return started ? new ValueTask(ProcessFile(fName, cancellationToken)) : ValueTask.CompletedTask;
    }

    public async ValueTask Initialize(string pluginName, IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        Name = pluginName;
        Scheduler = scheduler;

        logger.LogInformation("Registering Quartz JSON Job Initialization Plug-in");

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

                        var trig = new SimpleTriggerImpl();
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

                        await Scheduler.ScheduleJob(job, trig, cancellationToken).ConfigureAwait(false);
                        logger.LogDebug("Scheduled file scan job for data file: {FileName}, at interval: {ScanInterval}", jobFile.FileName, ScanInterval);
                    }

                    await ProcessFile(jobFile, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (SchedulerException ex)
        {
            if (FailOnSchedulingError) throw;
            logger.LogError(ex, "Error starting background-task for watching JSON jobs file");
        }
        finally
        {
            started = true;
        }
    }

    public ValueTask Shutdown(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

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

    private async Task ProcessFile(JobFile? jobFile, CancellationToken cancellationToken = default)
    {
        if (jobFile is null || !jobFile.FileFound) return;

        try
        {
            var processor = new JsonSchedulingDataProcessor(
                LogProvider.CreateLogger<JsonSchedulingDataProcessor>(), TypeLoadHelper, timeProvider);

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

            logger.LogError(e, "Could not schedule jobs and triggers from JSON file {FileName}", jobFile.FileName);

            foreach (var listener in Scheduler.ListenerManager.GetSchedulerListeners())
            {
                try
                {
                    await listener.SchedulerError(message, schedulerException, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error while notifying SchedulerListener of error");
                }
            }

            if (FailOnSchedulingError) throw schedulerException;
        }
    }

    private Task ProcessFile(string filePath, CancellationToken cancellationToken = default)
    {
        var idx = jobFiles.FindIndex(pair => pair.Key == filePath);
        return ProcessFile(idx >= 0 ? jobFiles[idx].Value : null, cancellationToken);
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
                    else plugin.logger.LogWarning("File named '{FileName}' does not exist", FileName);
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
                try { f?.Dispose(); }
                catch (IOException ioe) { plugin.logger.LogWarning(ioe, "Error closing jobs file {FileName}", FileName); }
            }

            return Task.CompletedTask;
        }
    }
}
