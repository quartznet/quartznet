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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using FakeItEasy;

using Quartz.Impl.AdoJobStore;
using Quartz.Logging;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// A lock handler is told to close what it opened when the store it locks for shuts down.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ISemaphore" /> declared how to take a lock and how to give it back and nothing about
/// closing, so <c>RedisSemaphore</c>'s multiplexer outlived every scheduler that ever used one
/// (#3639). The interface cannot gain a member on this branch — <c>netstandard2.0</c> and
/// <c>net462</c> have no default interface members — so a handler that holds something says so by
/// being <see cref="IDisposable" /> or <c>IAsyncDisposable</c>, and the store's shutdown honours it.
/// </para>
/// <para>
/// Non-parallelizable because the logging test hands the process-wide <see cref="LogProvider" /> a
/// recorder of its own.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class LockHandlerShutdownTest
{
    [TearDown]
    public void ForgetTheRecordingLogProvider()
    {
        LogProvider.SetCurrentLogProvider(null);
    }

    /// <summary>
    /// The store's own teardown runs first: a handler is entitled to assume no acquire is in flight by
    /// the time it is asked to close, which is only true once the misfire handler, the cluster manager
    /// and the connection manager have stopped.
    /// </summary>
    [Test]
    public async Task TheStoreClosesItsLockHandler()
    {
        RecordingSemaphore lockHandler = new RecordingSemaphore();
        JobStoreSupportTest.TestJobStoreSupport store = new JobStoreSupportTest.TestJobStoreSupport
        {
            LockHandler = lockHandler
        };

        await store.Shutdown();

        lockHandler.Closed.Should().Be(1,
            "whatever the handler opened - a Redis multiplexer, a connection, its heartbeat - outlives "
            + "the scheduler until the store closes it");
    }

#if NETCORE
    /// <summary>
    /// Where the framework has <c>IAsyncDisposable</c>, that is the one the store reaches for; the
    /// <c>net462</c> and <c>netstandard2.0</c> builds cannot see the interface at all and take the
    /// synchronous close instead, which is why a handler that holds a connection implements both.
    /// </summary>
    [Test]
    public async Task TheAsynchronousCloseIsPreferredWhereTheFrameworkHasOne()
    {
        RecordingSemaphore lockHandler = new RecordingSemaphore();
        JobStoreSupportTest.TestJobStoreSupport store = new JobStoreSupportTest.TestJobStoreSupport
        {
            LockHandler = lockHandler
        };

        await store.Shutdown();

        lockHandler.ClosedAsynchronously.Should().BeTrue(
            "closing a Redis connection is I/O, and a shutdown that can await it should not block on it");
    }
#endif

    /// <summary>
    /// Every row-lock handler in the box holds nothing of its own, so it implements neither interface
    /// and the store leaves it alone. The point is that the new call is not a break for a handler that
    /// never heard of it.
    /// </summary>
    [Test]
    public async Task AHandlerThatHoldsNothingIsLeftAlone()
    {
        ISemaphore lockHandler = A.Fake<ISemaphore>();
        JobStoreSupportTest.TestJobStoreSupport store = new JobStoreSupportTest.TestJobStoreSupport
        {
            LockHandler = lockHandler
        };

        Func<Task> act = async () => await store.Shutdown();

        await act.Should().NotThrowAsync(
            "an ISemaphore written against the 3.x shape declares no way of closing, and the store has "
            + "to shut down all the same");
    }

    /// <summary>
    /// A store that was never initialized never chose a lock handler, and a host that failed during
    /// startup still shuts its scheduler down.
    /// </summary>
    [Test]
    public async Task AStoreThatNeverChoseALockHandlerShutsDownAllTheSame()
    {
        JobStoreSupportTest.TestJobStoreSupport store = new JobStoreSupportTest.TestJobStoreSupport();

        Func<Task> act = async () => await store.Shutdown();

        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// A handler that throws on the way down is reported, not allowed to abandon the shutdown: a
    /// scheduler stuck half-down is worse than a connection that outlives the process.
    /// </summary>
    [Test]
    public async Task AHandlerThatThrowsOnTheWayDownIsLoggedRatherThanAllowedToStopTheShutdown()
    {
        RecordingLogProvider recorder = new RecordingLogProvider();
        LogProvider.SetCurrentLogProvider(recorder);

        // After the provider is set: JobStoreSupport takes its logger in its constructor.
        JobStoreSupportTest.TestJobStoreSupport store = new JobStoreSupportTest.TestJobStoreSupport
        {
            LockHandler = new RecordingSemaphore { ThrowOnClose = true }
        };

        Func<Task> act = async () => await store.Shutdown();

        await act.Should().NotThrowAsync(
            "the store is shutting down, and a handler that cannot close is a leak rather than a reason "
            + "to leave the scheduler wedged");

        recorder.Warnings.Should().Contain(x => x.Contains("failed to shut down"),
            "an operator whose connection is leaked has to be able to read why");
        recorder.Warnings.Should().Contain(x => x.Contains(nameof(RecordingSemaphore)),
            "and which handler leaked it, since a scheduler has exactly one and it is configurable");
    }

    /// <summary>
    /// A handler that holds something and says so both ways, which is the shape <c>RedisSemaphore</c>
    /// has: the asynchronous close where the framework has the interface, the synchronous one
    /// everywhere.
    /// </summary>
    private sealed class RecordingSemaphore : ISemaphore, IDisposable
#if NETCORE
        , IAsyncDisposable
#endif
    {
        public int Closed { get; private set; }

        public bool ClosedAsynchronously { get; private set; }

        public bool ThrowOnClose { get; set; }

        public bool RequiresConnection => false;

        public Task<bool> ObtainLock(
            Guid requestorId,
            ConnectionAndTransactionHolder conn,
            string lockName,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task ReleaseLock(
            Guid requestorId,
            string lockName,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            Closed++;
            if (ThrowOnClose)
            {
                throw new InvalidOperationException("the connection had already gone");
            }
        }

#if NETCORE
        public ValueTask DisposeAsync()
        {
            Closed++;
            ClosedAsynchronously = true;
            if (ThrowOnClose)
            {
                throw new InvalidOperationException("the connection had already gone");
            }

            return default;
        }
#endif
    }

    /// <summary>
    /// Collects what was logged at warning level or above. LibLog has no in-box recorder, and the
    /// message is the only thing an operator gets when a handler fails to close.
    /// </summary>
    private sealed class RecordingLogProvider : ILogProvider
    {
        private readonly List<string> warnings = new List<string>();

        public IReadOnlyList<string> Warnings
        {
            get
            {
                lock (warnings)
                {
                    return warnings.ToArray();
                }
            }
        }

        public Logger GetLogger(string name)
        {
            return (logLevel, messageFunc, exception, formatParameters) =>
            {
                // LibLog asks whether a level is enabled by passing no message.
                if (messageFunc is null)
                {
                    return true;
                }

                if (logLevel >= LogLevel.Warn)
                {
                    lock (warnings)
                    {
                        warnings.Add(messageFunc());
                    }
                }

                return true;
            };
        }

        public IDisposable OpenNestedContext(string message) => NoContext.Instance;

        public IDisposable OpenMappedContext(string key, object value, bool destructure = false) => NoContext.Instance;

        private sealed class NoContext : IDisposable
        {
            public static readonly NoContext Instance = new NoContext();

            public void Dispose()
            {
            }
        }
    }
}
