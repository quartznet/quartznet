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
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.Impl.Matchers;
using Quartz.Logging;
using Quartz.Simpl;

namespace Quartz.Tests.Unit.Extensions.DependencyInjection;

/// <summary>
/// A job or trigger registered through the container with no <c>WithIdentity</c> is stored under a key
/// generated afresh on every start, so a persistent store keeps one more copy each time and every copy
/// of a trigger fires. The scheduler says so as it applies the registrations (#4095).
/// </summary>
/// <remarks>
/// <para>
/// The store is in-memory and merely reports itself persistent, which is all the check reads; no
/// database is needed. The scheduler is created and never started, because the registrations are
/// applied when it is created.
/// </para>
/// <para>
/// Non-parallelizable because the container hands the process-wide <see cref="LogProvider" /> its
/// logger factory.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class GeneratedIdentityWarningTest
{
    private RecordingLoggerProvider recorder;

    [SetUp]
    public void RecordWarnings()
    {
        recorder = new RecordingLoggerProvider();
    }

    [TearDown]
    public void ForgetTheContainersLogProvider()
    {
        LogProvider.SetCurrentLogProvider(null);
    }

    [Test]
    public async Task AJobRegisteredWithoutAnIdentityWarnsWhenTheStorePersists()
    {
        IReadOnlyList<JobKey> stored = await Apply(persistent: true, q => q.AddJob<ReportJob>(j => j.StoreDurably()));

        string warning = GeneratedIdentityWarnings().Should().ContainSingle(
            "one registration without an identity is one warning").Which;
        warning.Should().StartWith($"AddJob<{typeof(ReportJob).FullName}> gave its job no identity",
            "the registration is named by its call and its job type, which is how the reader finds it in code");
        warning.Should().Contain($"'{stored.Single()}'", "the generated key is the one in the store")
            .And.Contain("each start adds another copy")
            .And.Contain("WithIdentity(...)");
    }

    [Test]
    public async Task AJobGivenAnIdentityDoesNotWarn()
    {
        await Apply(persistent: true, q => q.AddJob<ReportJob>(j => j.WithIdentity("reports").StoreDurably()));

        GeneratedIdentityWarnings().Should().BeEmpty("a chosen key is the same on every start, so the store replaces rather than adds");
    }

    [Test]
    public async Task TheInMemoryStoreDoesNotWarn()
    {
        await Apply(persistent: false, q => q.AddJob<ReportJob>(j => j.StoreDurably()));

        GeneratedIdentityWarnings().Should().BeEmpty("the in-memory store starts empty, so a generated key leaves nothing behind");
    }

    [Test]
    public async Task AJobNamedWithAGuidOnPurposeDoesNotWarn()
    {
        string name = Guid.NewGuid().ToString();

        await Apply(persistent: true, q => q.AddJob<ReportJob>(j => j.WithIdentity(name).StoreDurably()));

        GeneratedIdentityWarnings().Should().BeEmpty(
            "the warning is about the registration having given no identity, not about what the key looks like");
    }

    [Test]
    public async Task AScheduledJobWithoutAnIdentityWarnsOnceForItsTrigger()
    {
        await Apply(persistent: true, q => q.ScheduleJob<ReportJob>(t => t.StartAt(DateTimeOffset.UtcNow.AddHours(1))));

        GeneratedIdentityWarnings().Should().ContainSingle(
                "the job takes the trigger's generated key, so the two are one registration and one warning")
            .Which.Should().StartWith($"ScheduleJob<{typeof(ReportJob).FullName}> gave its trigger no identity");
    }

    [Test]
    public async Task AScheduledJobWhoseTriggerHasAnIdentityDoesNotWarn()
    {
        await Apply(persistent: true, q => q.ScheduleJob<ReportJob>(t => t
            .WithIdentity("hourly-reports")
            .StartAt(DateTimeOffset.UtcNow.AddHours(1))));

        GeneratedIdentityWarnings().Should().BeEmpty("the job takes the trigger's chosen key, so neither is generated");
    }

    [Test]
    public async Task ATriggerAddedWithoutAnIdentityNamesItsJob()
    {
        await Apply(persistent: true, q =>
        {
            q.AddJob<ReportJob>(j => j.WithIdentity("reports").StoreDurably());
            q.AddTrigger(t => t.ForJob("reports").StartAt(DateTimeOffset.UtcNow.AddHours(1)));
        });

        GeneratedIdentityWarnings().Should().ContainSingle()
            .Which.Should().StartWith("AddTrigger for job 'DEFAULT.reports' gave its trigger no identity",
                "a trigger added by key names no job type, so the job it fires stands in for one");
    }

    [Test]
    public async Task AJobAndTriggerAddedThroughTheOptionsWithoutAnIdentityWarnEach()
    {
        await Apply(
            persistent: true,
            configure: _ => { },
            configureOptions: options =>
            {
                options.AddJob<ReportJob>(j => j.StoreDurably());
                options.AddJob(typeof(ReportJob), j => j.WithIdentity("reports").StoreDurably());
                options.AddTrigger(t => t.ForJob("reports").StartAt(DateTimeOffset.UtcNow.AddHours(1)));
            });

        string[] warnings = GeneratedIdentityWarnings();
        warnings.Should().HaveCount(2, "the unnamed job and the unnamed trigger; the named job is neither");
        warnings.Should().Contain(x => x.StartsWith($"AddJob<{typeof(ReportJob).FullName}> gave its job no identity", StringComparison.Ordinal));
        warnings.Should().Contain(x => x.StartsWith("AddTrigger for job 'DEFAULT.reports' gave its trigger no identity", StringComparison.Ordinal));
    }

    private string[] GeneratedIdentityWarnings()
    {
        return recorder.Warnings.Where(x => x.Contains("no identity, so it was stored under the generated key")).ToArray();
    }

    private async Task<IReadOnlyList<JobKey>> Apply(
        bool persistent,
        Action<IServiceCollectionQuartzConfigurator> configure,
        Action<QuartzOptions> configureOptions = null)
    {
        ServiceCollection services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(recorder));
        services.AddQuartz(q =>
        {
            q.SchedulerName = "GeneratedIdentity_" + Guid.NewGuid().ToString("N");
            if (persistent)
            {
                q.SetProperty("quartz.jobStore.type", typeof(PersistentRamJobStore).AssemblyQualifiedName);
                q.SetProperty("quartz.serializer.type", TestConstants.DefaultSerializerType);
            }
            else
            {
                q.UseInMemoryStore();
            }

            configure(q);
        });

        if (configureOptions is not null)
        {
            services.Configure(configureOptions);
        }

        await using ServiceProvider provider = services.BuildServiceProvider();
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        try
        {
            return (await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup())).ToList();
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    /// <summary>
    /// An in-memory store that answers <see cref="RAMJobStore.SupportsPersistence" /> the way a database
    /// store does.
    /// </summary>
    public sealed class PersistentRamJobStore : RAMJobStore
    {
        public override bool SupportsPersistence => true;
    }

    private sealed class ReportJob : IJob
    {
        public Task Execute(IJobExecutionContext context)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Collects what was logged at warning level or above.
    /// </summary>
    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> warnings = new List<string>();

        public IReadOnlyList<string> Warnings
        {
            get
            {
                lock (warnings)
                {
                    return warnings.ToArray();
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(this);

        public void Dispose()
        {
        }

        private sealed class RecordingLogger : ILogger
        {
            private readonly RecordingLoggerProvider provider;

            public RecordingLogger(RecordingLoggerProvider provider)
            {
                this.provider = provider;
            }

            public IDisposable BeginScope<TState>(TState state) => null;

            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

            public void Log<TState>(
                Microsoft.Extensions.Logging.LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception exception,
                Func<TState, Exception, string> formatter)
            {
                if (logLevel >= Microsoft.Extensions.Logging.LogLevel.Warning)
                {
                    lock (provider.warnings)
                    {
                        provider.warnings.Add(formatter(state, exception));
                    }
                }
            }
        }
    }
}
