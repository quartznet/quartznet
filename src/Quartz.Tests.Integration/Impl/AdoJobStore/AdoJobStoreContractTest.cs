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

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The job store contract as the ADO.NET store implements it, against one real database. A subclass
/// per dialect says which driver, which delegate and which database, and the contract itself is the
/// same assertions the in-memory store answers.
/// </summary>
/// <remarks>
/// <para>
/// The dialects are where an ADO store's answers can diverge from each other: paging syntax, LIKE
/// escaping, how a boolean and a timestamp are spelled, and which statements a provider binds
/// positionally. Running the contract against one dialect proves the store's logic; running it
/// against all of them is what proves the SQL.
/// </para>
/// <para>
/// Three pieces of fixture discipline matter and are shared here, because getting any of them wrong
/// makes a green run meaningless:
/// </para>
/// <list type="bullet">
/// <item>
/// Every row is keyed by scheduler name, so each fixture uses one of its own. Two dialect fixtures
/// that shared a name would be invisible to each other only until the same database served both.
/// </item>
/// <item>
/// The store is cleared as it is built, which is once per test. A container's database outlives the
/// test that wrote to it, so anything the previous test stored would still be there to be counted.
/// </item>
/// <item>
/// <see cref="IJobStore.SchedulerStarted" /> is never called. It spawns the misfire handler, which
/// moves trigger state between a test's arrange and its act — #3303 is what that looks like when it
/// happens — and leaves a foreground thread behind for every store the fixture builds.
/// </item>
/// </list>
/// </remarks>
public abstract class AdoJobStoreContractTest : JobStoreContractTest
{
    private const string DataSourceName = "job-store-contract";
    private const string InstanceId = "contract-instance";

    protected override string StoreInstanceId => InstanceId;

    /// <summary>
    /// The scheduler name every row this fixture writes is keyed by. Derived from the fixture's own
    /// type name, so no two dialect fixtures can see each other's rows even in one database.
    /// </summary>
    protected string SchedulerName => "JobStoreContract_" + GetType().Name;

    /// <summary>
    /// The Quartz provider name of the ADO.NET driver, as <c>quartz.dataSource.default.provider</c>
    /// spells it.
    /// </summary>
    protected abstract string DbProviderName { get; }

    /// <summary>
    /// Makes the database ready and answers the connection string to reach it with. The container
    /// fixtures only read the connection string their container published; the file-database ones
    /// create the file here.
    /// </summary>
    protected abstract ValueTask<string> PrepareDatabase();

    /// <summary>
    /// The delegate that speaks this database's SQL dialect — the same one
    /// <c>UsePostgres</c>, <c>UseSqlServer</c> and their siblings select.
    /// </summary>
    protected abstract IDriverDelegate CreateDriverDelegate();

    /// <summary>
    /// The connection string <see cref="TestcontainersDatabaseEnvironment" /> published for this
    /// database when it started the container, read from the environment variable it publishes it in.
    /// </summary>
    /// <remarks>
    /// Read rather than defaulted, and deliberately so. The older ADO fixtures each carry a
    /// hard-coded localhost fallback, from when these databases were started by hand outside the test
    /// run; a container is now the only supported way to get a <c>db-*</c> leg going, so a fallback
    /// here would buy nothing and cost the one thing worth having — when the container fails to
    /// start, this says so, instead of timing out against whatever else happens to be on localhost.
    /// Not carrying credentials of its own is the other half of the point.
    /// </remarks>
    protected static string ContainerConnectionString(string variableName)
    {
        string connectionString = Environment.GetEnvironmentVariable(variableName);

        connectionString.Should().NotBeNullOrWhiteSpace(
            "{0} is set by the container this assembly starts, so an empty one means the container "
            + "for this leg never started — run the fixture through its own QUARTZ_TEST_DATABASE leg",
            variableName);

        return connectionString;
    }

    /// <summary>
    /// Builds the store under test. Overridden by the fixture that runs the contract against
    /// <see cref="ExternalTransactionJobStore" />.
    /// </summary>
    private protected virtual AdoJobStoreBase CreateJobStore(IDbProvider dbProvider, IDriverDelegate driverDelegate)
    {
        return new LocalTransactionJobStore(StoreDependencies(dbProvider, driverDelegate));
    }

    /// <summary>
    /// What the store under test is built from. Deliberately no lock handler: the store picks the one
    /// its delegate and clustering settings call for, which is the decision production makes too — the
    /// configuration builder injects one only when the application asked for a specific one. SQLite
    /// therefore gets SqliteLockHandler here exactly as it would in an application.
    /// </summary>
    private protected AdoJobStoreDependencies StoreDependencies(IDbProvider dbProvider, IDriverDelegate driverDelegate)
    {
        return TestJobStores.Dependencies(
            schedulerOptions: TestJobStores.SchedulerOptions(SchedulerName, InstanceId),
            storeOptions: TestJobStores.StoreOptions(DataSourceName),
            dbProvider: dbProvider,
            driverDelegate: driverDelegate) with
        {
            LockHandler = null,
        };
    }

    protected override async ValueTask<IJobStore> CreateStore()
    {
        string connectionString = await PrepareDatabase();

        // The store reads through the provider it is constructed with, so the test only has to build
        // one.
        IDbProvider dbProvider = new DbProvider(DbProviderName, connectionString);

        AdoJobStoreBase store = CreateJobStore(dbProvider, CreateDriverDelegate());

        await store.Initialize(new SchedulerIdentity { SchedulerName = SchedulerName, InstanceId = InstanceId });

        // Whatever the previous test left behind is this scheduler name's rows, and nothing else is.
        await store.Clear();

        return store;
    }
}
