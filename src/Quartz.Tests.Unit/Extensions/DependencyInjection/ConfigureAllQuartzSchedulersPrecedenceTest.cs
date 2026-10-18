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

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.Configuration;
using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Extensions.DependencyInjection;

/// <summary>
/// Who wins a scheduler's part when <c>ConfigureAllQuartzSchedulers</c> chooses one too (#4104).
/// </summary>
/// <remarks>
/// <para>
/// A part chosen container-wide replaces a default Quartz filled in, and never a part the scheduler chose
/// for itself — in its callback, its configuration or a key. Both halves hold whichever of
/// <c>AddQuartz</c> and <c>ConfigureAllQuartzSchedulers</c> was called first, because a library cannot
/// know which order the application writes them in.
/// </para>
/// <para>
/// Most cases run in both orders, and the order that used to fail is the one where
/// <c>ConfigureAllQuartzSchedulers</c> comes second: by then the scheduler's registration had already
/// filled the slot with its default, and the container-wide choice was a <c>TryAdd</c> that did nothing.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class ConfigureAllQuartzSchedulersPrecedenceTest
{
    private const string Named = "Named";

    private const string RamJobStoreKey = "Quartz.Impl.RAMJobStore, Quartz";

    private const string DefaultThreadPoolKey = "Quartz.Impl.DefaultThreadPool, Quartz";

    [Test]
    public void UsePersistentStoreReplacesTheDefaultStoreAndItsCompanions(
        [Values] bool configureAllFirst,
        [Values(null, Named)] string? schedulerName)
    {
        IObjectSerializer serializer = A.Fake<IObjectSerializer>();

        using ServiceProvider provider = Container(services => InEitherOrder(
            services,
            configureAllFirst,
            register: () => Register(services, schedulerName),
            configureAll: () => services.ConfigureAllQuartzSchedulers(q => q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, "Data Source=:memory:");
                store.UseSerializer(_ => serializer);
            }))));

        provider.GetScheduler<IJobStore>(schedulerName).Should().BeOfType<LocalTransactionJobStore>(
            "a store chosen for every scheduler replaces the in-memory default whichever call came first");
        provider.GetScheduler<IDriverDelegate>(schedulerName).Should().BeOfType<SQLiteDelegate>(
            "the dialect UseSqlite chose replaces the generic delegate the scheduler's registration filled in");
        provider.GetScheduler<IObjectSerializer>(schedulerName).Should().BeSameAs(serializer,
            "the serializer is a default too, and a store that writes with the wrong one corrupts its rows");
    }

    [Test]
    public void UseJobStoreReplacesTheDefaultStore(
        [Values] bool configureAllFirst,
        [Values(null, Named)] string? schedulerName)
    {
        using ServiceProvider provider = Container(services => InEitherOrder(
            services,
            configureAllFirst,
            register: () => Register(services, schedulerName),
            configureAll: () => services.ConfigureAllQuartzSchedulers(q => q.UseJobStore<MarkedJobStore>())));

        provider.GetScheduler<IJobStore>(schedulerName).Should().BeOfType<MarkedJobStore>();
    }

    [Test]
    public void UseThreadPoolReplacesTheDefaultPool(
        [Values] bool configureAllFirst,
        [Values(null, Named)] string? schedulerName)
    {
        using ServiceProvider provider = Container(services => InEitherOrder(
            services,
            configureAllFirst,
            register: () => Register(services, schedulerName),
            configureAll: () => services.ConfigureAllQuartzSchedulers(q => q.UseThreadPool<MarkedThreadPool>())));

        provider.GetScheduler<IThreadPool>(schedulerName).Should().BeOfType<MarkedThreadPool>(
            "a library that sizes every scheduler's pool cannot know whether it runs before AddQuartz");
    }

    [Test]
    public void UseJobFactoryReplacesTheDefaultFactory(
        [Values] bool configureAllFirst,
        [Values(null, Named)] string? schedulerName)
    {
        using ServiceProvider provider = Container(services => InEitherOrder(
            services,
            configureAllFirst,
            register: () => Register(services, schedulerName),
            configureAll: () => services.ConfigureAllQuartzSchedulers(q => q.UseJobFactory<RecordingJobFactory>())));

        provider.GetScheduler<IJobFactory>(schedulerName).Should().BeOfType<RecordingJobFactory>();
    }

    /// <summary>
    /// A part handed over as an object replaces the default as well, and the note a restart reads says so.
    /// </summary>
    /// <remarks>
    /// The note is what refuses to replay a recipe that would hand the next generation the running one's
    /// object. It describes the registration that took, so a replacement must leave one, or a restart
    /// would hand the instance over after all.
    /// </remarks>
    [Test]
    public void AnInstanceReplacesTheDefaultAndIsNoted([Values] bool configureAllFirst)
    {
        MarkedThreadPool pool = new();

        using ServiceProvider provider = Container(services => InEitherOrder(
            services,
            configureAllFirst,
            register: () => services.AddQuartz(Named),
            configureAll: () => services.ConfigureAllQuartzSchedulers(q => q.UseThreadPool(pool))));

        provider.GetRequiredKeyedService<IThreadPool>(Named).Should().BeSameAs(pool);
        provider.GetKeyedServices<SchedulerInstancePart>(Named).Should().ContainSingle()
            .Which.PartType.Should().Be(typeof(IThreadPool),
                "the instance is the registration that took, so a restart must refuse to replay it");
    }

    /// <summary>
    /// The precedence's other half, which the fix must not disturb.
    /// </summary>
    [Test]
    public void AStoreTheSchedulerChoseKeepsItAgainstConfigureAll(
        [Values] bool configureAllFirst,
        [Values(null, Named)] string? schedulerName)
    {
        using ServiceProvider provider = Container(services => InEitherOrder(
            services,
            configureAllFirst,
            register: () => Register(services, schedulerName, q => q.UseInMemoryStore()),
            configureAll: () => services.ConfigureAllQuartzSchedulers(q => q.UsePersistentStore(store =>
                store.UseSqlite(SqliteFactory.Instance, "Data Source=:memory:")))));

        provider.GetScheduler<IJobStore>(schedulerName).Should().BeOfType<RAMJobStore>(
            "UseInMemoryStore() is a choice the scheduler made, not a default, and a container-wide choice "
            + "never replaces one");
    }

    /// <summary>
    /// A key is the scheduler's own configuration, so it beats a container-wide choice in both orders.
    /// </summary>
    /// <remarks>
    /// When <c>ConfigureAllQuartzSchedulers</c> came first it used to be applied before the key-named
    /// registrations, and won; when it came second it lost. It is now applied after them either way.
    /// </remarks>
    [Test]
    public void AStoreAKeyChoseKeepsItAgainstConfigureAll(
        [Values] bool configureAllFirst,
        [Values(null, Named)] string? schedulerName)
    {
        Dictionary<string, string?> properties = new() { ["quartz.jobStore.type"] = RamJobStoreKey };

        using ServiceProvider provider = Container(services => InEitherOrder(
            services,
            configureAllFirst,
            register: () =>
            {
                if (schedulerName is null)
                {
                    services.AddQuartz(properties);
                }
                else
                {
                    services.AddQuartz(schedulerName, properties);
                }
            },
            configureAll: () => services.ConfigureAllQuartzSchedulers(q => q.UseJobStore<MarkedJobStore>())));

        provider.GetScheduler<IJobStore>(schedulerName).Should().BeOfType<RAMJobStore>(
            "the key is the scheduler's own configuration, which a container-wide choice never replaces, "
            + "whichever of the two calls was written first");
    }

    [Test]
    public void AnApplicationRegistrationMadeAfterAddQuartzKeepsItAgainstConfigureAll()
    {
        IJobStore applications = A.Fake<IJobStore>();

        using ServiceProvider provider = Container(services =>
        {
            services.AddQuartz();
            services.AddSingleton(applications);
            services.ConfigureAllQuartzSchedulers(q => q.UseJobStore<MarkedJobStore>());
        });

        provider.GetRequiredService<IJobStore>().Should().BeSameAs(applications,
            "only a default Quartz filled in is replaced, and a registration the application made is not one");
    }

    /// <summary>
    /// The default scheduler's registration is additive, and a second <c>AddQuartz</c> meets the defaults
    /// the first one filled in exactly as a late <c>ConfigureAllQuartzSchedulers</c> does.
    /// </summary>
    [Test]
    public void ASecondAddQuartzReplacesTheDefaultsTheFirstFilledIn()
    {
        using ServiceProvider provider = Container(services =>
        {
            services.AddQuartz();
            services.AddQuartz(q => q.UseJobStore<MarkedJobStore>());
        });

        provider.GetRequiredService<IJobStore>().Should().BeOfType<MarkedJobStore>(
            "the first call chose nothing, so the store the second one chose is the only choice made");
    }

    /// <summary>
    /// A job factory decides whether a scheduler's job types are registered with the container, so
    /// replacing the default one takes back what the default registered.
    /// </summary>
    /// <remarks>
    /// A factory of the application's own builds its jobs its own way, very often from another container,
    /// and a registration of the job type would fail container validation on dependencies only that
    /// factory supplies (#3930). Without this, the order of the two calls would decide that.
    /// </remarks>
    [Test]
    public void AJobFactoryWithdrawsTheJobTypeTheDefaultRegistered([Values] bool configureAllFirst)
    {
        ServiceCollection services = Collection();

        InEitherOrder(
            services,
            configureAllFirst,
            register: () => services.AddQuartz(Named, q => q.AddJob<PlainJob>(job => job.WithIdentity("plain").StoreDurably())),
            configureAll: () => services.ConfigureAllQuartzSchedulers(q => q.UseJobFactory<RecordingJobFactory>()));

        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(PlainJob),
            "the scheduler's factory is its own, so nothing would resolve the registration");
        RegisteredJobTypes.Find(services)!.Registrations(Named).Should().BeEmpty(
            "a job type the container does not build has no constructor for the startup check to read");
    }

    [Test]
    public void AJobTypeAnotherSchedulerStillBuildsFromTheContainerStaysRegistered([Values] bool configureAllFirst)
    {
        ServiceCollection services = Collection();

        InEitherOrder(
            services,
            configureAllFirst,
            register: () =>
            {
                services.AddQuartz("A", q => q.AddJob<PlainJob>(job => job.WithIdentity("plain").StoreDurably()));
                services.AddQuartz("B", q => q.AddJob<PlainJob>(job => job.WithIdentity("plain").StoreDurably()));
            },
            configureAll: () => services.ConfigureAllQuartzSchedulers(q =>
            {
                if (q.SchedulerName == "A")
                {
                    q.UseJobFactory<RecordingJobFactory>();
                }
            }));

        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(PlainJob),
            "scheduler B still builds its jobs from the container, and the registration is one both shared");
        RegisteredJobTypes.Find(services)!.Registrations("A").Should().BeEmpty();
        RegisteredJobTypes.Find(services)!.Registrations("B").Should().ContainSingle();
    }

    /// <summary>
    /// A restart replays the recipe into a container of its own, where the container-wide delegates run
    /// before the defaults. The first generation must have the same parts as every later one.
    /// </summary>
    [Test]
    public async Task ARestartedSchedulerKeepsThePartsItsRegistrationHad([Values] bool configureAllFirst)
    {
        await using ServiceProvider provider = Container(services => InEitherOrder(
            services,
            configureAllFirst,
            register: () => services.AddQuartz(Named),
            configureAll: () => services.ConfigureAllQuartzSchedulers(q => q.UseThreadPool<MarkedThreadPool>())));

        IScheduler first = await provider.GetRequiredKeyedService<ISchedulerFactory>(Named).GetScheduler();
        IScheduler second = first;
        try
        {
            (await first.GetMetadata()).ThreadPoolTypeName.Should().Contain(nameof(MarkedThreadPool),
                "the container's own registration is the first generation");

            second = await provider.GetRequiredService<ISchedulerRuntime>().Restart(Named);

            (await second.GetMetadata()).ThreadPoolTypeName.Should().Contain(nameof(MarkedThreadPool),
                "a restart that changed the pool would be a different scheduler under the same name");
        }
        finally
        {
            await first.Shutdown();
            await second.Shutdown();
        }
    }

    /// <summary>
    /// A runtime tenant is built by the same registration, so it agrees with a container-registered
    /// scheduler given the same recipe — here, a key that a container-wide choice must not beat.
    /// </summary>
    [Test]
    public async Task ARuntimeTenantGetsThePartsAContainerRegistrationGets([Values] bool configureAllFirst)
    {
        Dictionary<string, string?> properties = new() { ["quartz.threadPool.type"] = DefaultThreadPoolKey };

        await using ServiceProvider provider = Container(services => InEitherOrder(
            services,
            configureAllFirst,
            register: () => services.AddQuartz(Named, properties),
            configureAll: () => services.ConfigureAllQuartzSchedulers(q => q.UseThreadPool<MarkedThreadPool>())));

        IScheduler registered = await provider.GetRequiredKeyedService<ISchedulerFactory>(Named).GetScheduler();
        IScheduler tenant = await provider.GetRequiredService<ISchedulerRuntime>().Add(
            "tenant",
            options: new SchedulerAddOptions { Properties = properties, CreateWithoutStarting = true });
        try
        {
            (await registered.GetMetadata()).ThreadPoolTypeName.Should().Contain(nameof(DefaultThreadPool),
                "the key is the scheduler's own configuration");
            (await tenant.GetMetadata()).ThreadPoolTypeName.Should().Contain(nameof(DefaultThreadPool),
                "a tenant is an ordinary scheduler, so the same recipe gives it the same parts");
        }
        finally
        {
            await registered.Shutdown();
            await tenant.Shutdown();
        }
    }

    private static void InEitherOrder(IServiceCollection services, bool configureAllFirst, Action register, Action configureAll)
    {
        if (configureAllFirst)
        {
            configureAll();
            register();
        }
        else
        {
            register();
            configureAll();
        }
    }

    private static void Register(IServiceCollection services, string? schedulerName, Action<IQuartzBuilder>? configure = null)
    {
        if (schedulerName is null)
        {
            services.AddQuartz(configure);
        }
        else
        {
            services.AddQuartz(schedulerName, configure);
        }
    }

    private static ServiceCollection Collection()
    {
        ServiceCollection services = new();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        return services;
    }

    private static ServiceProvider Container(Action<IServiceCollection> configure)
    {
        ServiceCollection services = Collection();
        configure(services);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// A working in-memory store whose type is its own, so a test can tell it from the default.
    /// </summary>
    private sealed class MarkedJobStore(ILoggerFactory loggerFactory, ISchedulerSignaler signaler, TimeProvider timeProvider)
        : DelegatingJobStore(new RAMJobStore(loggerFactory, signaler, timeProvider));

    /// <summary>
    /// A working thread pool whose type is its own, which the scheduler's metadata reports as it is.
    /// </summary>
    private sealed class MarkedThreadPool : TaskSchedulingThreadPool
    {
        protected override TaskScheduler GetDefaultScheduler() => TaskScheduler.Default;
    }

    private sealed class RecordingJobFactory : IJobFactory
    {
        public ValueTask<JobScope> CreateJob(TriggerFiredBundle bundle, IScheduler scheduler, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("this factory exists to be resolved, not to build jobs");
        }

        public ValueTask ReturnJob(JobScope scope, CancellationToken cancellationToken = default) => default;
    }

    private sealed class PlainJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
