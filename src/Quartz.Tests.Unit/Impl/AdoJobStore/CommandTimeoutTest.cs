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
using System.Data.Common;
using System.Threading.Tasks;

using FakeItEasy;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// <c>quartz.jobStore.commandTimeout</c> bounds every statement the store issues, the lock statement
/// included.
/// </summary>
/// <remarks>
/// <para>
/// A row lock belongs to the database session that took it, so Quartz cannot release one whose client
/// is gone — a node that loses its network without the server noticing keeps the <c>TRIGGER_ACCESS</c>
/// row locked (#3763). Every other node's next lock statement then queues behind that dead session,
/// and with no timeout it waits there forever: the statement never fails, so none of the retry, the
/// back-off or the <c>SchedulerError</c> paths run and nothing is logged at all.
/// </para>
/// <para>
/// The timeout is what turns that wait into a failed statement the store retries and recovers from
/// once the server drops the dead session. 4.x has it as <c>JobStore:CommandTimeout</c>; this is the
/// same setting spelled the way this branch spells settings.
/// </para>
/// </remarks>
public class CommandTimeoutTest
{
    /// <summary>
    /// What a provider would have left on a command of its own, so that "unset" can be told apart from
    /// "set to zero" — which every provider reads as "wait forever".
    /// </summary>
    private const int ProviderDefault = 45;

    [Test]
    public void APreparedCommandCarriesTheConfiguredTimeout()
    {
        StubCommand command = A.Fake<StubCommand>();
        AdoUtil adoUtil = new AdoUtil(ProviderReturning(command))
        {
            CommandTimeout = TimeSpan.FromSeconds(20)
        };

        adoUtil.PrepareCommand(Connection(), "SELECT 1");

        command.CommandTimeout.Should().Be(20,
            "the configured timeout is what every statement the store issues runs under");
    }

    /// <summary>
    /// ADO.NET counts whole seconds, and rounding down would turn a sub-second timeout into zero —
    /// which is not a very short wait but an unbounded one, the exact failure the setting exists to
    /// prevent.
    /// </summary>
    [TestCase(1500, 2)]
    [TestCase(100, 1)]
    [TestCase(20000, 20)]
    public void ASubSecondTimeoutIsRoundedUpRatherThanDownToZero(int configuredMilliseconds, int expectedSeconds)
    {
        StubCommand command = A.Fake<StubCommand>();
        AdoUtil adoUtil = new AdoUtil(ProviderReturning(command))
        {
            CommandTimeout = TimeSpan.FromMilliseconds(configuredMilliseconds)
        };

        adoUtil.PrepareCommand(Connection(), "SELECT 1");

        command.CommandTimeout.Should().Be(expectedSeconds,
            "a timeout rounded down to zero would be read as 'no timeout at all' by every provider");
    }

    [Test]
    public void AnUnsetTimeoutLeavesTheCommandAsTheProviderMintedIt()
    {
        StubCommand command = A.Fake<StubCommand>();
        command.CommandTimeout = ProviderDefault;
        AdoUtil adoUtil = new AdoUtil(ProviderReturning(command));

        adoUtil.PrepareCommand(Connection(), "SELECT 1");

        command.CommandTimeout.Should().Be(ProviderDefault,
            "an unset timeout is the provider's own default, not zero");
    }

    /// <summary>
    /// The driver delegate issues everything the store does apart from the lock statement, and gets the
    /// timeout through its initialization arguments.
    /// </summary>
    [Test]
    public void TheDelegatePreparesItsCommandsWithTheTimeoutItWasInitializedWith()
    {
        StubCommand command = A.Fake<StubCommand>();
        StdAdoDelegate adoDelegate = new StdAdoDelegate();
        adoDelegate.Initialize(new DelegateInitializationArgs
        {
            TablePrefix = "QRTZ_",
            InstanceId = "INSTANCE",
            InstanceName = "TESTSCHED",
            TypeLoadHelper = new SimpleTypeLoadHelper(),
            UseProperties = false,
            InitString = "",
            DbProvider = ProviderReturning(command),
            CommandTimeout = TimeSpan.FromSeconds(20)
        });

        adoDelegate.PrepareCommand(Connection(), "SELECT 1");

        command.CommandTimeout.Should().Be(20,
            "the store's timeout has to reach the AdoUtil the delegate prepares its commands with");
    }

    /// <summary>
    /// The flat key is read the way every other duration on the store is: as a millisecond count. This
    /// is <c>StdSchedulerFactory</c>'s own binding, and it is why the property is a non-nullable
    /// <see cref="TimeSpan" /> — a <c>TimeSpan?</c> would fall to the nullable converter and read
    /// <c>20000</c> as twenty thousand days.
    /// </summary>
    [Test]
    public void TheFlatPropertyIsReadAsMilliseconds()
    {
        NameValueCollection properties = new NameValueCollection
        {
            ["quartz.jobStore.commandTimeout"] = "20000"
        };
        PropertiesParser parser = new PropertiesParser(properties);
        JobStoreSupportTest.TestJobStoreSupport store = new JobStoreSupportTest.TestJobStoreSupport();

        ObjectUtils.SetObjectProperties(store, parser.GetPropertyGroup(StdSchedulerFactory.PropertyJobStorePrefix, true));

        store.CommandTimeout.Should().Be(TimeSpan.FromSeconds(20),
            "quartz.jobStore.commandTimeout is a millisecond count, as quartz.jobStore.dbRetryInterval is");
    }

    [Test]
    public void TheTimeoutIsUnsetUntilItIsConfigured()
    {
        JobStoreSupportTest.TestJobStoreSupport store = new JobStoreSupportTest.TestJobStoreSupport();

        store.CommandTimeout.Should().Be(TimeSpan.Zero,
            "an unconfigured store leaves every statement with whatever default its provider gives a "
            + "new command");
    }

    [Test]
    public void ANegativeTimeoutIsRefusedWhereItIsConfigured()
    {
        JobStoreSupportTest.TestJobStoreSupport store = new JobStoreSupportTest.TestJobStoreSupport();

        Action act = () => store.CommandTimeout = TimeSpan.FromSeconds(-1);

        act.Should().Throw<ArgumentOutOfRangeException>(
                "a negative timeout is refused by the command it would be applied to, with nothing to "
                + "say which setting it came from")
            .WithMessage("*CommandTimeout*");
    }

    /// <summary>
    /// ADO.NET's timeout is a whole number of seconds in an <see cref="int" />, so a longer wait than
    /// that cannot be applied at all: unchecked, the rounded value overflows into a negative one and
    /// every statement the store issues fails.
    /// </summary>
    [Test]
    public void ATimeoutNoCommandCanExpressIsRefusedWhereItIsConfigured()
    {
        JobStoreSupportTest.TestJobStoreSupport store = new JobStoreSupportTest.TestJobStoreSupport();

        Action act = () => store.CommandTimeout = TimeSpan.FromDays(100000);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*CommandTimeout*")
            .WithMessage("*" + int.MaxValue + " seconds*");
    }

    /// <summary>
    /// The lock handler the store picks for itself is the one that takes <c>TRIGGER_ACCESS</c>, and its
    /// statement is the one a dead session blocks.
    /// </summary>
    [Test]
    public async Task TheStoreBoundsTheLockStatementOfTheHandlerItBuilds()
    {
        JobStoreSupportTest.TestJobStoreSupport store = StoreUsingDatabaseLocks();
        store.CommandTimeout = TimeSpan.FromSeconds(20);

        await store.Initialize(A.Fake<ITypeLoadHelper>(), A.Fake<ISchedulerSignaler>());

        store.LockHandler.Should().BeOfType<StdRowLockSemaphore>()
            .Which.CommandTimeout.Should().Be(TimeSpan.FromSeconds(20),
                "the lock statement is where an unbounded wait costs the whole cluster its scheduling loop");
    }

    /// <summary>
    /// A handler named by <c>quartz.jobStore.lockHandler.type</c> takes the same lock with the same
    /// kind of statement, so the store's timeout reaches it too — which is what 4.x does by handing
    /// every handler a <c>LockHandlerContext</c>.
    /// </summary>
    [Test]
    public async Task TheStoreBoundsTheLockStatementOfAHandlerItWasGiven()
    {
        JobStoreSupportTest.TestJobStoreSupport store = StoreUsingDatabaseLocks();
        store.CommandTimeout = TimeSpan.FromSeconds(20);
        store.LockHandler = new UpdateLockRowSemaphore(A.Fake<IDbProvider>());

        await store.Initialize(A.Fake<ITypeLoadHelper>(), A.Fake<ISchedulerSignaler>());

        store.LockHandler.Should().BeOfType<UpdateLockRowSemaphore>()
            .Which.CommandTimeout.Should().Be(TimeSpan.FromSeconds(20),
                "a custom handler locks the same row and can wait on the same dead session");
    }

    [Test]
    public async Task AnUnsetTimeoutLeavesAHandlerThatWasGivenOneOfItsOwnAlone()
    {
        JobStoreSupportTest.TestJobStoreSupport store = StoreUsingDatabaseLocks();
        store.LockHandler = new UpdateLockRowSemaphore(A.Fake<IDbProvider>())
        {
            CommandTimeout = TimeSpan.FromSeconds(5)
        };

        await store.Initialize(A.Fake<ITypeLoadHelper>(), A.Fake<ISchedulerSignaler>());

        store.LockHandler.Should().BeOfType<UpdateLockRowSemaphore>()
            .Which.CommandTimeout.Should().Be(TimeSpan.FromSeconds(5),
                "an unconfigured store has no timeout to impose, so a handler configured with one of "
                + "its own keeps it");
    }

    [Test]
    public void TheBuilderWritesTheTimeoutAsMilliseconds()
    {
        SchedulerBuilder builder = SchedulerBuilder.Create();
        builder.UsePersistentStore(store =>
        {
            store.CommandTimeout = TimeSpan.FromSeconds(20);
        });

        builder.Properties["quartz.jobStore.commandTimeout"].Should().Be("20000",
            "the fluent builder writes the same flat key the properties file does");
    }

    /// <summary>
    /// A store that locks in the database, with everything it reaches for during initialization faked:
    /// no connection is opened, and the fake driver delegate keeps it out of the SQLite and SQL Server
    /// special cases.
    /// </summary>
    private static JobStoreSupportTest.TestJobStoreSupport StoreUsingDatabaseLocks()
    {
        IDbConnectionManager connectionManager = A.Fake<IDbConnectionManager>();
        A.CallTo(() => connectionManager.GetDbProvider(A<string>._)).Returns(A.Fake<IDbProvider>());

        return new JobStoreSupportTest.TestJobStoreSupport
        {
            DataSource = "someDataSource",
            ConnectionManager = connectionManager,
            UseDBLocks = true,
            DirectDelegate = A.Fake<IDriverDelegate>()
        };
    }

    private static IDbProvider ProviderReturning(DbCommand command)
    {
        IDbProvider provider = A.Fake<IDbProvider>();
        A.CallTo(() => provider.CreateCommand()).Returns(command);
        return provider;
    }

    private static ConnectionAndTransactionHolder Connection()
    {
        return new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), A.Fake<DbTransaction>());
    }
}
