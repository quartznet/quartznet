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
using Quartz.Impl.Calendar;
using Quartz.Jobs;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// What the ADO store's operations turn a failure into. Every operation reports through one guard, so
/// these are the guard's rules stated once as behaviour: the message names the operation, a failure the
/// store raised on purpose keeps its identity, and the original travels as the inner exception.
/// </summary>
public class AdoJobStoreFailureWrappingTest
{
    private static readonly JobKey TestJob = new JobKey("j1", "jg1");
    private static readonly TriggerKey TestTrigger = new TriggerKey("t1", "g1");

    private AdoJobStoreBaseTest.TestAdoJobStoreBase jobStore;
    private IDriverDelegate driverDelegate;
    private ConnectionAndTransactionHolder conn;

    [SetUp]
    public void SetUp()
    {
        jobStore = new AdoJobStoreBaseTest.TestAdoJobStoreBase();
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
    /// The database says a statement failed; the store owes the caller the operation that statement was
    /// for, and the original failure it can go on to classify.
    /// </summary>
    [Test]
    public async Task ADatabaseFailureIsReportedAsTheOperationItWasFor()
    {
        A.CallTo(() => driverDelegate.JobExists(conn, TestJob, A<CancellationToken>.Ignored))
            .ThrowsAsync(new InvalidOperationException("connection reset by peer"));

        Func<Task> act = async () => await jobStore.CallJobExists(conn, TestJob);

        await act.Should().ThrowAsync<JobPersistenceException>()
            .WithMessage("Couldn't check for existence of job: connection reset by peer")
            .WithInnerException<JobPersistenceException, InvalidOperationException>();
    }

    /// <summary>
    /// The trigger overload used to report a failure to look for a job, because its message was a copy
    /// of the job overload's.
    /// </summary>
    [Test]
    public async Task ATriggerLookupFailureSaysItWasLookingForATrigger()
    {
        A.CallTo(() => driverDelegate.TriggerExists(conn, TestTrigger, A<CancellationToken>.Ignored))
            .ThrowsAsync(new InvalidOperationException("connection reset by peer"));

        Func<Task> act = async () => await jobStore.CallTriggerExists(conn, TestTrigger);

        await act.Should().ThrowAsync<JobPersistenceException>()
            .WithMessage("Couldn't check for existence of trigger*");
    }

    /// <summary>
    /// A failure that is about the stored bytes rather than about the database says so, because
    /// "couldn't retrieve job" sends an operator to the wrong place entirely.
    /// </summary>
    [Test]
    public async Task AJobWhoseTypeWillNotLoadSaysThatIsWhatWentWrong()
    {
        A.CallTo(() => driverDelegate.SelectJobDetail(conn, TestJob, A<ITypeLoader>.Ignored, A<CancellationToken>.Ignored))
            .ThrowsAsync(new TypeLoadException("Reporting.NightlyJob"));

        Func<Task> act = async () => await jobStore.CallGetJob(conn, TestJob);

        await act.Should().ThrowAsync<JobPersistenceException>()
            .WithMessage("Couldn't retrieve job because a required type was not found*");
    }

    /// <inheritdoc cref="AJobWhoseTypeWillNotLoadSaysThatIsWhatWentWrong" />
    [Test]
    public async Task AJobWhoseBlobWillNotDeserializeSaysThatIsWhatWentWrong()
    {
        A.CallTo(() => driverDelegate.SelectJobDetail(conn, TestJob, A<ITypeLoader>.Ignored, A<CancellationToken>.Ignored))
            .ThrowsAsync(new IOException("unexpected end of stream"));

        Func<Task> act = async () => await jobStore.CallGetJob(conn, TestJob);

        await act.Should().ThrowAsync<JobPersistenceException>()
            .WithMessage("Couldn't retrieve job because the BLOB couldn't be deserialized*");
    }

    /// <summary>
    /// The one failure the store raises on purpose, and the one a caller catches by type to tell
    /// "already there" from "the store broke". Wrapping it would leave the answer inside
    /// <see cref="Exception.InnerException" />, where nobody looks.
    /// </summary>
    [Test]
    public async Task AJobThatIsAlreadyThereStaysAnObjectAlreadyExistsException()
    {
        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity(TestJob).Build();
        A.CallTo(() => driverDelegate.JobExists(conn, TestJob, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<bool>(true));

        Func<Task> act = async () => await jobStore.CallAddJob(conn, job, replace: false);

        await act.Should().ThrowAsync<ObjectAlreadyExistsException>(
            "a caller distinguishes 'already scheduled' from 'the database is down' by type");
    }

    /// <inheritdoc cref="AJobThatIsAlreadyThereStaysAnObjectAlreadyExistsException" />
    [Test]
    public async Task ACalendarThatIsAlreadyThereStaysAnObjectAlreadyExistsException()
    {
        A.CallTo(() => driverDelegate.CalendarExists(conn, "holidays", A<CancellationToken>.Ignored))
            .Returns(new ValueTask<bool>(true));

        Func<Task> act = async () => await jobStore.CallAddCalendar(
            conn, "holidays", new HolidayCalendar(), replace: false, updateTriggers: false);

        await act.Should().ThrowAsync<ObjectAlreadyExistsException>();
    }

    /// <summary>
    /// Recovery is a sequence of operations that each say what they could not do, so one of those
    /// answers reaches the caller as itself.
    /// </summary>
    [Test]
    public async Task RecoveryLetsAnOperationsOwnAnswerThrough()
    {
        A.CallTo(() => driverDelegate.UpdateTriggerStatesFromOtherStates(
                conn,
                A<StoredTriggerState>.Ignored,
                A<IReadOnlyCollection<StoredTriggerState>>.Ignored,
                A<CancellationToken>.Ignored))
            .ThrowsAsync(new JobPersistenceException("Couldn't free acquired triggers: deadlock victim"));

        Func<Task> act = async () => await jobStore.CallRecoverJobs(conn);

        await act.Should().ThrowAsync<JobPersistenceException>()
            .WithMessage("Couldn't free acquired triggers: deadlock victim",
                "a persistence failure raised inside recovery is already specific, and prefixing it says nothing new");
    }

    /// <summary>
    /// Anything else recovery runs into is still reported as a failure to recover, because a provider
    /// exception on its own names no scheduling operation at all.
    /// </summary>
    [Test]
    public async Task RecoveryStillReportsADatabaseFailureAsItsOwn()
    {
        A.CallTo(() => driverDelegate.UpdateTriggerStatesFromOtherStates(
                conn,
                A<StoredTriggerState>.Ignored,
                A<IReadOnlyCollection<StoredTriggerState>>.Ignored,
                A<CancellationToken>.Ignored))
            .ThrowsAsync(new InvalidOperationException("connection reset by peer"));

        Func<Task> act = async () => await jobStore.CallRecoverJobs(conn);

        await act.Should().ThrowAsync<JobPersistenceException>()
            .WithMessage("Couldn't recover jobs: connection reset by peer");
    }

    /// <summary>
    /// The trigger update's own refusals name the trigger they are about, so they travel unprefixed.
    /// </summary>
    [Test]
    public async Task ATriggerUpdateLetsItsOwnRefusalThrough()
    {
        IOperableTrigger stored = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(TestTrigger)
            .ForJob(TestJob)
            .StartNow()
            .Build();

        A.CallTo(() => driverDelegate.SelectTrigger(conn, TestTrigger, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<IOperableTrigger>(stored));
        A.CallTo(() => driverDelegate.CalendarExists(conn, "nope", A<CancellationToken>.Ignored))
            .Returns(new ValueTask<bool>(false));

        Func<Task> act = async () => await jobStore.CallUpdateTriggerDetails(
            conn, TestTrigger, new TriggerDetailsUpdate().WithCalendarName("nope"));

        await act.Should().ThrowAsync<JobPersistenceException>()
            .WithMessage("Calendar 'nope' does not exist.");
    }

    /// <inheritdoc cref="RecoveryStillReportsADatabaseFailureAsItsOwn" />
    [Test]
    public async Task ATriggerUpdateStillReportsADatabaseFailureAsItsOwn()
    {
        A.CallTo(() => driverDelegate.SelectTrigger(conn, TestTrigger, A<CancellationToken>.Ignored))
            .ThrowsAsync(new InvalidOperationException("connection reset by peer"));

        Func<Task> act = async () => await jobStore.CallUpdateTriggerDetails(
            conn, TestTrigger, new TriggerDetailsUpdate().WithPriority(7));

        await act.Should().ThrowAsync<JobPersistenceException>()
            .WithMessage("Couldn't update trigger details for 'g1.t1': connection reset by peer");
    }
}
