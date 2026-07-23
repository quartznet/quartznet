using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;


using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Impl;
using Quartz.Logging;

namespace Quartz.Tests.Unit.Extensions.DependencyInjection;

[NonParallelizable]
public sealed class DeferredQuartzConfigurationTests
{
    // --- Property configuration ---

    [Test]
    public void DeferredLambda_ShouldSetSchedulerProperties()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddQuartz((q, sp) =>
        {
            q.SchedulerName = "DeferredScheduler";
            q.SchedulerId = "DEFERRED_01";
            q.MaxBatchSize = 7;
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        QuartzOptions options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options[StdSchedulerFactory.PropertySchedulerInstanceName].Should().Be("DeferredScheduler");
        options[StdSchedulerFactory.PropertySchedulerInstanceId].Should().Be("DEFERRED_01");
        options[StdSchedulerFactory.PropertySchedulerMaxBatchSize].Should().Be("7");
    }

    [Test]
    public void DeferredLambda_ShouldResolveServicesFromProvider()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IConnectionStringProvider>(new TestConnectionStringProvider("Server=test;Database=quartz"));

        services.AddQuartz((q, sp) =>
        {
            IConnectionStringProvider provider = sp.GetRequiredService<IConnectionStringProvider>();
            q.SetProperty("quartz.custom.connectionString", provider.GetConnectionString());
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        QuartzOptions options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options["quartz.custom.connectionString"].Should().Be("Server=test;Database=quartz");
    }

    [Test]
    public void DeferredLambda_ShouldConfigurePersistentStoreConnectionString()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IConnectionStringProvider>(new TestConnectionStringProvider("Server=myhost;Database=quartz;User=admin"));

        services.AddQuartz((q, sp) =>
        {
            IConnectionStringProvider connProvider = sp.GetRequiredService<IConnectionStringProvider>();
            q.UsePersistentStore(s =>
            {
                s.UseSqlServer(sqlServer =>
                {
                    sqlServer.ConnectionString = connProvider.GetConnectionString();
                });
            });
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        QuartzOptions options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options[$"quartz.dataSource.{SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName}.connectionString"]
            .Should().Be("Server=myhost;Database=quartz;User=admin");
    }

    [Test]
    public void DeferredLambda_WithIOptionsPattern_ShouldWork()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.Configure<TestSchedulerConfig>(c =>
        {
            c.Name = "FromOptions";
            c.MaxBatch = 5;
        });

        services.AddQuartz((q, sp) =>
        {
            TestSchedulerConfig config = sp.GetRequiredService<IOptions<TestSchedulerConfig>>().Value;
            q.SchedulerName = config.Name;
            q.MaxBatchSize = config.MaxBatch;
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        QuartzOptions options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options[StdSchedulerFactory.PropertySchedulerInstanceName].Should().Be("FromOptions");
        options[StdSchedulerFactory.PropertySchedulerMaxBatchSize].Should().Be("5");
    }

    [Test]
    public void DeferredLambda_WithNameValueCollectionProperties_ShouldMerge()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        var properties = new System.Collections.Specialized.NameValueCollection
        {
            { "quartz.custom.initial", "fromProperties" }
        };

        services.AddQuartz(properties, (q, sp) =>
        {
            q.SetProperty("quartz.custom.deferred", "fromDeferred");
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        QuartzOptions options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options["quartz.custom.initial"].Should().Be("fromProperties");
        options["quartz.custom.deferred"].Should().Be("fromDeferred");
    }

    // --- AddJob / AddTrigger / ScheduleJob ---

    [Test]
    public void DeferredLambda_AddJob_ShouldRegisterJobDetail()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddQuartz((q, sp) =>
        {
            q.AddJob<TestJob>(j => j.WithIdentity("deferredJob", "group1"));
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        QuartzOptions options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options.JobDetails.Should().HaveCount(1);
        options.JobDetails[0].Key.Name.Should().Be("deferredJob");
        options.JobDetails[0].Key.Group.Should().Be("group1");
    }

    [Test]
    public void DeferredLambda_AddJobWithServiceProvider_ShouldResolveService()
    {
        ServiceCollection services = new ServiceCollection();
        var configBuilder = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                { "jobName", "ConfiguredJob" }
            });
        services.AddSingleton<IConfiguration>(configBuilder.Build());

        services.AddQuartz((q, sp) =>
        {
            q.AddJob<TestJob>((innerSp, j) =>
            {
                IConfiguration config = innerSp.GetRequiredService<IConfiguration>();
                j.WithIdentity(config["jobName"]!, "group1");
            });
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        QuartzOptions options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options.JobDetails.Should().HaveCount(1);
        options.JobDetails[0].Key.Name.Should().Be("ConfiguredJob");
    }

    [Test]
    public void DeferredLambda_AddTrigger_ShouldRegisterTrigger()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddQuartz((q, sp) =>
        {
            q.AddTrigger(t => t
                .ForJob("someJob", "group1")
                .WithIdentity("deferredTrigger", "group1")
                .StartNow());
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        QuartzOptions options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options.Triggers.Should().HaveCount(1);
        options.Triggers[0].Key.Name.Should().Be("deferredTrigger");
        options.Triggers[0].Key.Group.Should().Be("group1");
    }

    [Test]
    public void DeferredLambda_ScheduleJob_ShouldRegisterBothJobAndTrigger()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddQuartz((q, sp) =>
        {
            q.ScheduleJob<TestJob>(
                t => t.WithIdentity("scheduledTrigger", "group1"),
                j => j.WithIdentity("scheduledJob", "group1"));
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        QuartzOptions options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options.JobDetails.Should().HaveCount(1);
        options.Triggers.Should().HaveCount(1);
        options.JobDetails[0].Key.Name.Should().Be("scheduledJob");
        options.Triggers[0].Key.Name.Should().Be("scheduledTrigger");
        options.Triggers[0].JobKey.Should().Be(options.JobDetails[0].Key);
    }

    [Test]
    public void DeferredLambda_AddCalendar_ShouldCaptureCalendar()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddQuartz((q, sp) =>
        {
            q.AddCalendar<TestCalendar>("deferred-cal", true, true, cal =>
            {
                cal.Description = "Deferred calendar";
            });
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        QuartzOptions options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options.deferredCalendars.Should().HaveCount(1);
        options.deferredCalendars[0].Name.Should().Be("deferred-cal");
        options.deferredCalendars[0].Calendar.Description.Should().Be("Deferred calendar");
    }

    // --- Listener configuration ---

    [Test]
    public void DeferredLambda_AddSchedulerListener_ShouldCaptureInDeferredList()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddQuartz((q, sp) =>
        {
            q.AddSchedulerListener<TestSchedulerListener>();
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        QuartzOptions options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options.deferredSchedulerListeners.Should().HaveCount(1);
        options.deferredSchedulerListeners[0].ListenerType.Should().Be(typeof(TestSchedulerListener));
    }

    [Test]
    public void DeferredLambda_AddSchedulerListenerWithFactory_ShouldCaptureFactory()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddQuartz((q, sp) =>
        {
            q.AddSchedulerListener<TestSchedulerListener>(_ => new TestSchedulerListener());
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        QuartzOptions options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options.deferredSchedulerListeners.Should().HaveCount(1);
        options.deferredSchedulerListeners[0].ListenerFactory.Should().NotBeNull();
    }

    // --- Named scheduler support ---

    [Test]
    public void DeferredLambda_NamedScheduler_ShouldIsolateOptions()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        services.AddQuartz("Sched1", (q, sp) =>
        {
            q.AddJob<TestJob>(j => j.WithIdentity("job1", "group1"));
        });

        services.AddQuartz("Sched2", (q, sp) =>
        {
            q.AddJob<TestJob>(j => j.WithIdentity("job2", "group2"));
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<QuartzOptions> monitor = provider.GetRequiredService<IOptionsMonitor<QuartzOptions>>();

        QuartzOptions options1 = monitor.Get("Sched1");
        QuartzOptions options2 = monitor.Get("Sched2");

        options1.JobDetails.Should().HaveCount(1);
        options1.JobDetails[0].Key.Name.Should().Be("job1");

        options2.JobDetails.Should().HaveCount(1);
        options2.JobDetails[0].Key.Name.Should().Be("job2");
    }

    [Test]
    public void DeferredLambda_NamedScheduler_ShouldResolveServiceProvider()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddSingleton<IConnectionStringProvider>(new TestConnectionStringProvider("Server=named;Database=quartz"));

        services.AddQuartz("NamedSched", (q, sp) =>
        {
            IConnectionStringProvider connProvider = sp.GetRequiredService<IConnectionStringProvider>();
            q.SetProperty("quartz.custom.conn", connProvider.GetConnectionString());
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<QuartzOptions> monitor = provider.GetRequiredService<IOptionsMonitor<QuartzOptions>>();
        QuartzOptions options = monitor.Get("NamedSched");

        options["quartz.custom.conn"].Should().Be("Server=named;Database=quartz");
    }

    // --- Combined immediate + deferred ---

    [Test]
    public void DeferredLambda_ShouldOverrideImmediateProperties()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        // Immediate: set scheduler name
        services.AddQuartz(q =>
        {
            q.SchedulerName = "ImmediateName";
        });

        // Deferred: override scheduler name
        services.AddQuartz((q, sp) =>
        {
            q.SchedulerName = "DeferredName";
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        QuartzOptions options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options[StdSchedulerFactory.PropertySchedulerInstanceName].Should().Be("DeferredName");
    }

    [Test]
    public void DeferredLambda_ShouldAccumulateJobsWithImmediate()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddQuartz(q =>
        {
            q.AddJob<TestJob>(j => j.WithIdentity("immediateJob", "group1"));
        });

        services.AddQuartz((q, sp) =>
        {
            q.AddJob<TestJob>(j => j.WithIdentity("deferredJob", "group1"));
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        QuartzOptions options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options.JobDetails.Should().HaveCount(2);
        options.JobDetails.Select(j => j.Key.Name).Should().Contain(new[] { "immediateJob", "deferredJob" });
    }

    // --- Integration: scheduler creation ---

    [Test]
    public async Task DeferredLambda_ShouldCreateSchedulerWithDeferredProperties()
    {
        try
        {
            ServiceCollection services = new ServiceCollection();
            services.AddLogging();

            services.AddQuartz((q, sp) =>
            {
                q.SchedulerName = "DeferredCreation";
                q.UseInMemoryStore();
            });

            await using ServiceProvider provider = services.BuildServiceProvider();
            ISchedulerFactory factory = provider.GetRequiredService<ISchedulerFactory>();
            IScheduler scheduler = await factory.GetScheduler();

            scheduler.SchedulerName.Should().Be("DeferredCreation");

            await scheduler.Shutdown();
        }
        finally
        {
            LogProvider.SetCurrentLogProvider(null);
        }
    }

    [Test]
    public async Task DeferredLambda_ShouldWireListenerToScheduler()
    {
        try
        {
            ServiceCollection services = new ServiceCollection();
            services.AddLogging();

            services.AddQuartz((q, sp) =>
            {
                q.SchedulerName = "ListenerTest";
                q.UseInMemoryStore();
                q.AddSchedulerListener<TestSchedulerListener>();
            });

            await using ServiceProvider provider = services.BuildServiceProvider();
            ISchedulerFactory factory = provider.GetRequiredService<ISchedulerFactory>();
            IScheduler scheduler = await factory.GetScheduler();

            IReadOnlyCollection<ISchedulerListener> listeners = scheduler.ListenerManager.GetSchedulerListeners();
            listeners.Should().Contain(l => l is TestSchedulerListener);

            await scheduler.Shutdown();
        }
        finally
        {
            LogProvider.SetCurrentLogProvider(null);
        }
    }

    [Test]
    public async Task DeferredLambda_ShouldWireJobListenerToScheduler()
    {
        try
        {
            ServiceCollection services = new ServiceCollection();
            services.AddLogging();

            services.AddQuartz((q, sp) =>
            {
                q.SchedulerName = "JobListenerTest";
                q.UseInMemoryStore();
                q.AddJobListener<TestJobListener>();
            });

            await using ServiceProvider provider = services.BuildServiceProvider();
            ISchedulerFactory factory = provider.GetRequiredService<ISchedulerFactory>();
            IScheduler scheduler = await factory.GetScheduler();

            IReadOnlyCollection<IJobListener> listeners = scheduler.ListenerManager.GetJobListeners();
            listeners.Should().Contain(l => l is TestJobListener);

            // Verify no duplicate — the listener should be registered exactly once
            listeners.Where(l => l is TestJobListener).Should().HaveCount(1);

            await scheduler.Shutdown();
        }
        finally
        {
            LogProvider.SetCurrentLogProvider(null);
        }
    }

    [Test]
    public async Task DeferredLambda_ShouldAddCalendarToScheduler()
    {
        try
        {
            ServiceCollection services = new ServiceCollection();
            services.AddLogging();

            services.AddQuartz((q, sp) =>
            {
                q.SchedulerName = "CalendarTest";
                q.UseInMemoryStore();
                q.AddCalendar<TestCalendar>("test-cal", true, false, cal =>
                {
                    cal.Description = "Integration test calendar";
                });
            });

            await using ServiceProvider provider = services.BuildServiceProvider();
            ISchedulerFactory factory = provider.GetRequiredService<ISchedulerFactory>();
            IScheduler scheduler = await factory.GetScheduler();

            ICalendar cal = await scheduler.GetCalendar("test-cal");
            cal.Should().NotBeNull();
            cal!.Description.Should().Be("Integration test calendar");

            await scheduler.Shutdown();
        }
        finally
        {
            LogProvider.SetCurrentLogProvider(null);
        }
    }

    // --- API proof: end-to-end scenarios from the issue ---

    [Test]
    public void ApiProof_ConnectionStringFromService()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IConnectionStringProvider>(new TestConnectionStringProvider("Server=production;Database=quartz;Encrypt=true"));

        services.AddQuartz((q, sp) =>
        {
            IConnectionStringProvider connFactory = sp.GetRequiredService<IConnectionStringProvider>();
            q.UsePersistentStore(s =>
            {
                s.UseSqlServer(sqlServer =>
                {
                    sqlServer.ConnectionString = connFactory.GetConnectionString();
                });
            });
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        QuartzOptions options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options[$"quartz.dataSource.{SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName}.connectionString"]
            .Should().Be("Server=production;Database=quartz;Encrypt=true");
    }

    [Test]
    public void ApiProof_OptionsPatternForSchedulerConfiguration()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.Configure<TestSchedulerConfig>(c =>
        {
            c.Name = "OptionsScheduler";
            c.MaxBatch = 10;
        });

        services.AddQuartz((q, sp) =>
        {
            TestSchedulerConfig config = sp.GetRequiredService<IOptions<TestSchedulerConfig>>().Value;
            q.SchedulerName = config.Name;
            q.MaxBatchSize = config.MaxBatch;

            q.AddJob<TestJob>(j => j.WithIdentity("optionsJob"));
            q.AddTrigger(t => t
                .ForJob("optionsJob")
                .WithIdentity("optionsTrigger")
                .WithSimpleSchedule(s => s.WithRepeatCount(0)));
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        QuartzOptions options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options[StdSchedulerFactory.PropertySchedulerInstanceName].Should().Be("OptionsScheduler");
        options[StdSchedulerFactory.PropertySchedulerMaxBatchSize].Should().Be("10");
        options.JobDetails.Should().HaveCount(1);
        options.Triggers.Should().HaveCount(1);
    }

    [Test]
    public async Task ApiProof_FullSchedulerCreationWithServiceResolvedConfig()
    {
        try
        {
            ServiceCollection services = new ServiceCollection();
            services.AddLogging();
            services.Configure<TestSchedulerConfig>(c =>
            {
                c.Name = "ServiceResolvedScheduler";
                c.MaxBatch = 3;
            });

            services.AddQuartz((q, sp) =>
            {
                TestSchedulerConfig config = sp.GetRequiredService<IOptions<TestSchedulerConfig>>().Value;
                q.SchedulerName = config.Name;
                q.MaxBatchSize = config.MaxBatch;
                q.UseInMemoryStore();

                q.ScheduleJob<TestJob>(
                    t => t.WithIdentity("apiProofTrigger").StartNow().WithSimpleSchedule(s => s.WithRepeatCount(0)),
                    j => j.WithIdentity("apiProofJob"));
            });

            await using ServiceProvider provider = services.BuildServiceProvider();
            ISchedulerFactory factory = provider.GetRequiredService<ISchedulerFactory>();
            IScheduler scheduler = await factory.GetScheduler();

            scheduler.SchedulerName.Should().Be("ServiceResolvedScheduler");

            IJobDetail job = await scheduler.GetJobDetail(new JobKey("apiProofJob"));
            job.Should().NotBeNull();

            await scheduler.Shutdown();
        }
        finally
        {
            LogProvider.SetCurrentLogProvider(null);
        }
    }

    // --- Test helpers ---

    private interface IConnectionStringProvider
    {
        string GetConnectionString();
    }

    private sealed class TestConnectionStringProvider : IConnectionStringProvider
    {
        private readonly string connectionString;

        public TestConnectionStringProvider(string connectionString)
        {
            this.connectionString = connectionString;
        }

        public string GetConnectionString() => connectionString;
    }

    private sealed class TestSchedulerConfig
    {
        public string Name { get; set; } = "DefaultName";
        public int MaxBatch { get; set; } = 1;
    }

    private sealed class TestJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }

    private sealed class TestCalendar : ICalendar
    {
        public string Description { get; set; }
        public ICalendar CalendarBase { get; set; }
        public ICalendar Clone() => this;
        public DateTimeOffset GetNextIncludedTimeUtc(DateTimeOffset timeUtc) => timeUtc;
        public bool IsTimeIncluded(DateTimeOffset timeUtc) => true;
    }

    private sealed class TestSchedulerListener : Quartz.Listener.SchedulerListenerSupport
    {
    }

    private sealed class TestJobListener : Quartz.Listener.JobListenerSupport
    {
        public override string Name => nameof(TestJobListener);
    }
}
