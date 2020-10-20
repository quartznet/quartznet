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

using System;
using System.Collections.Specialized;
using System.Threading.Tasks;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
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
        public static SchedulerBuilder Create(string id, string name)
        {
            var builder = Create();
            builder.SchedulerId = id;
            builder.SchedulerName = name;
            return builder;
        }

        /// <summary>
        /// Sets the instance id of the scheduler (must be unique within a cluster).
        /// </summary>
        public string SchedulerId
        {
            set => SetProperty(StdSchedulerFactory.PropertySchedulerInstanceId, value);
        }

        /// <summary>
        /// Sets the instance id of the scheduler (must be unique within this server instance).
        /// </summary>
        public string SchedulerName
        {
            set => SetProperty(StdSchedulerFactory.PropertySchedulerInstanceName, value);
        }

        /// <summary>
        /// Use memory store, which does not survive process restarts/crashes.
        /// </summary>
        public void UseInMemoryStore(Action<InMemoryStoreOptions>? options = null)
        {
            SetProperty(StdSchedulerFactory.PropertyJobStoreType, typeof(RAMJobStore).AssemblyQualifiedNameWithoutVersion());
            options?.Invoke(new InMemoryStoreOptions(this));
        }

        public void UsePersistentStore(Action<PersistentStoreOptions> options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            options?.Invoke(new PersistentStoreOptions(this));
        }

        public virtual void UseJobFactory<T>() where T : IJobFactory
        {
            SetProperty(StdSchedulerFactory.PropertySchedulerJobFactoryType, typeof(T).AssemblyQualifiedNameWithoutVersion());
        }

        public virtual void UseTypeLoader<T>() where T : ITypeLoadHelper
        {
            SetProperty(StdSchedulerFactory.PropertySchedulerTypeLoadHelperType, typeof(T).AssemblyQualifiedNameWithoutVersion());
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
        public Task<IScheduler> BuildScheduler()
        {
            var schedulerFactory = new StdSchedulerFactory(Properties);
            return schedulerFactory.GetScheduler();
        }

        /// <summary>
        /// Uses the default thread pool, which uses the default task scheduler.
        /// </summary>
        public void UseThreadPool<T>(Action<ThreadPoolOptions>? configure = null) where T : IThreadPool
        {
            SetProperty("quartz.threadPool.type", typeof(T).AssemblyQualifiedNameWithoutVersion());
            configure?.Invoke(new ThreadPoolOptions(this));
        } 

        /// <summary>
        /// Uses the zero size thread pool, which is used only for database administration nodes.
        /// </summary>
        public void UseZeroSizeThreadPool(Action<ThreadPoolOptions>? configure = null)
        {
            UseThreadPool<ZeroSizeThreadPool>(configure);
        }

        /// <summary>
        /// Uses the default thread pool, which uses the default task scheduler.
        /// </summary>
        public void UseDefaultThreadPool(Action<ThreadPoolOptions>? configure = null)
        {
            UseThreadPool<DefaultThreadPool>(configure);
        }

        /// <summary>
        /// Uses a dedicated thread pool, which uses own threads instead of task scheduler shared pool.
        /// </summary>
        public void UseDedicatedThreadPool(Action<ThreadPoolOptions>? configure = null)
        {
            UseThreadPool<DedicatedThreadPool>(configure);
        }

        public TimeSpan MisfireThreshold
        {
            set => SetProperty("quartz.jobStore.misfireThreshold", ((int) value.TotalMilliseconds).ToString());
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
            internal PersistentStoreOptions(PropertiesHolder parent) : base(parent)
            {
                SetProperty(StdSchedulerFactory.PropertyJobStoreType, typeof(JobStoreTX).AssemblyQualifiedNameWithoutVersion());
            }

            /// <summary>
            /// Set whether string-only properties will be handled in JobDataMaps.
            /// </summary>
            public bool UseProperties
            {
                set => SetProperty("quartz.jobStore.useProperties", value.ToString().ToLowerInvariant());
            }
            
            
            /// <summary>
            /// Gets or sets the database retry interval.
            /// </summary>
            /// <remarks>
            /// Defaults to 15 seconds.
            /// </remarks>
            public TimeSpan RetryInterval
            {
                set => SetProperty("quartz.jobStore.dbRetryInterval", ((int) value.TotalMilliseconds).ToString());
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
            options.SetProperty("quartz.jobStore.driverDelegateType", typeof(MySQLDelegate).AssemblyQualifiedNameWithoutVersion());
            options.SetProperty("quartz.jobStore.dataSource", SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName);
            options.SetProperty($"quartz.dataSource.{SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName}.provider", "MySql");

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

        public static void UseSQLite(
            this SchedulerBuilder.PersistentStoreOptions options,
            string connectionString)
        {
            options.UseSQLite(c => c.ConnectionString = connectionString);
        }
        
        public static void UseSQLite(
            this SchedulerBuilder.PersistentStoreOptions options,
            Action<SchedulerBuilder.AdoProviderOptions> configurer)
        {
            options.SetProperty("quartz.jobStore.driverDelegateType", typeof(SQLiteDelegate).AssemblyQualifiedNameWithoutVersion());
            options.SetProperty("quartz.jobStore.dataSource", SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName);
            options.SetProperty($"quartz.dataSource.{SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName}.provider", "SQLite");

            var adoProviderOptions = new SchedulerBuilder.AdoProviderOptions(options);
            configurer.Invoke(adoProviderOptions);
        }
    }
}