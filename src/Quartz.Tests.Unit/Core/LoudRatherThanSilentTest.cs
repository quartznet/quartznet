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

using System.Diagnostics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;
using Quartz.Tests.Unit.Plugin.History;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// Two things a scheduler used to do in silence, and now says out loud. Neither changes what it does.
/// </summary>
[NonParallelizable]
public sealed class LoudRatherThanSilentTest
{
    private const int ShuttingDownWithJobsStillExecutingEventId = 1020;
    private const int LegacyDiagnosticListenerSubscribedEventId = 1021;

    /// <summary>
    /// A shutdown that abandons running work says how much of it there was, and names the two settings
    /// that decide whether it does.
    /// </summary>
    /// <remarks>
    /// The default is 3.x's and stays — a host stopping does not wait for a job by default. But "the
    /// process stopped and four jobs were half-done" is not something to find out from the absence of a
    /// completion, and nothing said it at all.
    /// </remarks>
    [Test]
    public async Task ShuttingDownWithoutWaitingSaysHowMuchWorkWasAbandoned()
    {
        RecordingLoggerProvider recorder = new();
        BlockingJob.Reset(expected: 1);

        ServiceCollection services = new();
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(recorder);
        });
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "abandoned-work");
            q.ScheduleJob<BlockingJob>(trigger => trigger.WithIdentity("now").StartNow());
        });

        await using ServiceProvider container = services.BuildServiceProvider();
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();

        BlockingJob.Running.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue("the job has to be executing before the shutdown");

        await scheduler.Shutdown(waitForJobsToComplete: false);

        LogEntry? entry = recorder.Entries.FirstOrDefault(x => x.EventId.Id == ShuttingDownWithJobsStillExecutingEventId);

        entry.Should().NotBeNull();
        entry!.Level.Should().Be(LogLevel.Warning, "work was abandoned, which is not an informational event");
        entry.Message.Should().Contain("1 job(s) still executing")
            .And.Contain("waitForJobsToComplete")
            .And.Contain("ShutdownJobInterruption",
                "an operator reading this has to be told which two settings decide it");
    }

    [Test]
    public async Task ShuttingDownWithNothingRunningSaysNothing()
    {
        RecordingLoggerProvider recorder = new();

        ServiceCollection services = new();
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(recorder);
        });
        services.AddQuartz(q => q.ConfigureScheduler(options => options.InstanceName = "nothing-running"));

        await using ServiceProvider container = services.BuildServiceProvider();
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();
        await scheduler.Shutdown(waitForJobsToComplete: false);

        recorder.Entries.Should().NotContain(x => x.EventId.Id == ShuttingDownWithJobsStillExecutingEventId,
            "an ordinary shutdown is not a warning");
    }

    /// <summary>
    /// A subscriber attached where 3.x published and 4.x does not is told so, once per start.
    /// </summary>
    /// <remarks>
    /// <c>OpenTelemetry.Instrumentation.Quartz</c> subscribes to a <see cref="DiagnosticListener" />
    /// named <c>Quartz</c> and to two legacy activity sources, none of which 4.x produces — so an
    /// upgraded application that kept <c>AddQuartzInstrumentation()</c> simply stops seeing its job
    /// spans, with nothing thrown, nothing logged and the call still compiling.
    /// </remarks>
    [Test]
    public async Task ASubscriberToTheListenerThreeXPublishedOnIsToldItWillGetNothing()
    {
        RecordingLoggerProvider recorder = new();
        using IDisposable subscription = SubscribeToTheLegacyListener();

        ServiceCollection services = new();
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(recorder);
        });
        services.AddQuartz(q => q.ConfigureScheduler(options => options.InstanceName = "legacy-telemetry"));

        await using ServiceProvider container = services.BuildServiceProvider();
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();

        try
        {
            LogEntry? entry = recorder.Entries.FirstOrDefault(x => x.EventId.Id == LegacyDiagnosticListenerSubscribedEventId);

            entry.Should().NotBeNull("something is subscribed where nothing will ever be written");
            entry!.Level.Should().Be(LogLevel.Warning);
            entry.Message.Should().Contain("DiagnosticListener")
                .And.Contain(QuartzInstrumentation.ActivitySourceName)
                .And.Contain("AddSource",
                    "the message has to say what to do instead, not only that something is wrong");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Test]
    public async Task NothingIsSaidWhenNobodyIsSubscribed()
    {
        RecordingLoggerProvider recorder = new();

        ServiceCollection services = new();
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(recorder);
        });
        services.AddQuartz(q => q.ConfigureScheduler(options => options.InstanceName = "current-telemetry"));

        await using ServiceProvider container = services.BuildServiceProvider();
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();
        await scheduler.Shutdown(waitForJobsToComplete: false);

        recorder.Entries.Should().NotContain(x => x.EventId.Id == LegacyDiagnosticListenerSubscribedEventId,
            "an application subscribing the 4.x way hears nothing about it");
    }

    /// <summary>
    /// What the community package does, reduced to the part this detection sees: subscribe to the
    /// listener named <c>Quartz</c> if one is ever published.
    /// </summary>
    private static IDisposable SubscribeToTheLegacyListener()
    {
        List<IDisposable> subscriptions = [];

        IDisposable all = DiagnosticListener.AllListeners.Subscribe(new ListenerObserver(listener =>
        {
            if (listener.Name == QuartzInstrumentation.ActivitySourceName)
            {
                subscriptions.Add(listener.Subscribe(new EventObserver()));
            }
        }));

        subscriptions.Add(all);

        return new Subscriptions(subscriptions);
    }

    private sealed class Subscriptions(List<IDisposable> subscriptions) : IDisposable
    {
        public void Dispose()
        {
            foreach (IDisposable subscription in subscriptions)
            {
                subscription.Dispose();
            }
        }
    }

    private sealed class ListenerObserver(Action<DiagnosticListener> onNext) : IObserver<DiagnosticListener>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(DiagnosticListener value) => onNext(value);
    }

    private sealed class EventObserver : IObserver<KeyValuePair<string, object?>>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
        }
    }

    private sealed class BlockingJob : IJob
    {
        public static CountdownEvent Running { get; private set; } = new(1);

        public static void Reset(int expected)
        {
            Running.Dispose();
            Running = new CountdownEvent(expected);
        }

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            CountdownEvent running = Running;
            if (!running.IsSet)
            {
                running.Signal();
            }

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
