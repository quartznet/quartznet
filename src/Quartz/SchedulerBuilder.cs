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
    public abstract class PropertiesHolder
    {
        private readonly NameValueCollection properties;

        protected PropertiesHolder(NameValueCollection properties)
        {
            this.properties = properties;
        }

        protected PropertiesHolder(PropertiesHolder parent)
        {
            properties = parent.properties;
        }

        public void SetProperty(string name, string value)
        {
            properties[name] = value;
        }

        internal NameValueCollection Properties => properties;
    }

    /// <summary>
    /// Helper to create common scheduler configurations.
    /// </summary>
    public class SchedulerBuilder : PropertiesHolder
    {
        protected SchedulerBuilder(NameValueCollection properties)
            : base(properties ?? new NameValueCollection())
        {
        }

        /// <summary>
        /// UNSTABLE API. Creates a new scheduler configuration to build desired setup.
        /// </summary>
        /// <param name="properties">Base properties, if any.</param>
        /// <returns>New scheduler builder instance that can be used to build configuration.</returns>
        public static SchedulerBuilder Create(NameValueCollection properties = null)
        {
            return new SchedulerBuilder(properties);
        }

        /// <summary>
        /// UNSTABLE API. Creates a new scheduler configuration to build desired setup.
        /// </summary>
        public static SchedulerBuilder Create(string id, string name)
        {
            return Create().WithId(id).WithName(name);
        }

        /// <summary>
        /// Sets the instance id of the scheduler (must be unique within a cluster).
        /// </summary>
        public SchedulerBuilder WithId(string id)
        {
            SetProperty(StdSchedulerFactory.PropertySchedulerInstanceId, id);
            return this;
        }

        /// <summary>
        /// Sets the instance id of the scheduler (must be unique within this server instance).
        /// </summary>
        public SchedulerBuilder WithName(string name)
        {
            SetProperty(StdSchedulerFactory.PropertySchedulerInstanceName, name);
            return this;
        }

        /// <summary>
        /// Use memory store, which does not survive process restarts/crashes.
        /// </summary>
        public SchedulerBuilder UseMemoryStore(Action<MemoryStoreOptions> options = null)
        {
            SetProperty("quartz.jobStore.type", typeof(RAMJobStore).AssemblyQualifiedNameWithoutVersion());
            options?.Invoke(new MemoryStoreOptions(this));
            return this;
        }

        public SchedulerBuilder UsePersistentStore(Action<PersistentStoreOptions> options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            options?.Invoke(new PersistentStoreOptions(this));
            return this;
        }

        /// <summary>
        /// Finalizes the configuration and builds the actual scheduler.
        /// </summary>
        public Task<IScheduler> Build()
        {
            var schedulerFactory = new StdSchedulerFactory(Properties);
            return schedulerFactory.GetScheduler();
        }

        /// <summary>
        /// Uses the default thread pool, which uses the default task scheduler.
        /// </summary>
        public SchedulerBuilder WithDefaultThreadPool(Action<ThreadPoolOptions> configurer = null)
        {
            SetProperty("quartz.threadPool.type", typeof(DefaultThreadPool).AssemblyQualifiedNameWithoutVersion());
            configurer?.Invoke(new ThreadPoolOptions(this));
            return this;
        }

        /// <summary>
        /// Uses a dedicated thread pool, which uses own threads instead of task scheduler shared pool.
        /// </summary>
        public SchedulerBuilder WithDedicatedThreadPool(Action<ThreadPoolOptions> configurer = null)
        {
            SetProperty("quartz.threadPool.type", typeof(DedicatedThreadPool).AssemblyQualifiedNameWithoutVersion());
            configurer?.Invoke(new ThreadPoolOptions(this));
            return this;
        }

        public SchedulerBuilder WithMisfireThreshold(TimeSpan threshold)
        {
            SetProperty("quartz.jobStore.misfireThreshold", ((int) threshold.TotalMilliseconds).ToString());
            return this;
        }

        public class ThreadPoolOptions : PropertiesHolder
        {
            protected internal ThreadPoolOptions(PropertiesHolder parent) : base(parent)
            {
            }

            /// <summary>
            /// The maximum number of thread pool tasks which can be executing in parallel
            /// </summary>
            public ThreadPoolOptions WithThreadCount(int count)
            {
                SetProperty("quartz.threadPool.threadCount", count.ToString());
                return this;
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
                SetProperty("quartz.jobStore.type", typeof(JobStoreTX).AssemblyQualifiedNameWithoutVersion());
            }

            /// <summary>
            /// Set whether string-only properties will be handled in JobDataMaps.
            /// </summary>
            public PersistentStoreOptions UseProperties(bool use = true)
            {
                SetProperty("quartz.jobStore.useProperties", use.ToString().ToLowerInvariant());
                return this;
            }

            /// <summary>
            /// Make this instance is part of a cluster.
            /// </summary>
            public PersistentStoreOptions Clustered(Action<ClusterOptions> options = null)
            {
                return Clustered(true, options);
            }

            /// <summary>
            /// Make this instance is part of a cluster or not.
            /// </summary>
            public PersistentStoreOptions Clustered(bool clustered, Action<ClusterOptions> options = null)
            {
                SetProperty("quartz.jobStore.clustered", clustered.ToString().ToLowerInvariant());
                options?.Invoke(new ClusterOptions(this));
                return this;
            }

            /// <summary>
            /// Configures persistence to use generic <see cref="StdAdoDelegate" />.
            /// </summary>
            /// <param name="provider">Valid provider name to configure driver details.</param>
            /// <param name="configurer">Callback to refine configuration.</param>
            /// <returns></returns>
            public PersistentStoreOptions UseGenericDatabase(
                string provider,
                Action<AdoProviderOptions> configurer = null)
            {
                SetProperty("quartz.jobStore.driverDelegateType", typeof(StdAdoDelegate).AssemblyQualifiedNameWithoutVersion());
                SetProperty("quartz.jobStore.dataSource", SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName);
                SetProperty($"quartz.dataSource.{AdoProviderOptions.DefaultDataSourceName}.provider", provider);

                configurer?.Invoke(new AdoProviderOptions(this));

                return this;
            }

            /// <summary>
            /// Configure binary serialization, consider using JSON instead which requires extra package Quartz.Serialization.Json.
            /// </summary>
            public PersistentStoreOptions WithBinarySerializer()
            {
                return WithSerializer<BinaryObjectSerializer>();
            }

            /// <summary>
            /// Use custom serializer.
            /// </summary>
            public PersistentStoreOptions WithSerializer<T>() where T : IObjectSerializer
            {
                SetProperty("quartz.serializer.type", typeof(T).AssemblyQualifiedNameWithoutVersion());
                return this;
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
            public ClusterOptions WithCheckinInterval(TimeSpan interval)
            {
                SetProperty("quartz.jobStore.clusterCheckinInterval", ((int) interval.TotalMilliseconds).ToString());
                return this;
            }

            /// <summary>
            /// The time span by which a check-in must have missed its
            /// next-fire-time, in order for it to be considered "misfired" and thus
            /// other scheduler instances in a cluster can consider a "misfired" scheduler
            /// instance as failed or dead.
            /// </summary>
            public ClusterOptions WithCheckinMisfireThreshold(TimeSpan interval)
            {
                SetProperty("quartz.jobStore.clusterCheckinMisfireThreshold", ((int) interval.TotalMilliseconds).ToString());
                return this;
            }
        }

        public class MemoryStoreOptions : PropertiesHolder
        {
            protected internal MemoryStoreOptions(SchedulerBuilder parent) : base(parent)
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
            public AdoProviderOptions WithTablePrefix(string tablePrefix)
            {
                options.SetProperty("quartz.jobStore.tablePrefix", tablePrefix);
                return this;
            }

            /// <summary>
            /// Standard connection driver specific connection string.
            /// </summary>
            public AdoProviderOptions WithConnectionString(string connectionString)
            {
                options.SetProperty($"quartz.dataSource.{DefaultDataSourceName}.connectionString", connectionString);
                return this;
            }

            /// <summary>
            /// Use named connection defined in application configuration file.
            /// </summary>
            public AdoProviderOptions WithConnectionStringName(string connectionStringName)
            {
                options.SetProperty($"quartz.dataSource.{DefaultDataSourceName}.connectionStringName", connectionStringName);
                return this;
            }

            /// <summary>
            /// Use named connection defined in application configuration file.
            /// </summary>
            public AdoProviderOptions WithDriverDelegate<T>() where T : IDriverDelegate
            {
                options.SetProperty("quartz.jobStore.driverDelegateType", typeof(T).AssemblyQualifiedNameWithoutVersion());
                return this;
            }
        }
    }

    public static class AdoProviderExtensions
    {
        public static SchedulerBuilder.AdoProviderOptions UseSqlServer(
            this SchedulerBuilder.PersistentStoreOptions options,
            Action<SchedulerBuilder.AdoProviderOptions> configurer)
        {
            options.SetProperty("quartz.jobStore.driverDelegateType", typeof(SqlServerDelegate).AssemblyQualifiedNameWithoutVersion());
            options.SetProperty("quartz.jobStore.dataSource", SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName);
            options.SetProperty($"quartz.dataSource.{SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName}.provider", "SqlServer");

            var adoProviderOptions = new SchedulerBuilder.AdoProviderOptions(options);
            configurer.Invoke(adoProviderOptions);

            return adoProviderOptions;
        }

        public static SchedulerBuilder.AdoProviderOptions UsePostgres(
            this SchedulerBuilder.PersistentStoreOptions options,
            Action<SchedulerBuilder.AdoProviderOptions> configurer)
        {
            options.SetProperty("quartz.jobStore.driverDelegateType", typeof(PostgreSQLDelegate).AssemblyQualifiedNameWithoutVersion());
            options.SetProperty("quartz.jobStore.dataSource", SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName);
            options.SetProperty($"quartz.dataSource.{SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName}.provider", "Npgsql");

            var adoProviderOptions = new SchedulerBuilder.AdoProviderOptions(options);
            configurer.Invoke(adoProviderOptions);

            return adoProviderOptions;
        }

        public static SchedulerBuilder.AdoProviderOptions UseMySql(
            this SchedulerBuilder.PersistentStoreOptions options,
            Action<SchedulerBuilder.AdoProviderOptions> configurer)
        {
            options.SetProperty("quartz.jobStore.driverDelegateType", typeof(MySQLDelegate).AssemblyQualifiedNameWithoutVersion());
            options.SetProperty("quartz.jobStore.dataSource", SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName);
            options.SetProperty($"quartz.dataSource.{SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName}.provider", "MySql");

            var adoProviderOptions = new SchedulerBuilder.AdoProviderOptions(options);
            configurer.Invoke(adoProviderOptions);

            return adoProviderOptions;
        }

        public static SchedulerBuilder.AdoProviderOptions UseFirebird(
            this SchedulerBuilder.PersistentStoreOptions options,
            Action<SchedulerBuilder.AdoProviderOptions> configurer)
        {
            options.SetProperty("quartz.jobStore.driverDelegateType", typeof(FirebirdDelegate).AssemblyQualifiedNameWithoutVersion());
            options.SetProperty("quartz.jobStore.dataSource", SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName);
            options.SetProperty($"quartz.dataSource.{SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName}.provider", "Firebird");

            var adoProviderOptions = new SchedulerBuilder.AdoProviderOptions(options);
            configurer.Invoke(adoProviderOptions);

            return adoProviderOptions;
        }

        public static SchedulerBuilder.AdoProviderOptions UseOracle(
            this SchedulerBuilder.PersistentStoreOptions options,
            Action<SchedulerBuilder.AdoProviderOptions> configurer)
        {
            options.SetProperty("quartz.jobStore.driverDelegateType", typeof(OracleDelegate).AssemblyQualifiedNameWithoutVersion());
            options.SetProperty("quartz.jobStore.dataSource", SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName);
            options.SetProperty($"quartz.dataSource.{SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName}.provider", "OracleODPManaged");

            var adoProviderOptions = new SchedulerBuilder.AdoProviderOptions(options);
            configurer.Invoke(adoProviderOptions);

            return adoProviderOptions;
        }

        public static SchedulerBuilder.AdoProviderOptions UseSQLite(
            this SchedulerBuilder.PersistentStoreOptions options,
            Action<SchedulerBuilder.AdoProviderOptions> configurer)
        {
            options.SetProperty("quartz.jobStore.driverDelegateType", typeof(SQLiteDelegate).AssemblyQualifiedNameWithoutVersion());
            options.SetProperty("quartz.jobStore.dataSource", SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName);
            options.SetProperty($"quartz.dataSource.{SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName}.provider", "SQLite");

            var adoProviderOptions = new SchedulerBuilder.AdoProviderOptions(options);
            configurer.Invoke(adoProviderOptions);

            return adoProviderOptions;
        }
    }
}