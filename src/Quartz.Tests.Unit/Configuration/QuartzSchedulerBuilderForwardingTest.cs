using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Core;
using Quartz.Extensibility;
using Quartz.Impl.Calendar;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// Every mirror on <see cref="QuartzSchedulerBuilder" /> does the work its
/// <see cref="IQuartzBuilder" /> extension does.
/// </summary>
/// <remarks>
/// <see cref="QuartzBuilderExtensionsMirrorTest" /> proves a mirror exists for every extension and that
/// it returns the builder; neither says the body forwards. A mirror whose <c>inner.X(…)</c> call was
/// forgotten would satisfy both, chain beautifully, and register nothing — the job simply would not be
/// there at run time. So each case here calls one mirror and then reads back the registration the
/// extension behind it makes.
/// </remarks>
public class QuartzSchedulerBuilderForwardingTest
{
    /// <param name="Member">The mirror's method name, which is how the coverage check pairs cases with mirrors.</param>
    /// <param name="Name">The overload, readably, for the test name and the failure message.</param>
    /// <param name="Call">Calls the mirror on a fresh builder.</param>
    /// <param name="Forwarded">Reads the services for what the extension behind it registers.</param>
    public sealed record Case(
        string Member,
        string Name,
        Action<QuartzSchedulerBuilder> Call,
        Func<IServiceCollection, bool> Forwarded);

    public static readonly Case[] All =
    [
        new Case("UseSimpleTypeLoader", "UseSimpleTypeLoader()",
            builder => builder.UseSimpleTypeLoader(),
            services => Unkeyed(services).Any(descriptor =>
                descriptor.ServiceType == typeof(ITypeLoader) && descriptor.ImplementationType?.Name == "SimpleTypeLoader")),

        // UseTypeLoader<T>() is an interface member rather than an extension, but it shares its name with
        // one — and the coverage check pairs cases with mirrors by name — so both overloads need a case.
        new Case("UseTypeLoader", "UseTypeLoader<T>()",
            builder => builder.UseTypeLoader<ForwardingTypeLoader>(),
            services => Unkeyed(services).Any(descriptor =>
                descriptor.ServiceType == typeof(ITypeLoader) && descriptor.ImplementationType == typeof(ForwardingTypeLoader))),

        new Case("UseTypeLoader", "UseTypeLoader(configure)",
            builder => builder.UseTypeLoader(loader => loader.Map("Acme.Jobs.Renamed, Acme.Jobs", typeof(ForwardingJob))),
            services => Resolve(services, provider =>
                provider.GetRequiredService<IOptions<TypeLoaderOptions>>().Value.Aliases.ContainsKey("Acme.Jobs.Renamed, Acme.Jobs"))),

        new Case("ConfigureJobScope", "ConfigureJobScope(configure)",
            builder => builder.ConfigureJobScope((_, _, _) => { }),
            services => Resolve(services, provider =>
                provider.GetRequiredService<IOptions<JobFactoryOptions>>().Value.ConfigureScope is not null)),

        new Case("AddJob", "AddJob<T>(configure)",
            builder => builder.AddJob<ForwardingJob>(job => job.WithIdentity("job-typed")),
            services => HasJob(services, "job-typed")),

        new Case("AddJob", "AddJob<T>(serviceProvider, configure)",
            builder => builder.AddJob<ForwardingJob>((_, job) => job.WithIdentity("job-typed-sp")),
            services => HasJob(services, "job-typed-sp")),

        new Case("AddJob", "AddJob(jobType, configure)",
            builder => builder.AddJob(typeof(ForwardingJob), job => job.WithIdentity("job-runtime")),
            services => HasJob(services, "job-runtime")),

        new Case("AddJob", "AddJob(jobType, serviceProvider, configure)",
            builder => builder.AddJob(typeof(ForwardingJob), (_, job) => job.WithIdentity("job-runtime-sp")),
            services => HasJob(services, "job-runtime-sp")),

        new Case("AddTrigger", "AddTrigger<TJob>(configure)",
            builder => builder.AddTrigger<ForwardingJob>(trigger => trigger.WithIdentity("trigger-typed").ForJob("some-job")),
            services => HasTrigger(services, "trigger-typed")),

        new Case("AddTrigger", "AddTrigger<TJob>(serviceProvider, configure)",
            builder => builder.AddTrigger<ForwardingJob>((_, trigger) => trigger.WithIdentity("trigger-typed-sp").ForJob("some-job")),
            services => HasTrigger(services, "trigger-typed-sp")),

        new Case("AddTrigger", "AddTrigger(configure)",
            builder => builder.AddTrigger(trigger => trigger.WithIdentity("trigger-untyped").ForJob("some-job")),
            services => HasTrigger(services, "trigger-untyped")),

        new Case("AddTrigger", "AddTrigger(serviceProvider, configure)",
            builder => builder.AddTrigger((_, trigger) => trigger.WithIdentity("trigger-untyped-sp").ForJob("some-job")),
            services => HasTrigger(services, "trigger-untyped-sp")),

        // ScheduleJob is the one registration carrying both, and the job takes the trigger's identity —
        // so finding only the trigger would be AddTrigger's work rather than this one's.
        new Case("ScheduleJob", "ScheduleJob<T>(trigger, job)",
            builder => builder.ScheduleJob<ForwardingJob>(trigger => trigger.WithIdentity("scheduled")),
            services => HasJob(services, "scheduled") && HasTrigger(services, "scheduled")),

        new Case("ScheduleJob", "ScheduleJob<T>(serviceProvider, trigger, job)",
            builder => builder.ScheduleJob<ForwardingJob>((_, trigger) => trigger.WithIdentity("scheduled-sp")),
            services => HasJob(services, "scheduled-sp") && HasTrigger(services, "scheduled-sp")),

        new Case("AddJobType", "AddJobType<TJob>()",
            builder => builder.AddJobType<ForwardingJob>(),
            services => HasJobType(services, typeof(ForwardingJob), typeof(ForwardingJob), ServiceLifetime.Scoped)),

        new Case("AddJobType", "AddJobType<TJob>(lifetime)",
            builder => builder.AddJobType<ForwardingJob>(ServiceLifetime.Singleton),
            services => HasJobType(services, typeof(ForwardingJob), typeof(ForwardingJob), ServiceLifetime.Singleton)),

        new Case("AddJobType", "AddJobType<TJob, TImplementation>()",
            builder => builder.AddJobType<ForwardingJob, DerivedForwardingJob>(),
            services => HasJobType(services, typeof(ForwardingJob), typeof(DerivedForwardingJob), ServiceLifetime.Scoped)),

        new Case("AddJobType", "AddJobType<TJob, TImplementation>(lifetime)",
            builder => builder.AddJobType<ForwardingJob, DerivedForwardingJob>(ServiceLifetime.Singleton),
            services => HasJobType(services, typeof(ForwardingJob), typeof(DerivedForwardingJob), ServiceLifetime.Singleton)),

        new Case("AddJobType", "AddJobType<TJob>(implementationFactory)",
            builder => builder.AddJobType(_ => new ForwardingJob()),
            services => HasJobTypeFactory(services, typeof(ForwardingJob), ServiceLifetime.Scoped)),

        new Case("AddJobType", "AddJobType<TJob>(implementationFactory, lifetime)",
            builder => builder.AddJobType(_ => new ForwardingJob(), ServiceLifetime.Singleton),
            services => HasJobTypeFactory(services, typeof(ForwardingJob), ServiceLifetime.Singleton)),

        new Case("AddCalendar", "AddCalendar<T>(name, options, configure)",
            builder => builder.AddCalendar<HolidayCalendar>("calendar-typed"),
            services => HasCalendar(services, "calendar-typed")),

        new Case("AddCalendar", "AddCalendar<T>(name, options, serviceProvider, configure)",
            builder => builder.AddCalendar<HolidayCalendar>("calendar-typed-sp", default, (_, _) => { }),
            services => HasCalendar(services, "calendar-typed-sp")),

        new Case("AddCalendar", "AddCalendar(name, calendar, options)",
            builder => builder.AddCalendar("calendar-instance", new HolidayCalendar()),
            services => HasCalendar(services, "calendar-instance")),

        new Case("AddCalendar", "AddCalendar(name, factory, options)",
            builder => builder.AddCalendar("calendar-factory", _ => new HolidayCalendar()),
            services => HasCalendar(services, "calendar-factory")),

        new Case("AddJobTimeout", "AddJobTimeout(defaultTimeout)",
            builder => builder.AddJobTimeout(TimeSpan.FromMinutes(1)),
            services => Resolve(services, provider => provider
                .GetServices<JobExecutionMiddlewareRegistration>()
                .Any(registration => registration.MiddlewareType == typeof(JobTimeoutMiddleware)))),
    ];

    public static IEnumerable<TestCaseData> Cases()
    {
        foreach (Case testCase in All)
        {
            yield return new TestCaseData(testCase).SetName($"{{m}} {testCase.Name}");
        }
    }

    [TestCaseSource(nameof(Cases))]
    public void TheMirrorForwardsToTheExtension(Case testCase)
    {
        QuartzSchedulerBuilder builder = QuartzSchedulerBuilder.Create();

        testCase.Call(builder);

        testCase.Forwarded(builder.Services).Should().BeTrue(
            $"QuartzSchedulerBuilder.{testCase.Name} must do what the {testCase.Member} extension does — "
            + "a mirror that returns the builder without calling it registers nothing");
    }

    private static bool HasJob(IServiceCollection services, string name)
    {
        return Resolve(services, provider => provider.ScheduledJobs().Any(job => job.Key.Name == name));
    }

    private static bool HasTrigger(IServiceCollection services, string name)
    {
        return Resolve(services, provider => provider.ScheduledTriggers().Any(trigger => trigger.Key.Name == name));
    }

    private static bool HasCalendar(IServiceCollection services, string name)
    {
        return Resolve(services, provider => provider.GetServices<CalendarConfiguration>().Any(calendar => calendar.Name == name));
    }

    private static bool HasJobType(IServiceCollection services, Type jobType, Type implementationType, ServiceLifetime lifetime)
    {
        return Unkeyed(services).Any(descriptor =>
            descriptor.ServiceType == jobType
            && descriptor.ImplementationType == implementationType
            && descriptor.Lifetime == lifetime);
    }

    private static bool HasJobTypeFactory(IServiceCollection services, Type jobType, ServiceLifetime lifetime)
    {
        return Unkeyed(services).Any(descriptor =>
            descriptor.ServiceType == jobType
            && descriptor.ImplementationFactory is not null
            && descriptor.Lifetime == lifetime);
    }

    /// <summary>
    /// The standalone builder is the default scheduler, so everything it registers is unkeyed — and
    /// reading <see cref="ServiceDescriptor.ImplementationType" /> off a keyed descriptor throws.
    /// </summary>
    private static IEnumerable<ServiceDescriptor> Unkeyed(IServiceCollection services)
    {
        return services.Where(descriptor => !descriptor.IsKeyedService);
    }

    /// <summary>
    /// A job, a trigger or a calendar is registered as a factory that runs when it is resolved, so
    /// reading one back means building the container the mirror wrote into.
    /// </summary>
    private static bool Resolve(IServiceCollection services, Func<IServiceProvider, bool> read)
    {
        using ServiceProvider provider = services.BuildServiceProvider();
        return read(provider);
    }

    private class ForwardingJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

    private sealed class DerivedForwardingJob : ForwardingJob
    {
    }

    private sealed class ForwardingTypeLoader : ITypeLoader
    {
        public Type LoadType(string name) => Type.GetType(name, throwOnError: true)!;
    }
}
