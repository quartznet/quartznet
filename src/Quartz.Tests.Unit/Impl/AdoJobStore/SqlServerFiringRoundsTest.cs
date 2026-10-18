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

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// SQL Server's firing rounds work as in 4.5 in this release, as with <c>LockFreeFiringRounds</c> off
/// (#4117): every round is locked, and a round of one is 4.5's two transactions whatever else is configured.
/// </summary>
/// <remarks>
/// With 4.6's round two SQL Server nodes acquiring one trigger at a time ran seconds behind their schedule,
/// locked or not, against tenths of a second in 4.5's shape.
/// </remarks>
public sealed class SqlServerFiringRoundsTest
{
    [Test]
    public void EveryRoundOnSqlServerIsLockedAndSaysWhy()
    {
        AdoJobStoreBaseTest.TestAdoJobStoreBase store = new() { DirectDelegate = new SqlServerDelegate() };

        store.StaticRoundLockReason.Should().Be(RoundLockReason.SqlServer);
        RoundLockReason.SqlServer.ToTag().Should().Be("sql_server", "the reason tag on the acquisition instruments says why every round is locked");
        RoundLockReason.SqlServer.Describe().Should().Contain("as in 4.5").And.Contain("#4117",
            "the startup line says the rounds are 4.5's in this release and where that is tracked");
    }

    [Test]
    public void TheOptionOffStillSaysItself()
    {
        AdoJobStoreBaseTest.TestAdoJobStoreBase store = new(configureStore: options => options.LockFreeFiringRounds = false)
        {
            DirectDelegate = new SqlServerDelegate(),
        };

        store.StaticRoundLockReason.Should().Be(RoundLockReason.OptionOff, "an operator who turned the option off reads that, on any database");
    }

    [Test]
    public void TheOtherShippedDialectsTakeLockFreeRounds()
    {
        AdoJobStoreBaseTest.TestAdoJobStoreBase store = new() { DirectDelegate = new PostgreSQLDelegate() };

        store.StaticRoundLockReason.Should().BeNull();
    }

    /// <summary>
    /// A round of one on SQL Server is 4.5's even when it counts against a cluster-scoped limit, as with the
    /// option off; any other reason keeps such a round folded, so the limit stays exact.
    /// </summary>
    [TestCase("sql_server", true)]
    [TestCase("option_off", true)]
    [TestCase("older_peer", false)]
    [TestCase("cluster_limit", false)]
    public void ARoundOfOneCountingAgainstAClusterScopedLimitIs45sOnlyWhenTheRoundsAre(string tag, bool asIn45)
    {
        RoundLockReason reason = Enum.GetValues<RoundLockReason>().Single(x => x.ToTag() == tag);
        AdoJobStoreBaseTest.TestAdoJobStoreBase store = new() { DirectDelegate = new SqlServerDelegate() };
        TriggerAcquisitionRequest request = new()
        {
            NoLaterThan = DateTimeOffset.UtcNow,
            MaxCount = 1,
            ExecutionLimits = ExecutionLimitsBuilder.Create().ForGroup("batch", 2, ExecutionLimitScope.Cluster).Build(),
        };

        store.FiresRoundOfOneAsIn45(request, reason).Should().Be(asIn45);
    }

    [Test]
    public void ARoundOfMoreThanOneStaysFolded()
    {
        AdoJobStoreBaseTest.TestAdoJobStoreBase store = new() { DirectDelegate = new SqlServerDelegate() };

        store.FiresRoundOfOneAsIn45(new TriggerAcquisitionRequest { NoLaterThan = DateTimeOffset.UtcNow, MaxCount = 2 }, RoundLockReason.SqlServer)
            .Should().BeFalse("4.5 fired a batch in one locked transaction too");
    }
}
