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

using FakeItEasy;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Core;
using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// What <see cref="QuartzSchedulerOptions.MaxBatchSize" /> means when nobody set it (#3862).
/// </summary>
/// <remarks>
/// Zero is automatic, and it is resolved where the scheduler's resources are built, because that is the
/// first place the store and the pool both exist. The options keep the zero, so what a reader of the
/// options sees is what was configured rather than what it turned into.
/// </remarks>
public sealed class AutomaticMaxBatchSizeTest
{
    /// <summary>A connection string nothing here opens: resolving the resources issues no statement.</summary>
    private const string SqliteConnectionString = "Data Source=:memory:";

    /// <summary>The same for PostgreSQL, which is the one store here that may cluster.</summary>
    private const string PostgresConnectionString = "Host=localhost;Database=quartznet;Username=quartznet;Password=quartznet";

    [Test]
    public void TheOptionDefaultsToAutomatic()
    {
        new QuartzSchedulerOptions().MaxBatchSize.Should().Be(0,
            "zero is what lets the scheduler choose, and the choice depends on a store the options cannot see");
    }

    [Test]
    public void APersistentStoreThatIsNotClusteredBatchesUpToThePool()
    {
        ServiceCollection services = new();
        services.AddQuartz(quartz =>
        {
            quartz.UseDefaultThreadPool(maxConcurrency: 12);
            quartz.UsePersistentStore(store => store.UseSqlite(SqliteFactory.Instance, SqliteConnectionString));
        });

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<QuartzSchedulerResources>().MaxBatchSize.Should().Be(12,
            "on a database a round is round trips and a commit, so one round for every trigger already due "
            + "is cheaper per firing than one round each, and the pool is as many as can run");

        provider.GetRequiredService<IOptions<QuartzSchedulerOptions>>().Value.MaxBatchSize.Should().Be(0,
            "the options say what was configured; the resolved value lives with the resources");
    }

    [Test]
    public void AClusteredStoreStaysAtOne()
    {
        ServiceCollection services = new();
        services.AddQuartz(quartz =>
        {
            quartz.UseDefaultThreadPool(maxConcurrency: 12);
            quartz.UsePersistentStore(store =>
            {
                store.UsePostgres(PostgresConnectionString);
                store.UseClustering();
            });
        });

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<QuartzSchedulerResources>().MaxBatchSize.Should().Be(1,
            "a batched round takes the cluster-wide TRIGGER_ACCESS lock even when it acquires nothing, and "
            + "the clustered drain gate has not said that is worth it");
    }

    [Test]
    public void TheInMemoryStoreStaysAtOne()
    {
        ServiceCollection services = new();
        services.AddQuartz(quartz =>
        {
            quartz.UseDefaultThreadPool(maxConcurrency: 12);
            quartz.UseInMemoryStore();
        });

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<QuartzSchedulerResources>().MaxBatchSize.Should().Be(1,
            "an in-memory round is a monitor rather than a round trip, and a batch there measured slower "
            + "for a burst of one-offs");
    }

    [Test]
    public void ANamedSchedulerResolvesAgainstItsOwnStore()
    {
        ServiceCollection services = new();
        services.AddQuartz(quartz => quartz.UseInMemoryStore());
        services.AddQuartz("reporting", quartz =>
        {
            quartz.UseDefaultThreadPool(maxConcurrency: 6);
            quartz.UsePersistentStore(store => store.UseSqlite(SqliteFactory.Instance, SqliteConnectionString));
        });

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredKeyedService<QuartzSchedulerResources>("reporting").MaxBatchSize.Should().Be(6,
            "each scheduler's automatic value is its own store's and its own pool's");
        provider.GetRequiredService<QuartzSchedulerResources>().MaxBatchSize.Should().Be(1);
    }

    [TestCase(1)]
    [TestCase(4)]
    public void AnExplicitValueWinsOnAPersistentStore(int configured)
    {
        ServiceCollection services = new();
        services.AddQuartz(quartz =>
        {
            quartz.UseDefaultThreadPool(maxConcurrency: 12);
            quartz.ConfigureScheduler(options => options.MaxBatchSize = configured);
            quartz.UsePersistentStore(store => store.UseSqlite(SqliteFactory.Instance, SqliteConnectionString));
        });

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<QuartzSchedulerResources>().MaxBatchSize.Should().Be(configured,
            "one is how a deployment keeps 4.2's behaviour, and it has to mean one");
    }

    [Test]
    public void AnExplicitValueWinsInMemory()
    {
        ServiceCollection services = new();
        services.AddQuartz(quartz =>
        {
            quartz.UseDefaultThreadPool(maxConcurrency: 12);
            quartz.ConfigureScheduler(options => options.MaxBatchSize = 5);
        });

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<QuartzSchedulerResources>().MaxBatchSize.Should().Be(5);
    }

    [Test]
    public void TheLegacyKeyStillSetsTheValue()
    {
        ServiceCollection services = new();
        services.AddQuartz(new System.Collections.Specialized.NameValueCollection
        {
            ["quartz.scheduler.batchTriggerAcquisitionMaxCount"] = "3",
            ["quartz.threadPool.maxConcurrency"] = "8",
        });

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<QuartzSchedulerResources>().MaxBatchSize.Should().Be(3,
            "the key maps onto the option unchanged, and an explicit value wins over the automatic one");
    }

    /// <summary>
    /// A store of somebody's own is judged by what it says it is, which is the only thing the scheduler
    /// can know about it.
    /// </summary>
    [TestCase(true, false, 7, 7)]
    [TestCase(true, true, 7, 1)]
    [TestCase(false, false, 7, 1)]
    public void AStoreIsJudgedByWhatItSaysItIs(bool persistent, bool clustered, int poolSize, int expected)
    {
        IJobStore store = A.Fake<IJobStore>();
        A.CallTo(() => store.SupportsPersistence).Returns(persistent);
        A.CallTo(() => store.Clustered).Returns(clustered);

        IThreadPool pool = A.Fake<IThreadPool>();
        A.CallTo(() => pool.PoolSize).Returns(poolSize);

        QuartzSchedulerResources.AutomaticMaxBatchSize(store, pool).Should().Be(expected);
    }

    [Test]
    public void APoolWithNoThreadsStillAcquiresOne()
    {
        IJobStore store = A.Fake<IJobStore>();
        A.CallTo(() => store.SupportsPersistence).Returns(true);
        A.CallTo(() => store.Clustered).Returns(false);

        QuartzSchedulerResources.AutomaticMaxBatchSize(store, new ZeroSizeThreadPool()).Should().Be(1,
            "a batch of zero is not a batch, and QuartzSchedulerResources refuses one");
    }

    [Test]
    public void ZeroIsAValidSetting()
    {
        ServiceCollection services = new();
        services.AddQuartz(quartz => quartz.ConfigureScheduler(options => options.MaxBatchSize = 0));

        using ServiceProvider provider = services.BuildServiceProvider();

        Action act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().NotThrow("zero is the automatic value, and it is also the default");
    }

    [Test]
    public void ANegativeValueFailsAtStartupAndSaysWhatZeroMeans()
    {
        ServiceCollection services = new();
        services.AddQuartz(quartz => quartz.ConfigureScheduler(options => options.MaxBatchSize = -1));

        using ServiceProvider provider = services.BuildServiceProvider();

        Action act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>().WithMessage("*MaxBatchSize*negative*Zero lets the scheduler choose*");
    }

    [Test]
    public void TheAutomaticValueIsNotHeldToThePoolBecauseItNeverExceedsIt()
    {
        ServiceCollection services = new();
        services.AddQuartz(quartz =>
        {
            quartz.UseDefaultThreadPool(maxConcurrency: 2);
            quartz.UsePersistentStore(store => store.UseSqlite(SqliteFactory.Instance, SqliteConnectionString));
        });

        using ServiceProvider provider = services.BuildServiceProvider();

        Action act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().NotThrow("the pool is the largest the automatic value can be");
        provider.GetRequiredService<QuartzSchedulerResources>().MaxBatchSize.Should().Be(2);
    }
}
