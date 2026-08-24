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
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.  See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

using System.Net;

using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// Disposing an <see cref="IScheduler" /> releases what that instance owns, which is a different thing
/// for a scheduler than for a handle to one.
/// </summary>
[NonParallelizable]
public sealed class SchedulerDisposalTest
{
    [Test]
    public async Task DisposingALocalSchedulerShutsItDown()
    {
        IScheduler captured;

        await using (ServiceProvider provider = BuildContainer("DisposedLocalScheduler"))
        {
            IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
            await scheduler.Start();

            await using (scheduler)
            {
                scheduler.Status.Should().Be(SchedulerStatus.Running);
                captured = scheduler;
            }

            captured.Status.Should().Be(SchedulerStatus.Shutdown,
                "a local scheduler owns the execution it drives, so disposing it stops it");
        }
    }

    [Test]
    public async Task DisposingTwiceIsSafe()
    {
        await using ServiceProvider provider = BuildContainer("TwiceDisposedScheduler");
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();

        await scheduler.DisposeAsync();

        Func<Task> again = async () => await scheduler.DisposeAsync();

        await again.Should().NotThrowAsync("disposal is idempotent, which is what await using needs it to be");
        scheduler.Status.Should().Be(SchedulerStatus.Shutdown);
    }

    [Test]
    public async Task DisposingAfterAWaitingShutdownDoesNothing()
    {
        await using ServiceProvider provider = BuildContainer("AlreadyShutDownScheduler");
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();

        // The graceful drain is an explicit call; disposal is not a second, contradicting one.
        await scheduler.Shutdown(waitForJobsToComplete: true);

        Func<Task> dispose = async () => await scheduler.DisposeAsync();

        await dispose.Should().NotThrowAsync();
    }

    [Test]
    public async Task DisposingAHandleThatNeverBuiltASchedulerBuildsNothing()
    {
        ISchedulerFactory factory = A.Fake<ISchedulerFactory>();
        DeferredScheduler handle = new DeferredScheduler(
            factory,
            OptionsFor(new QuartzSchedulerOptions()),
            new SchedulerKey("never-used"));

        await handle.DisposeAsync();

        // Building a scheduler on the way out of a container that never used one would start it - and
        // under a persistent store open a database connection - only to shut it straight back down.
        A.CallTo(() => factory.GetScheduler(A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task DisposingAHandleThatBuiltASchedulerDisposesThatScheduler()
    {
        IScheduler built = A.Fake<IScheduler>();
        ISchedulerFactory factory = A.Fake<ISchedulerFactory>();
        A.CallTo(() => factory.GetScheduler(A<CancellationToken>._)).Returns(new ValueTask<IScheduler>(built));

        DeferredScheduler handle = new DeferredScheduler(
            factory,
            OptionsFor(new QuartzSchedulerOptions()),
            new SchedulerKey("used"));

        await handle.Start();
        await handle.DisposeAsync();

        A.CallTo(() => built.DisposeAsync()).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task ADecoratorForwardsDisposal()
    {
        IScheduler inner = A.Fake<IScheduler>();

        await new DelegatingScheduler(inner).DisposeAsync();

        A.CallTo(() => inner.DisposeAsync()).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task DisposingARemoteSchedulerDoesNotShutTheRemoteSchedulerDown()
    {
        RecordingHandler handler = new RecordingHandler();
        using HttpClient httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        HttpScheduler scheduler = new HttpScheduler("remote", httpClient);

        await scheduler.DisposeAsync();

        handler.Requests.Should().BeEmpty(
            "a client going away is not an instruction to stop scheduling for everybody else");
    }

    [Test]
    public async Task ARemoteSchedulerStillShutsDownWhenAskedTo()
    {
        RecordingHandler handler = new RecordingHandler();
        using HttpClient httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        HttpScheduler scheduler = new HttpScheduler("remote", httpClient);

        await scheduler.Shutdown();

        handler.Requests.Should().ContainSingle().Which.Should().Contain("/shutdown",
            "the deliberate call is how a remote scheduler is stopped, and it still works");
    }

    private static ServiceProvider BuildContainer(string schedulerName)
    {
        ServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = schedulerName);
            q.UseInMemoryStore();
        });

        return services.BuildServiceProvider();
    }

    private static IOptionsMonitor<QuartzSchedulerOptions> OptionsFor(QuartzSchedulerOptions options)
    {
        IOptionsMonitor<QuartzSchedulerOptions> monitor = A.Fake<IOptionsMonitor<QuartzSchedulerOptions>>();
        A.CallTo(() => monitor.Get(A<string>._)).Returns(options);
        return monitor;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("")
            });
        }
    }
}
