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

using System.Collections.Specialized;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// What a trimmed or native publish does with the flat <c>quartz.*</c> keys:
/// <c>Quartz.LegacyComponentKeys.IsSupported</c> off, a key that names a component by type or sets its
/// properties by string fails <c>AddQuartz</c> naming the code form, a value key binds as before, and
/// Quartz's own spellings of a store, a thread pool, a serializer and <c>SYS_PROP</c> still select what
/// they name.
/// </summary>
/// <remarks>
/// The switch is process-wide, so the fixture is non-parallelizable and puts it back after each test.
/// </remarks>
[NonParallelizable]
public sealed class LegacyComponentKeysSwitchTest
{
    private const string Switch = "Quartz.LegacyComponentKeys.IsSupported";

    private FeatureSwitchScope componentKeysOff = null!;

    [SetUp]
    public void TurnComponentKeysOff()
    {
        componentKeysOff = new FeatureSwitchScope(Switch, enabled: false);
    }

    [TearDown]
    public void TurnComponentKeysBackOn()
    {
        componentKeysOff.Dispose();
    }

    [TestCase("quartz.jobStore.type", "Acme.Scheduling.DocumentJobStore, Acme.Scheduling", "UseJobStore<T>()")]
    [TestCase("quartz.threadPool.type", "Acme.Threading.FiberPool, Acme.Threading", "UseThreadPool<T>()")]
    [TestCase("quartz.scheduler.typeLoadHelper.type", "Acme.Scheduling.PluginTypeLoader, Acme.Scheduling", "UseTypeLoader<T>()")]
    [TestCase("quartz.scheduler.jobFactory.type", "Acme.Scheduling.TenantJobFactory, Acme.Scheduling", "UseJobFactory<T>()")]
    [TestCase("quartz.timeProvider.type", "Acme.Time.FrozenClock, Acme.Time", "UseTimeProvider(")]
    [TestCase("quartz.scheduler.instanceIdGenerator.type", "Acme.Scheduling.RackIdGenerator, Acme.Scheduling", "UseInstanceIdGenerator<T>()")]
    [TestCase("quartz.jobStore.driverDelegateType", "Quartz.Impl.AdoJobStore.SqlServerDelegate, Quartz", "UseDriverDelegate<T>()")]
    [TestCase("quartz.jobStore.driverDelegateInitString", "triggerPersistenceDelegateTypes=Acme.Scheduling.RecurrencePersistenceDelegate, Acme.Scheduling", "UseTriggerPersistenceDelegate<T>()")]
    [TestCase("quartz.jobStore.lockHandler.type", "Quartz.Impl.AdoJobStore.UpdateRowLockHandler, Quartz", "UseLockHandler<T>()")]
    [TestCase("quartz.dataSource.quartz.connectionProvider.type", "Acme.Data.PooledProvider, Acme.Data", "UseConnectionProvider<T>()")]
    [TestCase("quartz.serializer.type", "newtonsoft", "UseSystemTextJsonSerializer()")]
    [TestCase("quartz.dbprovider.Acme.connectionType", "Acme.Data.AcmeConnection, Acme.Data", "UseGenericDatabase(")]
    public void AComponentKeyIsRefusedAtAddQuartzNamingTheCodeForm(string key, string value, string codeForm)
    {
        ServiceCollection services = new();

        var act = () => services.AddQuartz(new NameValueCollection { [key] = value });

        act.Should().Throw<SchedulerConfigException>()
            .WithMessage($"*'{key}'*")
            .WithMessage($"*{codeForm}*")
            .WithMessage($"*{Switch}*")
            .WithMessage("*<QuartzLegacyComponentKeysSupport>true</QuartzLegacyComponentKeysSupport>*",
                "a key the publish will not read fails where the configuration is handed over, saying which key, what to write in code, and how to turn the key back on");
    }

    [Test]
    public void ASerializerSettingBesideTheBuiltInSerializerIsRefused()
    {
        ServiceCollection services = new();

        var act = () => services.AddQuartz(new NameValueCollection
        {
            ["quartz.serializer.type"] = "stj",
            ["quartz.serializer.registerTriggerConverters"] = "true",
        });

        act.Should().Throw<SchedulerConfigException>()
            .WithMessage("*'quartz.serializer.registerTriggerConverters'*")
            .WithMessage("*UseSystemTextJsonSerializer()*",
                "stj on its own selects a typeof, and any further key under the prefix would be written onto the serializer by name");
    }

    [Test]
    public void AnInstanceIdGeneratorSettingBesideSysPropIsRefused()
    {
        ServiceCollection services = new();

        var act = () => services.AddQuartz(new NameValueCollection
        {
            ["quartz.scheduler.instanceId"] = "SYS_PROP",
            ["quartz.scheduler.instanceIdGenerator.systemPropertyName"] = "NODE_NAME",
        });

        act.Should().Throw<SchedulerConfigException>()
            .WithMessage("*'quartz.scheduler.instanceIdGenerator.systemPropertyName'*")
            .WithMessage("*UseInstanceIdGenerator<T>()*",
                "SYS_PROP selects Quartz's own generator, and its settings would be written onto it by name");
    }

    [Test]
    public async Task APluginKeyIsRefusedWhenTheSchedulerIsBuilt()
    {
        ServiceCollection services = new();
        services.AddQuartz(new NameValueCollection
        {
            ["quartz.plugin.history.type"] = "Quartz.Plugins.History.LoggingJobHistoryPlugin, Quartz.Plugins",
        });
        await using ServiceProvider container = services.BuildServiceProvider();

        Func<Task> act = async () => await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        await act.Should().ThrowAsync<SchedulerConfigException>()
            .WithMessage("*'quartz.plugin.history'*")
            .WithMessage("*AddPlugin<T>()*")
            .WithMessage($"*{Switch}*",
                "plugins are resolved when the scheduler is created, from a bag deferred configuration may have added to, so the refusal lives there too");
    }

    [Test]
    public void TheConfiguredMetadataFactoryRefusesWithoutTheBridge()
    {
        ConfigurationBasedDbMetadataFactory factory = new(
            new NameValueCollection { ["quartz.dbprovider.Acme.connectionType"] = "Acme.Data.AcmeConnection, Acme.Data" },
            "quartz.dbprovider");

        var act = () => factory.GetDbMetadata("Acme");

        act.Should().Throw<SchedulerConfigException>()
            .WithMessage("*'quartz.dbprovider.Acme'*")
            .WithMessage("*UseGenericDatabase(*",
                "the factory writes the keys onto a DbMetadata by name, so it guards itself for a caller that built it by other means");
    }

    [Test]
    public void ValueKeysStillBind()
    {
        ServiceCollection services = new();
        services.AddQuartz(new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = "values",
            ["quartz.threadPool.maxConcurrency"] = "3",
            ["quartz.jobStore.misfireThreshold"] = "45000",
            ["quartz.dataSource.reporting.provider"] = "SQLite-Microsoft",
            ["quartz.dataSource.reporting.connectionString"] = "Data Source=reporting.db",
        });
        using ServiceProvider container = services.BuildServiceProvider();

        container.GetRequiredService<IOptions<QuartzSchedulerOptions>>().Value.InstanceName.Should().Be("values");
        container.GetRequiredService<IOptions<ThreadPoolOptions>>().Value.MaxConcurrency.Should().Be(3);
        container.GetRequiredService<IOptions<InMemoryJobStoreOptions>>().Value.MisfireThreshold.Should().Be(TimeSpan.FromSeconds(45));
        container.GetRequiredService<IOptionsMonitor<DataSourceOptions>>().Get("reporting").ConnectionString.Should().Be("Data Source=reporting.db",
            "a value key reflects over nothing, and the switch is about reflection");
    }

    [TestCase("Quartz.Simpl.RAMJobStore, Quartz")]
    [TestCase("Quartz.Impl.RAMJobStore, Quartz")]
    [TestCase("Quartz.Impl.RAMJobStore")]
    [TestCase("Quartz.Impl.RAMJobStore, Quartz, Version=4.0.0.0, Culture=neutral, PublicKeyToken=f6b8c98a402cc8a4")]
    public void QuartzsOwnSpellingOfTheInMemoryStoreStillSelectsIt(string spelling)
    {
        ServiceCollection services = new();
        services.AddQuartz(new NameValueCollection
        {
            ["quartz.jobStore.type"] = spelling,
            ["quartz.jobStore.misfireThreshold"] = "45000",
        });
        using ServiceProvider container = services.BuildServiceProvider();

        container.GetRequiredService<IJobStore>().Should().BeOfType<RAMJobStore>(
            "a 3.x file names the store Quartz ships, and refusing that would make the flat format unusable trimmed for the default store");
        container.GetRequiredService<IJobStore>().As<RAMJobStore>().MisfireThreshold.Should().Be(TimeSpan.FromSeconds(45),
            "the store built from its typeof is configured the way the one built by name was");
    }

    /// <summary>
    /// The 3.x file carried over beside the code that chose the store: the key names what the code named
    /// and loses to it, which is the documented outcome, rather than failing the startup.
    /// </summary>
    [TestCase("Quartz.Impl.AdoJobStore.JobStoreTX, Quartz")]
    [TestCase("Quartz.Impl.AdoJobStore.LocalTransactionJobStore, Quartz")]
    [TestCase("Quartz.Impl.AdoJobStore.JobStoreCMT, Quartz")]
    [TestCase("Quartz.Impl.AdoJobStore.ExternalTransactionJobStore, Quartz")]
    public void APersistentStoreSpellingBesideTheStoreChosenInCodeIsLeftToTheCode(string spelling)
    {
        ServiceCollection services = new();
        services.AddQuartz(
            new NameValueCollection
            {
                ["quartz.scheduler.instanceName"] = "persistent-in-code",
                ["quartz.jobStore.type"] = spelling,
            },
            q => q.UsePersistentStore(store => store.UseSqlite(SqliteFactory.Instance, "Data Source=:memory:")));
        using ServiceProvider container = services.BuildServiceProvider();

        container.GetRequiredService<IJobStore>().Should().BeOfType<LocalTransactionJobStore>(
            "code wins when a store is named twice, and the key beside a UsePersistentStore call has always lost to it; "
            + "refusing it would fail a deployment that works today for a key that changes nothing");
    }

    /// <summary>
    /// A persistent store named by keys alone. It is refused rather than built from a <c>typeof</c>,
    /// because that <c>typeof</c> would root the whole ADO.NET store in every trimmed application, and a
    /// store configured this way could not run trimmed anyway: its driver is named by type.
    /// </summary>
    [TestCase("Quartz.Impl.AdoJobStore.JobStoreTX, Quartz")]
    [TestCase("Quartz.Impl.AdoJobStore.JobStoreCMT, Quartz")]
    public void APersistentStoreSpellingWithNoStoreInCodeIsRefusedNamingUsePersistentStore(string spelling)
    {
        ServiceCollection services = new();

        var act = () => services.AddQuartz(new NameValueCollection
        {
            ["quartz.jobStore.type"] = spelling,
            ["quartz.jobStore.dataSource"] = "default",
            ["quartz.dataSource.default.provider"] = "SQLite-Microsoft",
            ["quartz.dataSource.default.connectionString"] = "Data Source=:memory:",
        });

        act.Should().Throw<SchedulerConfigException>()
            .WithMessage("*'quartz.jobStore.type'*")
            .WithMessage("*UsePersistentStore(*")
            .WithMessage("*named by type*",
                "the data source's provider is a driver name that the type-name switch refuses, so the key alone never "
                + "builds a store this publish can run, and the message has to say where the store is configured instead");
    }

    [TestCase("Quartz.Simpl.SimpleThreadPool, Quartz")]
    [TestCase("Quartz.Impl.SimpleThreadPool, Quartz")]
    [TestCase("Quartz.Simpl.DefaultThreadPool, Quartz")]
    [TestCase("Quartz.Impl.DefaultThreadPool, Quartz")]
    public void QuartzsOwnSpellingOfTheThreadPoolStillSelectsIt(string spelling)
    {
        ServiceCollection services = new();
        services.AddQuartz(new NameValueCollection
        {
            ["quartz.threadPool.type"] = spelling,
            ["quartz.threadPool.maxConcurrency"] = "4",
        });
        using ServiceProvider container = services.BuildServiceProvider();

        IThreadPool threadPool = container.GetRequiredService<IThreadPool>();

        threadPool.Should().BeOfType<DefaultThreadPool>();
        threadPool.As<DefaultThreadPool>().MaxConcurrency.Should().Be(4, "the pool built from its typeof is configured the way the one built by name was");
    }

    [TestCase("stj")]
    [TestCase("json")]
    public void TheBuiltInSerializerSpellingsStillSelectIt(string spelling)
    {
        ServiceCollection services = new();
        services.AddQuartz(new NameValueCollection { ["quartz.serializer.type"] = spelling });
        using ServiceProvider container = services.BuildServiceProvider();

        container.GetRequiredService<IObjectSerializer>().Should().BeOfType<SystemTextJsonObjectSerializer>();
    }

    [Test]
    public void SysPropOnItsOwnStillSelectsTheSystemPropertyGenerator()
    {
        ServiceCollection services = new();
        services.AddQuartz(new NameValueCollection { ["quartz.scheduler.instanceId"] = "SYS_PROP" });
        using ServiceProvider container = services.BuildServiceProvider();

        container.GetRequiredService<IInstanceIdGenerator>().Should().BeOfType<SystemPropertyInstanceIdGenerator>(
            "SYS_PROP is a value that selects Quartz's own generator, not a type name");
    }
}
