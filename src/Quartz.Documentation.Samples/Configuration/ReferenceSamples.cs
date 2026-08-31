using System.Collections.Specialized;
using System.Data.Common;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Documentation.Samples.Configuration;

// NightlyRollupJob lives in the parent namespace, which this one already sees.

/// <summary>
/// Samples for docs/documentation/quartz-4.x/configuration/reference.md.
/// </summary>
public static class ReferenceSamples
{
    public static void OneSchedulerOption(IServiceCollection services)
    {
        #region sample_reference_one_option

        services.AddQuartz(q => q.ConfigureScheduler(options => options.MaxBatchSize = 5));

        #endregion
    }

    public static void SchedulerOptions(IServiceCollection services)
    {
        #region sample_reference_scheduler_options

        services.AddQuartz(q => q.ConfigureScheduler(options =>
        {
            options.InstanceName = "core";
            options.InstanceId = "node-1";
            options.MaxBatchSize = 5;
            options.ShutdownJobInterruption = ShutdownJobInterruption.Always;
        }));

        #endregion
    }

    public static void DefaultThreadPool(IServiceCollection services)
    {
        #region sample_reference_default_thread_pool

        services.AddQuartz(q => q.UseDefaultThreadPool(maxConcurrency: 20));

        #endregion
    }

    public static void ThreadPoolOfYourOwn(IServiceCollection services)
    {
        #region sample_reference_thread_pool_of_your_own

        services.AddQuartz(q => q.UseThreadPool<MyThreadPool>());

        #endregion
    }

    public static void ThreadPoolWithOptions(IServiceCollection services)
    {
        #region sample_reference_thread_pool_options

        services.AddQuartz(q =>
        {
            q.ConfigureOptions<MyThreadPoolOptions>(options => options.Slots = 20);
            q.UseThreadPool<MyThreadPool>();
        });

        #endregion
    }

    public static void InMemoryStore(IServiceCollection services)
    {
        #region sample_reference_in_memory_store

        services.AddQuartz(q => q.UseInMemoryStore(options => options.MisfireThreshold = TimeSpan.FromSeconds(30)));

        #endregion
    }

    public static void PersistentStore(IServiceCollection services, string connectionString)
    {
        #region sample_reference_persistent_store

        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.UseSqlServer(connectionString);
            store.UseSystemTextJsonSerializer();
        }));

        #endregion
    }

    public static void ConnectionStringOrName(IPersistentStoreBuilder store, string connectionString)
    {
        #region sample_reference_connection_string

        store.UseSqlServer(connectionString);
        store.UseSqlServer(db => db.ConnectionStringName = "Scheduler");

        #endregion
    }

    public static void GenericDatabase(IPersistentStoreBuilder store, string connectionString)
    {
        #region sample_reference_generic_database

        store.UseGenericDatabase("MyDatabase", connectionString, () => new DbMetadata
        {
            ProductName = "My Database",
            AssemblyName = typeof(MyConnection).Assembly.FullName,
            ConnectionType = typeof(MyConnection),
            CommandType = typeof(MyCommand),
            ParameterType = typeof(MyParameter),
            ParameterDbType = typeof(MyDbType),
            ParameterDbTypePropertyName = nameof(MyParameter.MyDbType),
            ParameterNamePrefix = "@",
            ExceptionType = typeof(MyException),
            UseParameterNamePrefixInParameterCollection = true,
            BindByName = true,
            DbBinaryTypeName = "VarBinary",
        });

        #endregion
    }

    public static void DataSourceFactory(IPersistentStoreBuilder store)
    {
        #region sample_reference_data_source_factory

        store.UsePostgres(db => db.DataSourceFactory = _ => BuildDataSource());

        #endregion
    }

    public static void ConnectionProvider(IServiceCollection services, string connectionString)
    {
        #region sample_reference_connection_provider

        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.UseSqlServer(connectionString);          // still selects the driver delegate
            store.UseConnectionProvider<MyDbProvider>();   // …but connections come from here
        }));

        #endregion
    }

    public static void Clustering(IServiceCollection services, string connectionString)
    {
        #region sample_reference_clustering

        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "core";
                options.GenerateInstanceId = true;
            });

            q.UsePersistentStore(store =>
            {
                store.UseSqlServer(connectionString);
                store.UseClustering(cluster =>
                {
                    cluster.CheckinInterval = TimeSpan.FromSeconds(10);
                    cluster.CheckinMisfireThreshold = TimeSpan.FromSeconds(20);
                });
                store.UseSystemTextJsonSerializer();
            });
        });

        #endregion
    }

    public static void Serializers(IPersistentStoreBuilder store)
    {
        #region sample_reference_serializers

        store.UseSystemTextJsonSerializer();
        store.UseNewtonsoftJsonSerializer();   // Quartz.Serialization.Newtonsoft

        #endregion
    }

    public static void SchedulingOptions(IServiceCollection services)
    {
        #region sample_reference_scheduling_options

        services.Configure<QuartzOptions>(options => options.Scheduling.IgnoreDuplicates = true);

        #endregion
    }

    public static void JobFactory(IServiceCollection services)
    {
        #region sample_reference_job_factory

        services.AddQuartz(q => q.UseJobFactory<MyJobFactory>());

        #endregion
    }

    public static void JobScope(IServiceCollection services)
    {
        #region sample_reference_job_scope

        services.AddQuartz(q => q.ConfigureJobScope((scope, bundle, scheduler) => { /* … */ }));

        #endregion
    }

    public static void ListenersAndPlugins(IServiceCollection services)
    {
        #region sample_reference_listeners_and_plugins

        services.AddQuartz(q =>
        {
            q.AddSchedulerListener<MySchedulerListener>();
            q.AddJobListener<MyJobListener>(GroupMatcher<JobKey>.GroupEquals("reports"));
            q.AddTriggerListener<MyTriggerListener>();
            q.AddPlugin<MyPlugin>();
        });

        #endregion
    }

    public static void NamedSchedulers(IServiceCollection services, string reportingDb)
    {
        #region sample_reference_named_schedulers

        services.AddQuartz("reporting", q => q.UsePersistentStore(store => store.UseSqlServer(reportingDb)));
        services.AddQuartz("ingest", q => q.UseInMemoryStore());

        #endregion
    }

    public static void TypeLoaderAliases(IServiceCollection services)
    {
        #region sample_reference_type_loader_aliases

        services.AddQuartz(q => q.UseTypeLoader(loader =>
            loader.Map("Acme.Jobs.NightlyReport, Acme.Jobs", typeof(NightlyRollupJob))));

        #endregion
    }

    public static async ValueTask ResolvingANamedScheduler(IServiceProvider serviceProvider)
    {
        #region sample_reference_resolving_a_named_scheduler

        var reporting = await serviceProvider
            .GetRequiredKeyedService<ISchedulerFactory>("reporting")
            .GetScheduler();

        #endregion
    }

    public static async ValueTask WithoutAContainer()
    {
        #region sample_reference_without_a_container

        IScheduler scheduler = await QuartzSchedulerBuilder.Create()
            .ConfigureScheduler(options => options.InstanceName = "reporting")
            .UseDefaultThreadPool(maxConcurrency: 20)
            .UseInMemoryStore()
            .BuildScheduler();

        #endregion
    }

    public static async ValueTask FromFlatProperties(NameValueCollection properties)
    {
        #region sample_reference_from_flat_properties

        IScheduler scheduler = await QuartzSchedulerBuilder.Create()
            .UseProperties(properties)
            .BuildScheduler();

        #endregion
    }

    private static DbDataSource BuildDataSource() => null!;
}
