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
        public SchedulerBuilder() : base(new NameValueCollection())
        {
        }

        public SchedulerBuilder WithId(string id)
        {
            SetProperty(StdSchedulerFactory.PropertySchedulerInstanceId, id);
            return this;
        }

        public SchedulerBuilder WithName(string name)
        {
            SetProperty(StdSchedulerFactory.PropertySchedulerInstanceName, name);
            return this;
        }

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

            SetProperty("quartz.jobStore.type", typeof(JobStoreTX).AssemblyQualifiedNameWithoutVersion());
            options?.Invoke(new PersistentStoreOptions(this));
            return this;
        }

        public Task<IScheduler> Build()
        {
            var schedulerFactory = new StdSchedulerFactory(Properties);
            return schedulerFactory.GetScheduler();
        }

        public SchedulerBuilder WithDefaultThreadPool(Action<ThreadPoolOptions> configurer)
        {
            SetProperty("quartz.threadPool.type", typeof(DefaultThreadPool).AssemblyQualifiedNameWithoutVersion());
            return this;
        }

        public class ThreadPoolOptions : PropertiesHolder
        {
            protected ThreadPoolOptions(PropertiesHolder parent) : base(parent)
            {
            }

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
            }

            public PersistentStoreOptions Clustered(Action<ClusterOptions> options = null)
            {
                SetProperty("quartz.jobStore.clustered", "true");
                options?.Invoke(new ClusterOptions(this));
                return this;
            }
        }

        public class ClusterOptions : PropertiesHolder
        {
            protected internal ClusterOptions(PropertiesHolder parent) : base(parent)
            {
            }

            public ClusterOptions WithCheckinInterval(TimeSpan interval)
            {
                SetProperty("quartz.jobStore.clusterCheckinInterval", ((int) interval.TotalMilliseconds).ToString());
                return this;
            }

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

            public AdoProviderOptions WithTablePrefix(string tablePrefix)
            {
                options.SetProperty("quartz.jobStore.tablePrefix", tablePrefix);
                return this;
            }

            public AdoProviderOptions WithBinarySerializer()
            {
                options.SetProperty("quartz.serializer.type", "binary");
                return this;
            }

            public AdoProviderOptions WithJsonSerializer()
            {
                options.SetProperty("quartz.serializer.type", "json");
                return this;
            }

            public AdoProviderOptions WithConnectionString(string connectionString)
            {
                options.SetProperty($"quartz.dataSource.{DefaultDataSourceName}.connectionString", connectionString);
                return this;
            }

            public AdoProviderOptions WithConnectionStringName(string connectionStringName)
            {
                options.SetProperty($"quartz.dataSource.{DefaultDataSourceName}.connectionStringName", connectionStringName);
                return this;
            }
        }
    }

    public static class SqlServerExtensions
    {
        public static SchedulerBuilder.AdoProviderOptions UseSqlServer(
            this SchedulerBuilder.PersistentStoreOptions options,
            Action<SchedulerBuilder.AdoProviderOptions> configurer)
        {
            options.SetProperty("quartz.jobStore.driverDelegateType", typeof(SqlServerDelegate).AssemblyQualifiedNameWithoutVersion());
            options.SetProperty("quartz.jobStore.lockHandler.type", typeof(SqlServerDelegate).AssemblyQualifiedNameWithoutVersion());

            options.SetProperty("quartz.jobStore.dataSource", SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName);
            options.SetProperty($"quartz.dataSource.{SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName}.provider", "SqlServer");

            var adoProviderOptions = new SchedulerBuilder.AdoProviderOptions(options);
            configurer.Invoke(adoProviderOptions);

            return adoProviderOptions;
        }
    }

    public static class PostgresExtensions
    {
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
    }
}