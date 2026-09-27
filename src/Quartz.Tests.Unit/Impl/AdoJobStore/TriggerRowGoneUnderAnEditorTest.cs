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

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// What an editor holding <c>TRIGGER_ACCESS</c> does when the trigger row it read is gone by the time
/// it writes — which a completion running without the lock (#3863) is the one way to arrange.
/// </summary>
/// <remarks>
/// Under the lock a row an editor has read stays until the editor commits. A lock-free completion of
/// the row's last firing deletes it in between, and the editor's write matches nothing. Each editor
/// answers as it would have had it arrived after that completion: a replace stores the trigger as
/// new, a reschedule and an edit say there was nothing to reschedule or edit. The delegate is a fake,
/// so the row's disappearance is a row count of zero.
/// </remarks>
public sealed class TriggerRowGoneUnderAnEditorTest
{
    private const string Group = "editors";

    [Test]
    public async Task AReplaceWhoseRowWentBetweenItsReadAndItsWriteStoresTheTriggerAsNew()
    {
        (EditingStore store, IDriverDelegate driverDelegate) = Harness();
        IJobDetail job = Job();
        IOperableTrigger trigger = Trigger(job);

        A.CallTo(() => driverDelegate.TriggerExists(A<ConnectionAndTransactionHolder>._, trigger.Key, A<CancellationToken>._))
            .Returns(new ValueTask<bool>(true));
        A.CallTo(() => driverDelegate.UpdateTrigger(A<ConnectionAndTransactionHolder>._, trigger, A<StoredTriggerState>._, job, A<CancellationToken>._))
            .Returns(new ValueTask<int>(0));

        await store.AddTrigger(trigger, AddTriggerOptions.Replacing);

        // A replace that finds no row to update is a store of a trigger that is not there, as AddJob
        // already treats a job whose row went.
        A.CallTo(() => driverDelegate.InsertTrigger(A<ConnectionAndTransactionHolder>._, trigger, StoredTriggerState.Waiting, job, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task AReplaceWhoseRowIsStillThereUpdatesItAndInsertsNothing()
    {
        (EditingStore store, IDriverDelegate driverDelegate) = Harness();
        IJobDetail job = Job();
        IOperableTrigger trigger = Trigger(job);

        A.CallTo(() => driverDelegate.TriggerExists(A<ConnectionAndTransactionHolder>._, trigger.Key, A<CancellationToken>._))
            .Returns(new ValueTask<bool>(true));
        A.CallTo(() => driverDelegate.UpdateTrigger(A<ConnectionAndTransactionHolder>._, trigger, A<StoredTriggerState>._, job, A<CancellationToken>._))
            .Returns(new ValueTask<int>(1));

        await store.AddTrigger(trigger, AddTriggerOptions.Replacing);

        // The row took the update, so there is nothing to fall back to.
        A.CallTo(() => driverDelegate.InsertTrigger(A<ConnectionAndTransactionHolder>._, A<IOperableTrigger>._, A<StoredTriggerState>._, A<IJobDetail>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Test]
    public async Task ARescheduleWhoseTriggerWentUnderItSaysSoAndStoresNothing()
    {
        (EditingStore store, IDriverDelegate driverDelegate) = Harness();
        IJobDetail job = Job();
        IOperableTrigger replacement = Trigger(job);

        A.CallTo(() => driverDelegate.SelectJobForTrigger(A<ConnectionAndTransactionHolder>._, replacement.Key, A<ITypeLoader>._, A<bool>._, A<CancellationToken>._))
            .Returns(new ValueTask<IJobDetail?>(job));
        A.CallTo(() => driverDelegate.DeleteTrigger(A<ConnectionAndTransactionHolder>._, replacement.Key, A<CancellationToken>._))
            .Returns(new ValueTask<int>(0));

        bool replaced = await store.ReplaceTrigger(replacement.Key, replacement);

        replaced.Should().BeFalse("the trigger it read was gone by the time it went to delete it, which is the answer a caller arriving after the completion gets");
        // A reschedule of a trigger that is not there stores nothing, or the caller would be told
        // "not found" about a trigger that now exists.
        A.CallTo(() => driverDelegate.InsertTrigger(A<ConnectionAndTransactionHolder>._, A<IOperableTrigger>._, A<StoredTriggerState>._, A<IJobDetail>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Test]
    public async Task AnEditOfATriggerThatWentUnderItSaysSo()
    {
        (EditingStore store, IDriverDelegate driverDelegate) = Harness();
        IJobDetail job = Job();
        IOperableTrigger existing = Trigger(job);

        A.CallTo(() => driverDelegate.SelectTrigger(A<ConnectionAndTransactionHolder>._, existing.Key, A<CancellationToken>._))
            .Returns(new ValueTask<IOperableTrigger?>(existing));
        A.CallTo(() => driverDelegate.SelectJobForTrigger(A<ConnectionAndTransactionHolder>._, existing.Key, A<ITypeLoader>._, A<bool>._, A<CancellationToken>._))
            .Returns(new ValueTask<IJobDetail?>(job));
        A.CallTo(() => driverDelegate.UpdateTrigger(A<ConnectionAndTransactionHolder>._, existing, A<StoredTriggerState>._, job, A<CancellationToken>._))
            .Returns(new ValueTask<int>(0));

        bool updated = await store.UpdateTriggerDetails(existing.Key, new TriggerDetailsUpdate().WithDescription("edited"));

        updated.Should().BeFalse("an update that reached no row edited nothing, and true would say it did");
    }

    private static (EditingStore Store, IDriverDelegate Delegate) Harness()
    {
        EditingStore store = new();
        IDriverDelegate driverDelegate = A.Fake<IDriverDelegate>();
        store.DirectDelegate = driverDelegate;
        store.DirectSignaler = A.Fake<ISchedulerSignaler>();

        // The job every trigger here belongs to, read back by the store before it writes a trigger.
        IJobDetail job = Job();
        A.CallTo(() => driverDelegate.SelectJobDetail(A<ConnectionAndTransactionHolder>._, job.Key, A<ITypeLoader>._, A<CancellationToken>._))
            .Returns(new ValueTask<IJobDetail?>(job));

        return (store, driverDelegate);
    }

    /// <summary>The one job of these tests; two calls answer equal details.</summary>
    private static IJobDetail Job()
    {
        return JobBuilder.Create<EditedJob>().WithIdentity("job", Group).StoreDurably().Build();
    }

    private static IOperableTrigger Trigger(IJobDetail job)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity("trigger", Group)
            .ForJob(job)
            .StartAt(DateTimeOffset.UtcNow)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();

        trigger.ComputeFirstFireTimeUtc(calendar: null);
        return trigger;
    }

    /// <summary>
    /// The ADO harness from <see cref="AdoJobStoreBaseTest" />, with the lock wrapper running the work
    /// it is given so that the public editing members reach the delegate.
    /// </summary>
    private sealed class EditingStore : AdoJobStoreBaseTest.TestAdoJobStoreBase
    {
        protected override ValueTask<T> ExecuteInLock<T>(
            SchedulerLock? lockKind,
            Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback,
            CancellationToken cancellationToken = default)
        {
            return ExecuteInLocalTransactionLock(lockKind, txCallback, cancellationToken: cancellationToken);
        }
    }

    private sealed class EditedJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
