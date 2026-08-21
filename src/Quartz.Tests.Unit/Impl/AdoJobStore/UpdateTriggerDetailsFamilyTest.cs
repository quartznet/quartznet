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

using System.Data.Common;

using FakeItEasy;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Jobs;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The ADO half of the misfire-instruction family check that <c>UpdateTriggerDetails</c> makes.
/// </summary>
/// <remarks>
/// Both stores call the same <c>EnsureMisfireInstructionMatchesFamily</c>, but each from its own
/// method, so "the ADO store validates too" was until now an assumption rather than a fact. These
/// tests drive the whole 5 × 5 matrix in <see cref="MisfireInstructionFamilyCases" /> through the
/// faked-<see cref="IDriverDelegate" /> store; <c>UpdateTriggerDetailsTest</c> drives the same list
/// through <c>RAMJobStore</c>. A divergence between the two now fails a test rather than reaching a
/// user whose trigger silently acquired a policy from another family.
/// </remarks>
public class UpdateTriggerDetailsFamilyTest
{
    private static readonly TriggerKey TestTrigger = new TriggerKey("t1", "g1");
    private static readonly JobKey TestJob = new JobKey("j1", "jg1");

    private FamilyValidationStore jobStore;
    private IDriverDelegate driverDelegate;
    private ConnectionAndTransactionHolder conn;

    [SetUp]
    public void SetUp()
    {
        jobStore = new FamilyValidationStore();
        driverDelegate = A.Fake<IDriverDelegate>();
        jobStore.DirectDelegate = driverDelegate;
        jobStore.DirectSignaler = A.Fake<ISchedulerSignaler>();
        conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
    }

    [TearDown]
    public void TearDown()
    {
        conn?.Dispose();
    }

    /// <summary>
    /// Arranges the reads <c>UpdateTriggerDetails</c> makes, and returns the trigger the store will
    /// find - the same instance the store mutates, so the assertions can read the outcome off it.
    /// </summary>
    private IOperableTrigger GivenStoredTrigger(MisfireInstructionFamilyCase testCase)
    {
        IOperableTrigger stored = testCase.CreateTrigger(TestTrigger, TestJob);
        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity(TestJob).Build();

        A.CallTo(() => driverDelegate.SelectTrigger(conn, TestTrigger, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<IOperableTrigger>(stored));
        A.CallTo(() => driverDelegate.SelectTriggerState(conn, TestTrigger, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<StoredTriggerState>(StoredTriggerState.Waiting));
        A.CallTo(() => driverDelegate.SelectJobForTrigger(conn, TestTrigger, A<ITypeLoadHelper>.Ignored, true, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<IJobDetail>(job));

        stored.MisfireInstructionCode.Should().Be(MisfireInstruction.SmartPolicy,
            "the fixture needs a trigger whose instruction the update would visibly change");
        return stored;
    }

    [TestCaseSource(typeof(MisfireInstructionFamilyCases), nameof(MisfireInstructionFamilyCases.Mismatched))]
    public async Task MisfireInstruction_FromAnotherFamilyIsRejected(MisfireInstructionFamilyCase testCase)
    {
        IOperableTrigger stored = GivenStoredTrigger(testCase);

        Func<Task> act = async () => await jobStore.CallUpdateTriggerDetails(conn, TestTrigger, testCase.CreateUpdate());

        await act.Should().ThrowAsync<JobPersistenceException>()
            .WithMessage($"*{testCase.RequestedName}*{testCase.StoredName}*",
                "the message has to name both families, because the whole problem is that the code alone names neither");

        stored.MisfireInstructionCode.Should().Be(MisfireInstruction.SmartPolicy,
            "a rejected update must leave the trigger as it was");
        A.CallTo(() => driverDelegate.UpdateTrigger(
            A<ConnectionAndTransactionHolder>.Ignored,
            A<IOperableTrigger>.Ignored,
            A<StoredTriggerState>.Ignored,
            A<IJobDetail>.Ignored,
            A<CancellationToken>.Ignored)).MustNotHaveHappened();
    }

    [TestCaseSource(typeof(MisfireInstructionFamilyCases), nameof(MisfireInstructionFamilyCases.Matching))]
    public async Task MisfireInstruction_FromItsOwnFamilyIsApplied(MisfireInstructionFamilyCase testCase)
    {
        IOperableTrigger stored = GivenStoredTrigger(testCase);

        bool result = await jobStore.CallUpdateTriggerDetails(conn, TestTrigger, testCase.CreateUpdate());

        result.Should().BeTrue();
        stored.MisfireInstructionCode.Should().Be(testCase.InstructionCode);
        // Storing the instruction on the in-memory trigger is only half of it; the row has to be written.
        A.CallTo(() => driverDelegate.UpdateTrigger(conn, stored, StoredTriggerState.Waiting, A<IJobDetail>.Ignored, A<CancellationToken>.Ignored))
            .MustHaveHappenedOnceExactly();
    }

    // Matching() is the diagonal, so this runs once per stored trigger family - the update it carries
    // is unused here, since the point is the overload that names no family at all.
    [TestCaseSource(typeof(MisfireInstructionFamilyCases), nameof(MisfireInstructionFamilyCases.Matching))]
    public async Task MisfireInstructionCode_SkipsTheFamilyCheckAltogether(MisfireInstructionFamilyCase testCase)
    {
        IOperableTrigger stored = GivenStoredTrigger(testCase);

        // The bare-code overload carries no family, so there is nothing to disagree with the stored
        // trigger and the code goes in as given. That is the escape hatch for a caller who read the
        // number off the wire rather than picking a policy.
        bool result = await jobStore.CallUpdateTriggerDetails(
            conn, TestTrigger, new TriggerDetailsUpdate().WithMisfireInstructionCode(testCase.InstructionCode));

        result.Should().BeTrue();
        stored.MisfireInstructionCode.Should().Be(testCase.InstructionCode,
            "WithMisfireInstructionCode names no family, so only the trigger's own range check applies");
    }

    /// <summary>
    /// Exposes the connection-taking <c>UpdateTriggerDetails</c>. The public entry point goes through
    /// <c>ExecuteInLock</c>, which the test store short-circuits to <c>default</c>.
    /// </summary>
    private sealed class FamilyValidationStore : AdoJobStoreBaseTest.TestAdoJobStoreBase
    {
        internal ValueTask<bool> CallUpdateTriggerDetails(
            ConnectionAndTransactionHolder conn,
            TriggerKey triggerKey,
            TriggerDetailsUpdate update)
        {
            return UpdateTriggerDetails(conn, triggerKey, update, CancellationToken.None);
        }
    }
}
