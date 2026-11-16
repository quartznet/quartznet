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

using FakeItEasy;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Configuration;
using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// The factory builds the container's own scheduler, whatever proxy is bound under the same name and
/// whenever it was bound (#4322).
/// </summary>
/// <remarks>
/// A dashboard that accepts agents binds each agent's scheduler into the container's repository under the
/// scheduler's name, and a process that shares a store with its workers configures that same name. The
/// factory used to answer with whatever the repository held under the name, so a process whose agent
/// connected before anything asked for the scheduler was handed the agent's proxy, and its own scheduler
/// was never built.
/// </remarks>
[NonParallelizable]
public sealed class DefaultSchedulerFactoryProxyTest
{
    private const string SharedName = "Shared";

    private readonly List<IScheduler> built = [];

    [TearDown]
    public async Task ShutDownWhateverIsLeft()
    {
        foreach (IScheduler scheduler in built)
        {
            try
            {
                await scheduler.Shutdown();
            }
            catch (SchedulerException)
            {
                // Already shut down by the test itself.
            }
        }

        built.Clear();
    }

    [TestCase("worker-1", TestName = "GetSchedulerBuildsItsOwnBesideATargetedProxyBoundFirst")]
    [TestCase(null, TestName = "GetSchedulerBuildsItsOwnBesideAnUntargetedProxyBoundFirst")]
    public async Task GetSchedulerBuildsItsOwnBesideAProxyBoundFirst(string? target)
    {
        await using ServiceProvider provider = Container(services =>
            services.AddQuartz(q => q.ConfigureScheduler(o => o.InstanceName = SharedName)));

        ISchedulerRepository repository = provider.GetRequiredService<ISchedulerRepository>();
        IScheduler proxy = Proxy(SharedName, target);
        repository.Bind(proxy);

        ISchedulerFactory factory = provider.GetRequiredService<ISchedulerFactory>();
        IScheduler own = await Track(await factory.GetScheduler());

        own.Should().NotBeSameAs(proxy, "a proxy to another process is never what this container's factory was asked to build");
        own.Should().NotBeAssignableTo<IProxyScheduler>();
        own.SchedulerName.Should().Be(SharedName);

        (await factory.GetScheduler()).Should().BeSameAs(own, "the second call finds the scheduler the first one bound");

        repository.LookupByName(SharedName).Should().HaveCount(2, "the factory's scheduler is bound beside the proxy, not in place of it");
        repository.Lookup(SharedName, target ?? SharedName).Should().BeSameAs(proxy, "the proxy stays resolvable under its own instance id");
        repository.Lookup(SharedName, own.SchedulerInstanceId).Should().BeSameAs(own);
    }

    [Test]
    public async Task LookupSchedulerByTheOwnNameReturnsTheOwnSchedulerBesideAProxyBoundFirst()
    {
        await using ServiceProvider provider = Container(services =>
            services.AddQuartz(q => q.ConfigureScheduler(o => o.InstanceName = SharedName)));

        IScheduler proxy = Proxy(SharedName, "worker-1");
        provider.GetRequiredService<ISchedulerRepository>().Bind(proxy);

        ISchedulerFactory factory = provider.GetRequiredService<ISchedulerFactory>();
        IScheduler? byName = await factory.LookupScheduler(SharedName);
        await Track(byName);

        byName.Should().NotBeNull();
        byName.Should().NotBeSameAs(proxy);
        byName.Should().BeSameAs(await factory.GetScheduler());
    }

    [Test]
    public async Task AProxyBoundAfterTheOwnSchedulerDoesNotDisplaceIt()
    {
        await using ServiceProvider provider = Container(services =>
            services.AddQuartz(q => q.ConfigureScheduler(o => o.InstanceName = SharedName)));

        ISchedulerFactory factory = provider.GetRequiredService<ISchedulerFactory>();
        IScheduler own = await Track(await factory.GetScheduler());

        ISchedulerRepository repository = provider.GetRequiredService<ISchedulerRepository>();
        IScheduler proxy = Proxy(SharedName, "worker-1");
        Action bind = () => repository.Bind(proxy);
        bind.Should().NotThrow("the proxy is bound under its target, which tells it apart from the local scheduler");

        (await factory.GetScheduler()).Should().BeSameAs(own);
        (await factory.LookupScheduler(SharedName)).Should().BeSameAs(own);
        repository.Lookup(SharedName, "worker-1").Should().BeSameAs(proxy);
        repository.Lookup(SharedName, own.SchedulerInstanceId).Should().BeSameAs(own);
    }

    [Test]
    public async Task LookupSchedulerBuildsARegisteredSiblingBesideAProxyBoundUnderItsName()
    {
        await using ServiceProvider provider = Container(services =>
        {
            services.AddQuartz("acme", _ => { });
            services.AddQuartz("initech", _ => { });
        });

        IScheduler proxy = Proxy("initech", "worker-1");
        provider.GetRequiredService<ISchedulerRepository>().Bind(proxy);

        ISchedulerFactory acme = provider.GetRequiredKeyedService<ISchedulerFactory>("acme");
        IScheduler? initech = await acme.LookupScheduler("initech");
        await Track(initech);

        initech.Should().NotBeNull();
        initech.Should().NotBeSameAs(proxy, "the registration says this container builds initech");
        initech.Should().BeSameAs(await provider.GetRequiredKeyedService<ISchedulerFactory>("initech").GetScheduler());
    }

    [Test]
    public async Task LookupSchedulerStillAnswersWithAProxyForANameNothingHereRegistered()
    {
        await using ServiceProvider provider = Container(services => services.AddQuartz("acme", _ => { }));

        IScheduler proxy = Proxy("remote", target: null);
        provider.GetRequiredService<ISchedulerRepository>().Bind(proxy);

        (await provider.GetRequiredKeyedService<ISchedulerFactory>("acme").LookupScheduler("remote")).Should().BeSameAs(proxy,
            "a lookup by name resolves a proxy on purpose when this container has no scheduler of that name");
    }

    [Test]
    public async Task RestartOfARegisteredSchedulerReplacesTheOwnSchedulerAndLeavesTheProxyAlone()
    {
        await using ServiceProvider provider = Container(services => services.AddQuartz("acme", _ => { }));

        ISchedulerRepository repository = provider.GetRequiredService<ISchedulerRepository>();
        IScheduler proxy = Proxy("acme", "worker-1");
        repository.Bind(proxy);

        IScheduler first = await Track(await provider.GetRequiredKeyedService<ISchedulerFactory>("acme").GetScheduler());
        first.Should().NotBeSameAs(proxy);

        IScheduler second = await Track(await provider.GetRequiredService<ISchedulerRuntime>().Restart("acme"));

        first.Status.Should().Be(SchedulerStatus.Shutdown, "the incumbent a restart replaces is the scheduler this container built, not the proxy");
        A.CallTo(() => ((DelegatingScheduler) proxy).InnerScheduler.Shutdown(A<bool>._, A<CancellationToken>._)).MustNotHaveHappened();
        repository.Lookup("acme", "worker-1").Should().BeSameAs(proxy);
        repository.LookupLocal("acme").Should().BeSameAs(second);
    }

    private Task<IScheduler> Track(IScheduler? scheduler)
    {
        if (scheduler is not null && scheduler is not IProxyScheduler)
        {
            built.Add(scheduler);
        }

        return Task.FromResult(scheduler!);
    }

    private static IScheduler Proxy(string name, string? target)
    {
        IScheduler inner = A.Fake<IScheduler>();
        A.CallTo(() => inner.SchedulerName).Returns(name);
        A.CallTo(() => inner.SchedulerInstanceId).Returns(target ?? name);
        A.CallTo(() => inner.Status).Returns(SchedulerStatus.Running);
        return new TestProxy(inner, target);
    }

    private static ServiceProvider Container(Action<IServiceCollection> configure)
    {
        ServiceCollection services = new();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        configure(services);
        return services.BuildServiceProvider();
    }

    private sealed class TestProxy : DelegatingScheduler, IProxyScheduler
    {
        public TestProxy(IScheduler inner, string? target) : base(inner)
        {
            Target = target;
        }

        public string? Target { get; }

        public SchedulerOrigin Origin => SchedulerOrigin.Agent;
    }
}
