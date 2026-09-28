#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// A scheduler with a job factory of the application's own registers none of its job types with the
/// container, so container validation does not fail a job on dependencies only that factory supplies
/// (#3930, from discussion #3842).
/// </summary>
/// <remarks>
/// <c>AddJob&lt;T&gt;</c> registers the job type so that <c>ValidateOnBuild</c> reports a dependency the
/// container cannot supply. That is only true of a factory that builds jobs from the container: the
/// default <see cref="MicrosoftDependencyInjectionJobFactory" />, or one derived from it. Any other factory
/// builds the job its own way, often from another container, and the registration is one nothing
/// resolves.
/// </remarks>
public sealed class CustomJobFactoryRegistrationTest
{
    /// <summary>
    /// The program from the discussion: a job whose dependency lives in a container other than Microsoft's,
    /// built by the factory that knows that container, on a host that validates on build.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TheDiscussionsProgramBuildsAndRunsItsJobThroughTheFactory(bool factoryFirst)
    {
        OtherContainer other = new();

        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.ConfigureContainer(new DefaultServiceProviderFactory(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));

        builder.Services.AddSingleton(other);
        builder.Services.AddQuartz(q =>
        {
            if (factoryFirst)
            {
                q.UseJobFactory<OutsideJobFactory>();
            }

            q.ScheduleJob<OutsideJob>(trigger => trigger
                .StartNow()
                .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromSeconds(1)).RepeatForever()));

            if (!factoryFirst)
            {
                q.UseJobFactory<OutsideJobFactory>();
            }
        });
        builder.Services.AddQuartzHostedService();

        builder.Services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(OutsideJob),
            "the factory builds the job, so a registration would be one nothing resolves, whichever of the "
            + "two calls came first");

        using IHost host = builder.Build();
        await host.StartAsync();

        try
        {
            Task executed = other.Service.Executed.Task;
            Task finished = await Task.WhenAny(executed, Task.Delay(TimeSpan.FromSeconds(30)));
            finished.Should().BeSameAs(executed, "the job is built by the factory, with what only it supplies");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    /// <summary>
    /// The standalone builder validates the container it builds, so it could not build this scheduler at all.
    /// </summary>
    [Test]
    public async Task TheStandaloneBuilderBuildsASchedulerWhoseFactorySuppliesTheJob()
    {
        OtherContainer other = new();

        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(q =>
            {
                q.ScheduleJob<OutsideJob>(trigger => trigger.StartNow());
                q.UseJobFactory(new OutsideJobFactory(other));
            })
            .BuildScheduler();

        try
        {
            await scheduler.Start();

            Task executed = other.Service.Executed.Task;
            Task finished = await Task.WhenAny(executed, Task.Delay(TimeSpan.FromSeconds(30)));
            finished.Should().BeSameAs(executed, "the job is built by the factory, with what only it supplies");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    /// <summary>
    /// Every way of choosing a scheduler's job factory, and whether the job type ends up registered — which
    /// is exactly when container validation fails it, since only the factory can build it.
    /// </summary>
    [TestCaseSource(nameof(WaysToChooseAJobFactory))]
    public void TheJobTypeIsRegisteredOnlyWhenTheFactoryBuildsFromTheContainer(
        Action<IServiceCollection> register,
        bool registered)
    {
        ServiceCollection services = new();
        services.AddSingleton<OtherContainer>();

        register(services);

        services.Count(descriptor => descriptor.ServiceType == typeof(OutsideJob)).Should().Be(registered ? 1 : 0);

        Action build = () => services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true }).Dispose();

        if (registered)
        {
            build.Should().Throw<AggregateException>().WithMessage("*IOutsideService*OutsideJob*",
                "a factory that builds from the container is still told at startup what it cannot build");
        }
        else
        {
            build.Should().NotThrow("validation cannot see what the factory supplies, so it is not asked to");
        }
    }

    private static IEnumerable<TestCaseData> WaysToChooseAJobFactory()
    {
        yield return Case("the default factory", registered: true, services =>
            services.AddQuartz(q => q.AddJob<OutsideJob>(Identity)));

        yield return Case("a factory derived from the default, chosen first", registered: true, services =>
            services.AddQuartz(q =>
            {
                q.UseJobFactory<DerivedJobFactory>();
                q.AddJob<OutsideJob>(Identity);
            }));

        yield return Case("a factory derived from the default, chosen last", registered: true, services =>
            services.AddQuartz(q =>
            {
                q.AddJob<OutsideJob>(Identity);
                q.UseJobFactory<DerivedJobFactory>();
            }));

        yield return Case("a factory of its own, chosen last, on a named scheduler", registered: false, services =>
            services.AddQuartz("acme", q =>
            {
                q.AddJob<OutsideJob>(Identity);
                q.UseJobFactory<OutsideJobFactory>();
            }));

        yield return Case("a factory handed over as an instance", registered: false, services =>
            services.AddQuartz(q =>
            {
                q.AddJob<OutsideJob>(Identity);
                q.UseJobFactory(new OutsideJobFactory(new OtherContainer()));
            }));

        yield return Case("a factory of its own named by a property key", registered: false, services =>
            services.AddQuartz(
                new Dictionary<string, string?> { ["quartz.scheduler.jobFactory.type"] = typeof(OutsideJobFactory).AssemblyQualifiedName },
                q => q.AddJob<OutsideJob>(Identity)));

        yield return Case("a factory derived from the default named by a property key", registered: true, services =>
            services.AddQuartz(
                new Dictionary<string, string?> { ["quartz.scheduler.jobFactory.type"] = typeof(DerivedJobFactory).AssemblyQualifiedName },
                q => q.AddJob<OutsideJob>(Identity)));

        yield return Case("a factory of its own the application registered by type", registered: false, services =>
        {
            services.AddSingleton<IJobFactory, OutsideJobFactory>();
            services.AddQuartz(q => q.AddJob<OutsideJob>(Identity));
        });

        yield return Case("a factory derived from the default the application registered by type", registered: true, services =>
        {
            services.AddSingleton<IJobFactory, DerivedJobFactory>();
            services.AddQuartz(q => q.AddJob<OutsideJob>(Identity));
        });

        yield return Case("a factory of its own the application registered for one named scheduler", registered: false, services =>
        {
            services.AddKeyedSingleton<IJobFactory, OutsideJobFactory>("acme");
            services.AddQuartz("acme", q => q.AddJob<OutsideJob>(Identity));
        });

        yield return Case("a factory the application registered with a delegate", registered: false, services =>
        {
            services.AddSingleton<IJobFactory>(provider => new OutsideJobFactory(provider.GetRequiredService<OtherContainer>()));
            services.AddQuartz(q => q.AddJob<OutsideJob>(Identity));
        });

        yield return Case("a factory of its own chosen for every scheduler beforehand", registered: false, services =>
        {
            services.ConfigureAllQuartzSchedulers(q => q.UseJobFactory<OutsideJobFactory>());
            services.AddQuartz(q => q.AddJob<OutsideJob>(Identity));
        });

        yield return Case("a job added for every scheduler afterwards, to one with a factory of its own", registered: false, services =>
        {
            services.AddQuartz(q => q.UseJobFactory<OutsideJobFactory>());
            services.ConfigureAllQuartzSchedulers(q => q.AddJob<OutsideJob>(Identity));
        });

        yield return Case("a job added for every scheduler afterwards, to one on the default factory", registered: true, services =>
        {
            services.AddQuartz(q => q.UseInMemoryStore());
            services.ConfigureAllQuartzSchedulers(q => q.AddJob<OutsideJob>(Identity));
        });

        yield return Case("a job added by a second AddQuartz() to a scheduler with a factory of its own", registered: false, services =>
        {
            services.AddQuartz(q => q.UseJobFactory<OutsideJobFactory>());
            services.AddQuartz(q => q.AddJob<OutsideJob>(Identity));
        });

        static TestCaseData Case(string name, bool registered, Action<IServiceCollection> register)
        {
            return new TestCaseData(register, registered).SetArgDisplayNames(name);
        }
    }

    /// <summary>
    /// Withheld, never removed: a scheduler on the default factory that carries the same job type keeps
    /// the registration, whichever of the two was registered first.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public void AJobTypeASchedulerOnTheDefaultFactoryAlsoCarriesKeepsItsRegistration(bool customFactoryFirst)
    {
        ServiceCollection services = new();
        services.AddSingleton<OtherContainer>();

        if (customFactoryFirst)
        {
            AddCustom(services);
            AddDefault(services);
        }
        else
        {
            AddDefault(services);
            AddCustom(services);
        }

        services.Where(descriptor => descriptor.ServiceType == typeof(SharedJob)).Should().ContainSingle()
            .Which.Lifetime.Should().Be(ServiceLifetime.Scoped,
                "the scheduler on the default factory builds the job from the container, as it always did");

        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        provider.GetRequiredService<IStartupValidator>().Validate();

        static void AddCustom(IServiceCollection services)
        {
            services.AddQuartz("custom", q =>
            {
                q.UseJobFactory<OutsideJobFactory>();
                q.AddJob<SharedJob>(Identity);
            });
        }

        static void AddDefault(IServiceCollection services)
        {
            services.AddQuartz("plain", q => q.AddJob<SharedJob>(Identity));
        }
    }

    /// <summary>
    /// The <c>TryAdd</c> rule is unchanged: a registration the application made itself is never removed.
    /// </summary>
    [Test]
    public void ARegistrationTheApplicationMadeItselfIsKept()
    {
        OutsideJob instance = new(new OutsideService());

        ServiceCollection services = new();
        services.AddSingleton<OtherContainer>();
        services.AddSingleton(instance);

        services.AddQuartz(q =>
        {
            q.UseJobFactory<OutsideJobFactory>();
            q.AddJob<OutsideJob>(Identity);
        });

        services.Where(descriptor => descriptor.ServiceType == typeof(OutsideJob)).Should().ContainSingle()
            .Which.ImplementationInstance.Should().BeSameAs(instance,
                "the application may resolve the job from the container for reasons of its own");
    }

    /// <summary>
    /// The constructor rule is about a job the container builds. A factory of the application's own builds
    /// its job itself, and may hand it the scheduler firing it.
    /// </summary>
    [Test]
    public void AJobOnlyTheFactoryBuildsIsNotHeldToTheRegisteredJobConstructorRule()
    {
        ServiceCollection services = new();
        services.AddSingleton<OtherContainer>();
        services.AddQuartz(q =>
        {
            q.UseJobFactory<OutsideJobFactory>();
            q.AddJob<SchedulerTakingJob>(Identity);
        });

        using ServiceProvider provider = services.BuildServiceProvider();

        Action validate = () => provider.GetRequiredService<IStartupValidator>().Validate();

        validate.Should().NotThrow("the container never builds this job, so what its constructor takes is the factory's business");
    }

    /// <summary>
    /// A delegate job is Quartz's type rather than the application's, and a factory of the application's
    /// own that does not know it can still hand it to the container.
    /// </summary>
    [Test]
    public void ADelegateJobIsRegisteredWhateverTheJobFactory()
    {
        ServiceCollection services = new();
        services.AddSingleton<OtherContainer>();
        services.AddQuartz(q =>
        {
            q.UseJobFactory<OutsideJobFactory>();
            q.AddJob("tick", () => { });
        });

        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(DelegateJob));
    }

    private static void Identity<TJob>(IJobConfigurator<TJob> job) where TJob : IJob
    {
        job.WithIdentity("job").StoreDurably();
    }

    /// <summary>
    /// A container other than Microsoft's, holding what the jobs need.
    /// </summary>
    public sealed class OtherContainer
    {
        public OutsideService Service { get; } = new();
    }

    /// <summary>
    /// A dependency Microsoft's container does not hold.
    /// </summary>
    public interface IOutsideService
    {
        TaskCompletionSource Executed { get; }
    }

    public sealed class OutsideService : IOutsideService
    {
        public TaskCompletionSource Executed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public sealed class OutsideJob : IJob
    {
        private readonly IOutsideService outside;

        public OutsideJob(IOutsideService outside)
        {
            this.outside = outside;
        }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            outside.Executed.TrySetResult();
            return default;
        }
    }

    /// <summary>
    /// A job Microsoft's container could build, carried by schedulers on both kinds of factory.
    /// </summary>
    public sealed class SharedJob : IJob
    {
        public SharedJob(OtherContainer other)
        {
            Other = other;
        }

        public OtherContainer Other { get; }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

    public sealed class SchedulerTakingJob : IJob
    {
        public SchedulerTakingJob(IScheduler scheduler)
        {
            Scheduler = scheduler;
        }

        public IScheduler Scheduler { get; }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

    /// <summary>
    /// Builds jobs from the other container, as the discussion's factory does.
    /// </summary>
    public sealed class OutsideJobFactory : IJobFactory
    {
        private readonly OtherContainer other;

        public OutsideJobFactory(OtherContainer other)
        {
            this.other = other;
        }

        public ValueTask<JobScope> CreateJob(TriggerFiredBundle bundle, IScheduler scheduler, CancellationToken cancellationToken = default)
        {
            return new ValueTask<JobScope>(new JobScope(new OutsideJob(other.Service)));
        }

        public ValueTask ReturnJob(JobScope scope, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

    /// <summary>
    /// A factory derived from the default, which resolves jobs from the container like the default does.
    /// </summary>
    public sealed class DerivedJobFactory : MicrosoftDependencyInjectionJobFactory
    {
        public DerivedJobFactory(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }
    }
}
