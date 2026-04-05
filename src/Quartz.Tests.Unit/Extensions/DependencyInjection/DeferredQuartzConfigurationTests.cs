using FluentAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Extensions.DependencyInjection;

[NonParallelizable]
public sealed class DeferredQuartzConfigurationTests
{
    // --- Property configuration ---

    [Test]
    public void DeferredLambda_ShouldSetSchedulerProperties()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddQuartz((q, sp) =>
        {
            q.SchedulerName = "DeferredScheduler";
            q.SchedulerId = "DEFERRED_01";
            q.MaxBatchSize = 7;
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options[StdSchedulerFactory.PropertySchedulerInstanceName].Should().Be("DeferredScheduler");
        options[StdSchedulerFactory.PropertySchedulerInstanceId].Should().Be("DEFERRED_01");
        options[StdSchedulerFactory.PropertySchedulerMaxBatchSize].Should().Be("7");
    }

    [Test]
    public void DeferredLambda_ShouldResolveServicesFromProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IConnectionStringProvider>(new TestConnectionStringProvider("Server=test;Database=quartz"));

        services.AddQuartz((q, sp) =>
        {
            var connProvider = sp.GetRequiredService<IConnectionStringProvider>();
            q.SetProperty("quartz.custom.connectionString", connProvider.GetConnectionString());
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options["quartz.custom.connectionString"].Should().Be("Server=test;Database=quartz");
    }

    [Test]
    public void DeferredLambda_ShouldConfigurePersistentStoreConnectionString()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IConnectionStringProvider>(new TestConnectionStringProvider("Server=myhost;Database=quartz;User=admin"));

        services.AddQuartz((q, sp) =>
        {
            var connProvider = sp.GetRequiredService<IConnectionStringProvider>();
            q.UsePersistentStore(s =>
            {
                s.UseSqlServer("default", c =>
                {
                    c.ConnectionString = connProvider.GetConnectionString();
                });
            });
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options["quartz.dataSource.default.connectionString"]
            .Should().Be("Server=myhost;Database=quartz;User=admin");
    }

    [Test]
    public void DeferredLambda_WithIOptionsPattern_ShouldWork()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.Configure<TestSchedulerConfig>(c =>
        {
            c.Name = "FromOptions";
            c.MaxBatch = 5;
        });

        services.AddQuartz((q, sp) =>
        {
            var config = sp.GetRequiredService<IOptions<TestSchedulerConfig>>().Value;
            q.SchedulerName = config.Name;
            q.MaxBatchSize = config.MaxBatch;
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options[StdSchedulerFactory.PropertySchedulerInstanceName].Should().Be("FromOptions");
        options[StdSchedulerFactory.PropertySchedulerMaxBatchSize].Should().Be("5");
    }

    [Test]
    public void DeferredLambda_WithNameValueCollectionProperties_ShouldMerge()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        var properties = new System.Collections.Specialized.NameValueCollection
        {
            { "quartz.custom.initial", "fromProperties" }
        };

        services.AddQuartz(properties, (q, sp) =>
        {
            q.SetProperty("quartz.custom.deferred", "fromDeferred");
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options["quartz.custom.initial"].Should().Be("fromProperties");
        options["quartz.custom.deferred"].Should().Be("fromDeferred");
    }

    // --- AddJob / AddTrigger / ScheduleJob ---

    [Test]
    public void DeferredLambda_AddJob_ShouldRegisterJobDetail()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddQuartz((q, sp) =>
        {
            q.AddJob<TestJob>(j => j.WithIdentity("deferredJob", "group1"));
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options.JobDetails.Should().HaveCount(1);
        options.JobDetails[0].Key.Name.Should().Be("deferredJob");
        options.JobDetails[0].Key.Group.Should().Be("group1");
    }

    [Test]
    public void DeferredLambda_AddTrigger_ShouldRegisterTrigger()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddQuartz((q, sp) =>
        {
            q.AddTrigger(t => t
                .ForJob("someJob", "group1")
                .WithIdentity("deferredTrigger", "group1")
                .StartNow());
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options.Triggers.Should().HaveCount(1);
        options.Triggers[0].Key.Name.Should().Be("deferredTrigger");
        options.Triggers[0].Key.Group.Should().Be("group1");
    }

    [Test]
    public void DeferredLambda_ScheduleJob_ShouldRegisterBothJobAndTrigger()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddQuartz((q, sp) =>
        {
            q.ScheduleJob<TestJob>(
                t => t.WithIdentity("scheduledTrigger", "group1"),
                j => j.WithIdentity("scheduledJob", "group1"));
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options.JobDetails.Should().HaveCount(1);
        options.Triggers.Should().HaveCount(1);
        options.JobDetails[0].Key.Name.Should().Be("scheduledJob");
        options.Triggers[0].Key.Name.Should().Be("scheduledTrigger");
        options.Triggers[0].JobKey.Should().Be(options.JobDetails[0].Key);
    }

    [Test]
    public void DeferredLambda_AddCalendar_ShouldCaptureCalendar()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddQuartz((q, sp) =>
        {
            q.AddCalendar<TestCalendar>("deferred-cal", true, true, cal =>
            {
                cal.Description = "Deferred calendar";
            });
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options._deferredCalendars.Should().HaveCount(1);
        options._deferredCalendars[0].Name.Should().Be("deferred-cal");
        options._deferredCalendars[0].Calendar.Description.Should().Be("Deferred calendar");
    }

    // --- Listener configuration ---

    [Test]
    public void DeferredLambda_AddSchedulerListener_ShouldCaptureInDeferredList()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddQuartz((q, sp) =>
        {
            q.AddSchedulerListener<TestSchedulerListener>();
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options._deferredSchedulerListeners.Should().HaveCount(1);
        options._deferredSchedulerListeners[0].ListenerType.Should().Be(typeof(TestSchedulerListener));
    }

    // --- Named scheduler support ---

    [Test]
    public void DeferredLambda_NamedScheduler_ShouldIsolateOptions()
    {
        var services = new ServiceCollection();
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

        using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<QuartzOptions>>();

        var options1 = monitor.Get("Sched1");
        var options2 = monitor.Get("Sched2");

        options1.JobDetails.Should().HaveCount(1);
        options1.JobDetails[0].Key.Name.Should().Be("job1");

        options2.JobDetails.Should().HaveCount(1);
        options2.JobDetails[0].Key.Name.Should().Be("job2");
    }

    // --- Combined immediate + deferred ---

    [Test]
    public void DeferredLambda_ShouldOverrideImmediateProperties()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddQuartz(q => { q.SchedulerName = "ImmediateName"; });

        services.AddQuartz((q, sp) => { q.SchedulerName = "DeferredName"; });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options[StdSchedulerFactory.PropertySchedulerInstanceName].Should().Be("DeferredName");
    }

    [Test]
    public void DeferredLambda_ShouldAccumulateJobsWithImmediate()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddQuartz(q =>
        {
            q.AddJob<TestJob>(j => j.WithIdentity("immediateJob", "group1"));
        });

        services.AddQuartz((q, sp) =>
        {
            q.AddJob<TestJob>(j => j.WithIdentity("deferredJob", "group1"));
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options.JobDetails.Should().HaveCount(2);
        options.JobDetails.Select(j => j.Key.Name).Should().Contain(["immediateJob", "deferredJob"]);
    }

    // --- Integration: scheduler creation ---

    [Test]
    public async Task DeferredLambda_ShouldCreateSchedulerWithDeferredProperties()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddQuartz((q, sp) =>
        {
            q.SchedulerName = "DeferredCreation";
            q.UseInMemoryStore();
        });

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<ISchedulerFactory>();
        var scheduler = await factory.GetScheduler();

        scheduler.SchedulerName.Should().Be("DeferredCreation");
        await scheduler.Shutdown();
    }

    [Test]
    public async Task DeferredLambda_ShouldWireListenerToScheduler()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddQuartz((q, sp) =>
        {
            q.SchedulerName = "ListenerTest";
            q.UseInMemoryStore();
            q.AddSchedulerListener<TestSchedulerListener>();
        });

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<ISchedulerFactory>();
        var scheduler = await factory.GetScheduler();

        var listeners = scheduler.ListenerManager.GetSchedulerListeners();
        listeners.Should().Contain(l => l is TestSchedulerListener);
        await scheduler.Shutdown();
    }

    [Test]
    public async Task DeferredLambda_ShouldWireJobListenerToScheduler()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddQuartz((q, sp) =>
        {
            q.SchedulerName = "JobListenerTest";
            q.UseInMemoryStore();
            q.AddJobListener<TestJobListener>();
        });

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<ISchedulerFactory>();
        var scheduler = await factory.GetScheduler();

        var listeners = scheduler.ListenerManager.GetJobListeners();
        listeners.Should().Contain(l => l is TestJobListener);
        listeners.Where(l => l is TestJobListener).Should().HaveCount(1);
        await scheduler.Shutdown();
    }

    // --- Deferred UsePersistentStore sets properties correctly ---

    [Test]
    public void DeferredLambda_UsePersistentStore_ShouldSetAllExpectedProperties()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IConnectionStringProvider>(new TestConnectionStringProvider("Server=deferred;Database=quartz"));

        services.AddQuartz((q, sp) =>
        {
            var connProvider = sp.GetRequiredService<IConnectionStringProvider>();
            q.UsePersistentStore(s =>
            {
                s.UseSqlServer("default", c =>
                {
                    c.ConnectionString = connProvider.GetConnectionString();
                });
                s.UseClustering();
            });
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        // Verify property-based configuration works correctly in deferred context
        options["quartz.dataSource.default.connectionString"].Should().Be("Server=deferred;Database=quartz");
        options["quartz.jobStore.type"].Should().NotBeNullOrWhiteSpace();
        options["quartz.jobStore.driverDelegateType"].Should().NotBeNullOrWhiteSpace();
        options["quartz.jobStore.clustered"].Should().Be("true");
    }

    // --- API proof ---

    [Test]
    public void ApiProof_ConnectionStringFromService()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IConnectionStringProvider>(new TestConnectionStringProvider("Server=production;Database=quartz;Encrypt=true"));

        services.AddQuartz((q, sp) =>
        {
            var connFactory = sp.GetRequiredService<IConnectionStringProvider>();
            q.UsePersistentStore(s =>
            {
                s.UseSqlServer("default", c =>
                {
                    c.ConnectionString = connFactory.GetConnectionString();
                });
            });
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options["quartz.dataSource.default.connectionString"]
            .Should().Be("Server=production;Database=quartz;Encrypt=true");
    }

    [Test]
    public async Task ApiProof_FullSchedulerCreationWithServiceResolvedConfig()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<TestSchedulerConfig>(c =>
        {
            c.Name = "ServiceResolvedScheduler";
            c.MaxBatch = 3;
        });

        services.AddQuartz((q, sp) =>
        {
            var config = sp.GetRequiredService<IOptions<TestSchedulerConfig>>().Value;
            q.SchedulerName = config.Name;
            q.MaxBatchSize = config.MaxBatch;
            q.UseInMemoryStore();

            q.ScheduleJob<TestJob>(
                t => t.WithIdentity("apiProofTrigger").StartNow().WithSimpleSchedule(s => s.WithRepeatCount(0)),
                j => j.WithIdentity("apiProofJob"));
        });

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<ISchedulerFactory>();
        var scheduler = await factory.GetScheduler();

        scheduler.SchedulerName.Should().Be("ServiceResolvedScheduler");

        var job = await scheduler.GetJobDetail(new JobKey("apiProofJob"));
        job.Should().NotBeNull();
        await scheduler.Shutdown();
    }

    // --- Test helpers ---

    private interface IConnectionStringProvider
    {
        string GetConnectionString();
    }

    private sealed class TestConnectionStringProvider(string connectionString) : IConnectionStringProvider
    {
        public string GetConnectionString() => connectionString;
    }

    private sealed class TestSchedulerConfig
    {
        public string Name { get; set; } = "DefaultName";
        public int MaxBatch { get; set; } = 1;
    }

    private sealed class TestJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context) => default;
    }

    private sealed class TestCalendar : ICalendar
    {
        public string Description { get; set; } = "";
        public ICalendar CalendarBase { get; set; }
        public ICalendar Clone() => this;
        public DateTimeOffset GetNextIncludedTimeUtc(DateTimeOffset timeUtc) => timeUtc;
        public bool IsTimeIncluded(DateTimeOffset timeUtc) => true;
    }

    private sealed class TestSchedulerListener : Listener.SchedulerListenerSupport;

    private sealed class TestJobListener : Listener.JobListenerSupport
    {
        public override string Name => nameof(TestJobListener);
    }
}
