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

using System.Collections.Specialized;
using System.Diagnostics;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz
{
    /// <summary>
    /// Helper to create common scheduler configurations.
    /// </summary>
    public class SchedulerBuilder : PropertiesHolder, IPropertyConfigurationRoot
    {
        protected SchedulerBuilder(NameValueCollection? properties)
            : base(properties ?? new NameValueCollection())
        {
        }

        /// <summary>
        /// UNSTABLE API. Creates a new scheduler configuration to build desired setup.
        /// </summary>
        /// <param name="properties">Base properties, if any.</param>
        /// <returns>New scheduler builder instance that can be used to build configuration.</returns>
        public static SchedulerBuilder Create(NameValueCollection? properties = null)
        {
            return new SchedulerBuilder(properties);
        }

        /// <summary>
        /// UNSTABLE API. Creates a new scheduler configuration to build desired setup.
        /// </summary>
        public static SchedulerBuilder Create(string? id, string? name)
        {
            var builder = Create();
            if (!string.IsNullOrWhiteSpace(id) && id != null)
            {
                builder.SchedulerId = id;
            }

            if (!string.IsNullOrWhiteSpace(name) && name != null)
            {
                builder.SchedulerName = name;
            }
            return builder;
        }

        /// <summary>
        /// Sets the instance id of the scheduler (must be unique within a cluster).
        /// </summary>
        public SchedulerBuilder WithId(string id)
        {
            SchedulerId = id;
            return this;
        }

        /// <summary>
        /// Sets the instance name of the scheduler (must be unique within this server instance).
        /// </summary>
        public SchedulerBuilder WithName(string name)
        {
            SchedulerName = name;
            return this;
        }

        /// <summary>
        /// Sets the instance id of the scheduler (must be unique within a cluster).
        /// </summary>
        public string SchedulerId
        {
            set => SetProperty(StdSchedulerFactory.PropertySchedulerInstanceId, value);
        }

        /// <summary>
        /// Sets the instance name of the scheduler (must be unique within this server instance).
        /// </summary>
        public string SchedulerName
        {
            set => SetProperty(StdSchedulerFactory.PropertySchedulerInstanceName, value);
        }

        /// <summary>
        /// Use memory store, which does not survive process restarts/crashes.
        /// </summary>
        public SchedulerBuilder UseInMemoryStore(Action<InMemoryStoreOptions>? options = null)
        {
            SetProperty(StdSchedulerFactory.PropertyJobStoreType, typeof(RAMJobStore).AssemblyQualifiedNameWithoutVersion());
            options?.Invoke(new InMemoryStoreOptions(this));
            return this;
        }

        public SchedulerBuilder UsePersistentStore(Action<PersistentStoreOptions> options)
        {
            return UsePersistentStore<JobStoreTX>(options);
        }

        public SchedulerBuilder UsePersistentStore<T>(Action<PersistentStoreOptions> options) where T : IJobStore
        {
            if (options == null)
            {
                ThrowHelper.ThrowArgumentNullException(nameof(options));
            }

            options(new PersistentStoreOptions(this, typeof(T)));
            return this;
        }

        public virtual SchedulerBuilder UseJobFactory<T>() where T : IJobFactory
        {
            SetProperty(StdSchedulerFactory.PropertySchedulerJobFactoryType, typeof(T).AssemblyQualifiedNameWithoutVersion());
            return this;
        }

        public virtual SchedulerBuilder UseTypeLoader<T>() where T : ITypeLoadHelper
        {
            SetProperty(StdSchedulerFactory.PropertySchedulerTypeLoadHelperType, typeof(T).AssemblyQualifiedNameWithoutVersion());
            return this;
        }

        /// <summary>
        /// Finalizes the configuration and builds the scheduler factoryh.
        /// </summary>
        public StdSchedulerFactory Build()
        {
            return new StdSchedulerFactory(Properties);
        }

        /// <summary>
        /// Finalizes the configuration and builds the actual scheduler.
        /// </summary>
        public ValueTask<IScheduler> BuildScheduler()
        {
            var schedulerFactory = new StdSchedulerFactory(Properties);
            return schedulerFactory.GetScheduler();
        }

        /// <summary>
        /// Uses the default thread pool, which uses the default task scheduler.
        /// </summary>
        public SchedulerBuilder UseThreadPool<T>(Action<ThreadPoolOptions>? configure = null) where T : IThreadPool
        {
            SetProperty("quartz.threadPool.type", typeof(T).AssemblyQualifiedNameWithoutVersion());
            configure?.Invoke(new ThreadPoolOptions(this));
            return this;
        }

        /// <summary>
        /// Uses the zero size thread pool, which is used only for database administration nodes.
        /// </summary>
        public SchedulerBuilder UseZeroSizeThreadPool(Action<ThreadPoolOptions>? configure = null)
        {
            UseThreadPool<ZeroSizeThreadPool>(configure);
            return this;
        }

        /// <summary>
        /// Uses the default thread pool, which uses the default task scheduler.
        /// </summary>
        public SchedulerBuilder UseDefaultThreadPool(Action<ThreadPoolOptions>? configure = null)
        {
            return UseDefaultThreadPool(TaskSchedulingThreadPool.DefaultMaxConcurrency, configure);
        }

        /// <summary>
        /// Uses the default thread pool, which uses the default task scheduler.
        /// </summary>
        public SchedulerBuilder UseDefaultThreadPool(int maxConcurrency, Action<ThreadPoolOptions>? configure = null)
        {
            UseThreadPool<DefaultThreadPool>(options =>
            {
                options.MaxConcurrency = maxConcurrency;
                configure?.Invoke(options);
            });
            return this;
        }

        /// <summary>
        /// Uses a dedicated thread pool, which uses own threads instead of task scheduler shared pool.
        /// </summary>
        public SchedulerBuilder UseDedicatedThreadPool(Action<ThreadPoolOptions>? configure = null)
        {
            UseThreadPool<DedicatedThreadPool>(configure);
            return this;
        }

#if REMOTING
        /// <summary>
        /// Makes this scheduler a proxy that calls another scheduler instance via remote invocation
        /// using the default mechanism (for full .NET Framework it's remoting, otherwise unsupported).
        /// </summary>
        /// <param name="address">Connection address</param>
        public SchedulerBuilder ProxyToRemoteScheduler(string address)
        {
            return ProxyToRemoteScheduler<RemotingSchedulerProxyFactory>(address);
        }
#endif // REMOTING

        /// <summary>
        /// Makes this scheduler a proxy that calls another scheduler instance via remote invocation
        /// using the typeof T proxy generator.
        /// </summary>
        /// <param name="address">Connection address</param>
        public SchedulerBuilder ProxyToRemoteScheduler<T>(string address) where T : IRemotableSchedulerProxyFactory
        {
            SetProperty(StdSchedulerFactory.PropertySchedulerProxy, "true");
            SetProperty(StdSchedulerFactory.PropertySchedulerProxyType, typeof(T).AssemblyQualifiedNameWithoutVersion());
            SetProperty("quartz.scheduler.proxy.address", address);
            return this;
        }

        /// <inheritdoc cref="MisfireThreshold"/>
        public SchedulerBuilder WithMisfireThreshold(TimeSpan threshold)
        {
            MisfireThreshold = threshold;
            return this;
        }

        /// <summary>
        /// The time span by which a trigger must have missed its
        /// next-fire-time, in order for it to be considered "misfired" and thus
        /// have its misfire instruction applied.
        /// </summary>
        public TimeSpan MisfireThreshold
        {
            set => SetProperty("quartz.jobStore.misfireThreshold", ((int) value.TotalMilliseconds).ToString());
        }

        /// <inheritdoc cref="MaxBatchSize"/>
        public SchedulerBuilder WithMaxBatchSize(int batchSize)
        {
            MaxBatchSize = batchSize;
            return this;
        }

        /// <summary>
        /// The maximum number of triggers that a scheduler node is allowed to acquire (for firing) at once.
        /// Default value is 1.
        /// The larger the number, the more efficient firing is (in situations where there are very many triggers needing to be fired all at once) - but at the cost of possible imbalanced load between cluster nodes.
        /// </summary>
        public int MaxBatchSize
        {
            set => SetProperty(StdSchedulerFactory.PropertySchedulerMaxBatchSize, value.ToString());
        }

        /// <inheritdoc cref="BatchTriggerAcquisitionFireAheadTimeWindow"/>
        public SchedulerBuilder WithBatchTriggerAcquisitionFireAheadTimeWindow(TimeSpan timeWindow)
        {
            BatchTriggerAcquisitionFireAheadTimeWindow = timeWindow;
            return this;
        }

        /// <summary>
        /// The amount of time that a trigger is allowed to be acquired and fired ahead of its scheduled fire time.
        /// Defaults to TimeSpan.Zero.
        /// The larger the number, the more likely batch acquisition of triggers to fire will be able to select and fire more than 1 trigger at a time -at the cost of trigger schedule not being honored precisely (triggers may fire this amount early).
        /// This may be useful (for performance’s sake) in situations where the scheduler has very large numbers of triggers that need to be fired at or near the same time.
        /// </summary>
        public TimeSpan BatchTriggerAcquisitionFireAheadTimeWindow
        {
            set => SetProperty(StdSchedulerFactory.PropertySchedulerBatchTimeWindow, ((int) value.TotalMilliseconds).ToString());
        }

        /// <inheritdoc cref="InterruptJobsOnShutdown"/>
        public SchedulerBuilder WithInterruptJobsOnShutdown(bool interrupt)
        {
            InterruptJobsOnShutdown = interrupt;
            return this;
        }

        /// <summary>
        /// Whether to interrupt (cancel) job execution on shutdown.
        /// </summary>
        /// <remarks>
        /// Job needs to observe <see cref="IJobExecutionContext.CancellationToken"/>.
        /// </remarks>
        public bool InterruptJobsOnShutdown
        {
            set => SetProperty(StdSchedulerFactory.PropertySchedulerInterruptJobsOnShutdown, value.ToString());
        }

        /// <inheritdoc cref="InterruptJobsOnShutdownWithWait"/>
        public SchedulerBuilder WithInterruptJobsOnShutdownWithWait(bool interruptWithWait)
        {
            InterruptJobsOnShutdownWithWait = interruptWithWait;
            return this;
        }

        /// <summary>
        /// Whether to interrupt (cancel) job execution on shutdown when wait for jobs to completed has is specified.
        /// </summary>
        /// <remarks>
        /// Job needs to observe <see cref="IJobExecutionContext.CancellationToken"/>.
        /// </remarks>
        public bool InterruptJobsOnShutdownWithWait
        {
            set => SetProperty(StdSchedulerFactory.PropertySchedulerInterruptJobsOnShutdownWithWait, value.ToString());
        }

        public class ThreadPoolOptions : PropertiesHolder
        {
            protected internal ThreadPoolOptions(PropertiesHolder parent) : base(parent, "quartz.threadPool")
            {
            }

            /// <summary>
            /// The maximum number of thread pool tasks which can be executing in parallel.
            /// </summary>
            public int MaxConcurrency
            {
                set => SetProperty("maxConcurrency", value.ToString());
            }
        }

        public abstract class StoreOptions : PropertiesHolder
        {
            protected StoreOptions(PropertiesHolder parent) : base(parent)
            {
            }
        }

        public class PersistentStoreOptions : StoreOptions
        {
            internal PersistentStoreOptions(PropertiesHolder parent, Type jobStoreType) : base(parent)
            {
                SetProperty(StdSchedulerFactory.PropertyJobStoreType, jobStoreType.AssemblyQualifiedNameWithoutVersion());
            }

            /// <summary>
            /// Set whether string-only properties will be handled in JobDataMaps.
            /// </summary>
            public bool UseProperties
            {
                set => SetProperty("quartz.jobStore.useProperties", value.ToString().ToLowerInvariant());
            }

            /// <summary>
            /// Set whether database schema validated will be tried during scheduler initialization.
            /// </summary>
            /// <remarks>
            /// Optional feature and all providers do no support it.
            /// </remarks>
            public bool PerformSchemaValidation
            {
                set => SetProperty("quartz.jobStore.performSchemaValidation", value.ToString().ToLowerInvariant());
            }

            /// <summary>
            /// Sets the database retry interval.
            /// </summary>
            /// <remarks>
            /// Defaults to 15 seconds.
            /// </remarks>
            public TimeSpan RetryInterval
            {
                set => SetProperty(StdSchedulerFactory.PropertyJobStoreDbRetryInterval, ((int) value.TotalMilliseconds).ToString());
            }

            /// <summary>
            /// Make this instance is part of a cluster.
            /// </summary>
            public void UseClustering(Action<ClusterOptions>? options = null)
            {
                SetProperty("quartz.jobStore.clustered", "true");
                options?.Invoke(new ClusterOptions(this));
            }

            /// <summary>
            /// Configures persistence to use generic <see cref="StdAdoDelegate" />.
            /// </summary>
            /// <param name="provider">Valid provider name to configure driver details.</param>
            /// <param name="configurer">Callback to refine configuration.</param>
            /// <returns></returns>
            public void UseGenericDatabase(
                string provider,
                Action<AdoProviderOptions>? configurer = null)
            {
                SetProperty("quartz.jobStore.driverDelegateType", typeof(StdAdoDelegate).AssemblyQualifiedNameWithoutVersion());
                SetProperty("quartz.jobStore.dataSource", AdoProviderOptions.DefaultDataSourceName);
                SetProperty($"quartz.dataSource.{AdoProviderOptions.DefaultDataSourceName}.provider", provider);

                configurer?.Invoke(new AdoProviderOptions(this));
            }

            /// <summary>
            /// Configure binary serialization, consider using JSON instead which requires extra package Quartz.Serialization.Json.
            /// </summary>
            public void UseBinarySerializer()
            {
                UseSerializer<BinaryObjectSerializer>();
            }

            /// <summary>
            /// Use custom serializer.
            /// </summary>
            public void UseSerializer<T>() where T : IObjectSerializer
            {
                SetProperty("quartz.serializer.type", typeof(T).AssemblyQualifiedNameWithoutVersion());
            }
        }

        public class ClusterOptions : PropertiesHolder
        {
            protected internal ClusterOptions(PropertiesHolder parent) : base(parent)
            {
            }

            /// <summary>
            /// Sets the frequency at which this instance "checks-in"
            /// with the other instances of the cluster. -- Affects the rate of
            /// detecting failed instances.
            /// </summary>
            /// <remarks>
            /// Defaults to 7500 milliseconds.
            /// </remarks>
            public TimeSpan CheckinInterval
            {
                set => SetProperty("quartz.jobStore.clusterCheckinInterval", ((int) value.TotalMilliseconds).ToString());
            }

            /// <summary>
            /// The time span by which a check-in must have missed its
            /// next-fire-time, in order for it to be considered "misfired" and thus
            /// other scheduler instances in a cluster can consider a "misfired" scheduler
            /// instance as failed or dead.
            /// </summary>
            /// <remarks>
            /// Defaults to 7500 milliseconds.
            /// </remarks>
            public TimeSpan CheckinMisfireThreshold
            {
                set => SetProperty("quartz.jobStore.clusterCheckinMisfireThreshold", ((int) value.TotalMilliseconds).ToString());
            }
        }

        public class InMemoryStoreOptions : PropertiesHolder
        {
            protected internal InMemoryStoreOptions(SchedulerBuilder parent) : base(parent)
            {
            }
        }

        public class AdoProviderOptions
        {
            public const string DefaultDataSourceName = "default";

            private readonly PersistentStoreOptions options;

            protected internal AdoProviderOptions(PersistentStoreOptions options)
            {
                this.options = options;
            }

            /// <summary>
            /// The prefix that should be pre-pended to all table names, defaults to QRTZ_.
            /// </summary>
            public string TablePrefix
            {
                set => options.SetProperty("quartz.jobStore.tablePrefix", value);
            }

            /// <summary>
            /// Standard connection driver specific connection string.
            /// </summary>
            public string ConnectionString
            {
                set => options.SetProperty($"quartz.dataSource.{DefaultDataSourceName}.connectionString", value);
            }

            /// <summary>
            /// Use named connection defined in application configuration file.
            /// </summary>
            public string ConnectionStringName
            {
                set => options.SetProperty($"quartz.dataSource.{DefaultDataSourceName}.connectionStringName", value);
            }

            /// <summary>
            /// Use named connection defined in application configuration file.
            /// </summary>
            public void UseDriverDelegate<T>() where T : IDriverDelegate
            {
                options.SetProperty("quartz.jobStore.driverDelegateType", typeof(T).AssemblyQualifiedNameWithoutVersion());
            }

            /// <summary>
            /// Use given connection provider.
            /// </summary>
            public void UseConnectionProvider<T>() where T : IDbProvider
            {
                options.SetProperty($"quartz.dataSource.{DefaultDataSourceName}.connectionProvider.type", typeof(T).AssemblyQualifiedNameWithoutVersion());
            }
        }
    }

    public static class AdoProviderExtensions
    {
        public static void UseSqlServer(
            this SchedulerBuilder.PersistentStoreOptions options,
            string connectionString)
        {
            options.UseSqlServer(c => c.ConnectionString = connectionString);
        }

        public static void UseSqlServer(
            this SchedulerBuilder.PersistentStoreOptions options,
            Action<SchedulerBuilder.AdoProviderOptions> configurer)
        {
            options.SetProperty("quartz.jobStore.driverDelegateType", typeof(SqlServerDelegate).AssemblyQualifiedNameWithoutVersion());
            options.SetProperty("quartz.jobStore.dataSource", SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName);
            options.SetProperty($"quartz.dataSource.{SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName}.provider", "SqlServer");

            var adoProviderOptions = new SchedulerBuilder.AdoProviderOptions(options);
            configurer.Invoke(adoProviderOptions);
        }

        public static void UsePostgres(
            this SchedulerBuilder.PersistentStoreOptions options,
            string connectionString)
        {
            options.UsePostgres(c => c.ConnectionString = connectionString);
        }

        public static void UsePostgres(
            this SchedulerBuilder.PersistentStoreOptions options,
            Action<SchedulerBuilder.AdoProviderOptions> configurer)
        {
            options.SetProperty("quartz.jobStore.driverDelegateType", typeof(PostgreSQLDelegate).AssemblyQualifiedNameWithoutVersion());
            options.SetProperty("quartz.jobStore.dataSource", SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName);
            options.SetProperty($"quartz.dataSource.{SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName}.provider", "Npgsql");

            var adoProviderOptions = new SchedulerBuilder.AdoProviderOptions(options);
            configurer.Invoke(adoProviderOptions);
        }

        public static void UseMySql(
            this SchedulerBuilder.PersistentStoreOptions options,
            string connectionString)
        {
            options.UseMySql(c => c.ConnectionString = connectionString);
        }

        public static void UseMySql(
            this SchedulerBuilder.PersistentStoreOptions options,
            Action<SchedulerBuilder.AdoProviderOptions> configurer)
        {
            UseMySqlInternal(options, "MySql", configurer);
        }

        public static void UseMySqlConnector(
            this SchedulerBuilder.PersistentStoreOptions options,
            Action<SchedulerBuilder.AdoProviderOptions> configurer)
        {
            UseMySqlInternal(options, "MySqlConnector", configurer);
        }

        internal static void UseMySqlInternal(
            this SchedulerBuilder.PersistentStoreOptions options,
            string provider,
            Action<SchedulerBuilder.AdoProviderOptions> configurer)
        {
            options.SetProperty("quartz.jobStore.driverDelegateType", typeof(MySQLDelegate).AssemblyQualifiedNameWithoutVersion());
            options.SetProperty("quartz.jobStore.dataSource", SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName);
            options.SetProperty($"quartz.dataSource.{SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName}.provider", provider);

            var adoProviderOptions = new SchedulerBuilder.AdoProviderOptions(options);
            configurer.Invoke(adoProviderOptions);
        }

        public static void UseFirebird(
            this SchedulerBuilder.PersistentStoreOptions options,
            string connectionString)
        {
            options.UseFirebird(c => c.ConnectionString = connectionString);
        }

        public static void UseFirebird(
            this SchedulerBuilder.PersistentStoreOptions options,
            Action<SchedulerBuilder.AdoProviderOptions> configurer)
        {
            options.SetProperty("quartz.jobStore.driverDelegateType", typeof(FirebirdDelegate).AssemblyQualifiedNameWithoutVersion());
            options.SetProperty("quartz.jobStore.dataSource", SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName);
            options.SetProperty($"quartz.dataSource.{SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName}.provider", "Firebird");

            var adoProviderOptions = new SchedulerBuilder.AdoProviderOptions(options);
            configurer.Invoke(adoProviderOptions);
        }

        public static void UseOracle(
            this SchedulerBuilder.PersistentStoreOptions options,
            string connectionString)
        {
            options.UseOracle(c => c.ConnectionString = connectionString);
        }

        public static void UseOracle(
            this SchedulerBuilder.PersistentStoreOptions options,
            Action<SchedulerBuilder.AdoProviderOptions> configurer)
        {
            options.SetProperty("quartz.jobStore.driverDelegateType", typeof(OracleDelegate).AssemblyQualifiedNameWithoutVersion());
            options.SetProperty("quartz.jobStore.dataSource", SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName);
            options.SetProperty($"quartz.dataSource.{SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName}.provider", "OracleODPManaged");

            var adoProviderOptions = new SchedulerBuilder.AdoProviderOptions(options);
            configurer.Invoke(adoProviderOptions);
        }

        /// <summary>
        /// Configures the scheduler to use System.Data.Sqlite data source provider.
        /// </summary>
        public static void UseSQLite(
            this SchedulerBuilder.PersistentStoreOptions options,
            string connectionString)
        {
            options.UseSQLite(c => c.ConnectionString = connectionString);
        }

        /// <summary>
        /// Configures the scheduler to use System.Data.Sqlite data source provider.
        /// </summary>
        public static void UseSQLite(
            this SchedulerBuilder.PersistentStoreOptions options,
            Action<SchedulerBuilder.AdoProviderOptions> configurer)
        {
            options.UseSQLite("SQLite", configurer);
        }

        /// <summary>
        /// Configures the scheduler to use Microsoft.Data.Sqlite data source provider.
        /// </summary>
        public static void UseMicrosoftSQLite(
            this SchedulerBuilder.PersistentStoreOptions options,
            string connectionString)
        {
            options.UseMicrosoftSQLite(c => c.ConnectionString = connectionString);
        }

        /// <summary>
        /// Configures the scheduler to use System.Data.Sqlite data source provider.
        /// </summary>
        public static void UseMicrosoftSQLite(
            this SchedulerBuilder.PersistentStoreOptions options,
            Action<SchedulerBuilder.AdoProviderOptions> configurer)
        {
            options.UseSQLite("SQLite-Microsoft", configurer);
        }

        private static void UseSQLite(
            this SchedulerBuilder.PersistentStoreOptions options,
            string provider,
            Action<SchedulerBuilder.AdoProviderOptions> configurer)
        {
            options.SetProperty("quartz.jobStore.driverDelegateType", typeof(SQLiteDelegate).AssemblyQualifiedNameWithoutVersion());
            options.SetProperty("quartz.jobStore.dataSource", SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName);
            options.SetProperty($"quartz.dataSource.{SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName}.provider", provider);

            var adoProviderOptions = new SchedulerBuilder.AdoProviderOptions(options);
            configurer.Invoke(adoProviderOptions);
        }
    }
}