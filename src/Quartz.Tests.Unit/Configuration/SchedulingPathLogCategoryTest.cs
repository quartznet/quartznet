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

#nullable enable

using System.Collections.Concurrent;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Jobs;
using Quartz.Plugins.Json;
using Quartz.Xml;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// Three ways of declaring jobs and triggers share one implementation, and each logs under its own
/// category.
/// </summary>
/// <remarks>
/// They used to share a category too: <c>ContainerConfigurationProcessor</c> and
/// <c>JsonSchedulingDataProcessor</c> both derive from <c>XmlSchedulingDataProcessor</c> and both handed
/// it an <c>ILogger&lt;XmlSchedulingDataProcessor&gt;</c>, so "Adding 2 jobs, 2 triggers" arrived under
/// <c>Quartz.Xml.XmlSchedulingDataProcessor</c> whether a file, a JSON file or <c>AddQuartz</c> put them
/// there — and a log filter could not tell one path from another, or silence one of them. The event ids
/// are deliberately the same on all three: it is the same event, from a different source.
/// </remarks>
public class SchedulingPathLogCategoryTest
{
    private const int AddingJobsAndTriggersEventId = 5018;

    [Test]
    public async Task TheContainerPathLogsUnderItsOwnCategory()
    {
        CategoryRecordingLoggerProvider recorder = new();

        ServiceCollection services = new();
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(recorder);
        });

        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "log-category");
            q.AddJob<NoOpJob>(job => job.WithIdentity("declared").StoreDurably());
        });

        await using ServiceProvider container = services.BuildServiceProvider();
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Shutdown(waitForJobsToComplete: false);

        CategoryOf(recorder, AddingJobsAndTriggersEventId).Should().Be(
            "Quartz.Configuration.ContainerConfigurationProcessor",
            "what AddQuartz declared is the container path, and a log filter has to be able to say so");
    }

    [Test]
    public async Task TheXmlPathLogsUnderItsOwnCategory()
    {
        CategoryRecordingLoggerProvider recorder = new();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(recorder);
        });

        XmlSchedulingDataProcessor processor = new(
            loggerFactory.CreateLogger<XmlSchedulingDataProcessor>(),
            new SimpleTypeLoader(),
            TimeProvider.System);

        await processor.ProcessStream(ToStream(XmlDocument), systemId: null);
        await ScheduleInto(processor);

        CategoryOf(recorder, AddingJobsAndTriggersEventId).Should().Be("Quartz.Xml.XmlSchedulingDataProcessor");
    }

    [Test]
    public async Task TheJsonPathLogsUnderItsOwnCategory()
    {
        CategoryRecordingLoggerProvider recorder = new();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(recorder);
        });

        JsonSchedulingDataProcessor processor = new(
            loggerFactory.CreateLogger<JsonSchedulingDataProcessor>(),
            new SimpleTypeLoader(),
            TimeProvider.System);

        processor.ProcessJsonContent(JsonDocument);
        await ScheduleInto(processor);

        CategoryOf(recorder, AddingJobsAndTriggersEventId).Should().Be(
            "Quartz.Plugins.Json.JsonSchedulingDataProcessor");
    }

    private static async Task ScheduleInto(XmlSchedulingDataProcessor processor)
    {
        IScheduler scheduler = await QuartzSchedulerBuilder.Create(q =>
            q.ConfigureScheduler(options => options.InstanceName = "log-category-" + Guid.NewGuid().ToString("N")))
            .BuildScheduler();

        try
        {
            await processor.ScheduleJobs(scheduler);
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    private static string CategoryOf(CategoryRecordingLoggerProvider recorder, int eventId)
    {
        (string Category, EventId EventId) entry = recorder.Entries.FirstOrDefault(x => x.EventId.Id == eventId);
        entry.Category.Should().NotBeNull($"event {eventId} has to have been logged for its category to be asserted on");
        return entry.Category;
    }

    private static Stream ToStream(string content) => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

    private const string XmlDocument = """
        <?xml version="1.0" encoding="UTF-8"?>
        <job-scheduling-data xmlns="http://quartznet.sourceforge.net/JobSchedulingData" version="2.0">
          <schedule>
            <job>
              <name>xml-job</name>
              <job-type>Quartz.Jobs.NoOpJob, Quartz.Jobs</job-type>
              <durable>true</durable>
            </job>
          </schedule>
        </job-scheduling-data>
        """;

    private const string JsonDocument = """
        {
          "Schedule": {
            "Jobs": [
              { "Name": "json-job", "JobType": "Quartz.Jobs.NoOpJob, Quartz.Jobs", "Durable": true }
            ]
          }
        }
        """;

    /// <summary>
    /// Records the category each entry was written under, which is the whole of what these tests are
    /// about and what the shared <c>RecordingLoggerProvider</c> deliberately throws away.
    /// </summary>
    private sealed class CategoryRecordingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<(string Category, EventId EventId)> entries = new();

        public List<(string Category, EventId EventId)> Entries => entries.ToList();

        public ILogger CreateLogger(string categoryName) => new CategoryRecordingLogger(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class CategoryRecordingLogger(CategoryRecordingLoggerProvider provider, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                provider.entries.Enqueue((category, eventId));
            }
        }
    }
}
