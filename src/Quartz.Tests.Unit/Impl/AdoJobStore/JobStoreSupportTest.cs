using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using FakeItEasy;

using FluentAssertions;

using NUnit.Framework;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

public class JobStoreSupportTest
{
    private TestJobStoreSupport jobStoreSupport;
    private IDriverDelegate driverDelegate;

    [SetUp]
    public void SetUp()
    {
        jobStoreSupport = new TestJobStoreSupport();
        driverDelegate = A.Fake<IDriverDelegate>();
        jobStoreSupport.DirectDelegate = driverDelegate;
    }

    [Test]
    public async Task TestRecoverMisfiredJobs_ShouldCheckForMisfiredTriggersInStateWaiting()
    {
        await jobStoreSupport.RecoverMisfiredJobs(null, false);

        A.CallTo(() => driverDelegate.HasMisfiredTriggersInState(
            A<ConnectionAndTransactionHolder>.Ignored,
            A<string>.That.IsEqualTo(AdoConstants.StateWaiting),
            A<DateTimeOffset>.Ignored,
            A<int>.Ignored,
            A<IList<TriggerKey>>.Ignored,
            CancellationToken.None)).MustHaveHappened();
    }

    [Test]
    public async Task TestExecuteInNonManagedTXLock_RetriesOnTransientException()
    {
        int callCount = 0;
        var store = CreateRetryTestStore();

        // Callback fails with transient exception on first call, succeeds on second
        string result = await store.CallExecuteInNonManagedTXLock<string>(conn =>
        {
            callCount++;
            if (callCount == 1)
            {
                throw new JobPersistenceException("transient", new TransientTestException());
            }
            return Task.FromResult("success");
        }, CancellationToken.None);

        result.Should().Be("success");
        callCount.Should().Be(2);
    }

    [Test]
    public async Task TestExecuteInNonManagedTXLock_StopsRetryingAfterMaxRetries()
    {
        int callCount = 0;
        var store = CreateRetryTestStore(maxTransientRetries: 2);

        // Callback always throws transient exception
        Func<Task> act = async () => await store.CallExecuteInNonManagedTXLock<string>(conn =>
        {
            callCount++;
            throw new JobPersistenceException("transient", new TransientTestException());
        }, CancellationToken.None);

        await act.Should().ThrowAsync<JobPersistenceException>();
        // Initial attempt + 2 retries = 3 total
        callCount.Should().Be(3);
    }

    [Test]
    public async Task TestExecuteInNonManagedTXLock_DoesNotRetryNonTransientException()
    {
        int callCount = 0;
        var store = CreateRetryTestStore();

        // Non-transient exception should not be retried
        Func<Task> act = async () => await store.CallExecuteInNonManagedTXLock<string>(conn =>
        {
            callCount++;
            throw new JobPersistenceException("non-transient");
        }, CancellationToken.None);

        await act.Should().ThrowAsync<JobPersistenceException>().WithMessage("non-transient");
        callCount.Should().Be(1);
    }

    [Test]
    public async Task TestExecuteInNonManagedTXLock_NoRetryWhenMaxTransientRetriesIsZero()
    {
        int callCount = 0;
        var store = CreateRetryTestStore(maxTransientRetries: 0);

        // With MaxTransientRetries = 0, transient exceptions should not be retried
        Func<Task> act = async () => await store.CallExecuteInNonManagedTXLock<string>(conn =>
        {
            callCount++;
            throw new JobPersistenceException("transient", new TransientTestException());
        }, CancellationToken.None);

        await act.Should().ThrowAsync<JobPersistenceException>();
        callCount.Should().Be(1);
    }

    private static RetryTestJobStoreSupport CreateRetryTestStore(int maxTransientRetries = 3)
    {
        return new RetryTestJobStoreSupport
        {
            MaxTransientRetries = maxTransientRetries,
            TransientRetryInterval = TimeSpan.Zero,
        };
    }

    public class TestJobStoreSupport : JobStoreSupport
    {
        protected override ConnectionAndTransactionHolder GetNonManagedTXConnection()
        {
            return new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
        }

        protected override Task<T> ExecuteInLock<T>(
            string lockName,
            Func<ConnectionAndTransactionHolder, Task<T>> txCallback,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(default(T));
        }

        /// <summary>
        /// sets delegate directly
        /// </summary>
        internal IDriverDelegate DirectDelegate
        {
            set
            {
                FieldInfo fieldInfo = typeof(JobStoreSupport).GetField("driverDelegate", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(fieldInfo);
                fieldInfo.SetValue(this, value);
            }
        }
    }

    /// <summary>
    /// A <see cref="JobStoreSupport"/> subclass used to test retry logic in
    /// <see cref="JobStoreSupport.ExecuteInNonManagedTXLock{T}"/>.
    /// </summary>
    public sealed class RetryTestJobStoreSupport : JobStoreSupport
    {
        protected override ConnectionAndTransactionHolder GetNonManagedTXConnection()
        {
            // Return a holder with a mock connection and no transaction
            return new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
        }

        protected override Task<T> ExecuteInLock<T>(
            string lockName,
            Func<ConnectionAndTransactionHolder, Task<T>> txCallback,
            CancellationToken cancellationToken = default)
        {
            return ExecuteInNonManagedTXLock(lockName, txCallback, cancellationToken);
        }

        protected override bool IsTransient(Exception ex)
        {
            // Mark JobPersistenceException wrapping TransientTestException as transient
            return ex is JobPersistenceException { InnerException: TransientTestException };
        }

        public Task<T> CallExecuteInNonManagedTXLock<T>(
            Func<ConnectionAndTransactionHolder, Task<T>> txCallback,
            CancellationToken cancellationToken)
        {
            return ExecuteInNonManagedTXLock(null, txCallback, cancellationToken);
        }
    }

    /// <summary>
    /// A test exception that will be recognized as transient by <see cref="RetryTestJobStoreSupport"/>.
    /// </summary>
    public sealed class TransientTestException : Exception
    {
        public TransientTestException() : base("Simulated transient database error (e.g. deadlock)")
        {
        }
    }
}