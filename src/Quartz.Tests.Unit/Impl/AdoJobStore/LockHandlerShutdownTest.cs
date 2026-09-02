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

using Microsoft.Extensions.Logging;

using Quartz.Impl.AdoJobStore;
using Quartz.Tests.Unit.Plugin.History;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// A lock handler is told to close what it opened when the store it locks for shuts down.
/// </summary>
/// <remarks>
/// <see cref="ILockHandler" /> declared how to take a lock and how to give it back and nothing about
/// closing, so <c>RedisLockHandler</c>'s multiplexer outlived every scheduler that ever used one
/// (#3639). The hook is a default interface member, so a handler written before it exists still
/// compiles and still runs — it simply has nothing to close.
/// </remarks>
public sealed class LockHandlerShutdownTest
{
    /// <summary>
    /// The store's own teardown runs first: a handler is entitled to assume no acquire is in flight by
    /// the time it is asked to close, which is only true once the misfire handler and the cluster
    /// manager have stopped.
    /// </summary>
    [Test]
    public async Task TheStoreShutsItsLockHandlerDown()
    {
        ILockHandler lockHandler = A.Fake<ILockHandler>();

        LocalTransactionJobStore store = new(TestJobStores.Dependencies(lockHandler: lockHandler));

        await store.Shutdown();

        A.CallTo(() => lockHandler.Shutdown(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    /// <summary>
    /// The default body, reached through a handler that predates the member. Nothing to close and
    /// nothing to say: the point is that the store calling it is not a break for an implementation
    /// that never heard of it.
    /// </summary>
    [Test]
    public async Task AHandlerThatDoesNotImplementTheHookIsShutDownAllTheSame()
    {
        ILockHandler lockHandler = new HandlerFromBeforeTheHook();

        LocalTransactionJobStore store = new(TestJobStores.Dependencies(lockHandler: lockHandler));

        Func<Task> act = async () => await store.Shutdown();

        await act.Should().NotThrowAsync(
            "the hook is a default interface member precisely so that a handler compiled against 4.0's "
            + "first shape needs no change");
    }

    /// <summary>
    /// A handler that throws on the way down is reported, not allowed to abandon the shutdown: a
    /// scheduler stuck half-down is worse than a connection that outlives the process.
    /// </summary>
    [Test]
    public async Task AHandlerThatThrowsOnTheWayDownIsLoggedRatherThanAllowedToStopTheShutdown()
    {
        ILockHandler lockHandler = A.Fake<ILockHandler>();
        A.CallTo(() => lockHandler.Shutdown(A<CancellationToken>._))
            .Throws(new InvalidOperationException("the connection had already gone"));

        RecordingLoggerProvider recorder = new();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(recorder));

        LocalTransactionJobStore store = new(TestJobStores.Dependencies(
            lockHandler: lockHandler,
            loggerFactory: loggerFactory));

        Func<Task> act = async () => await store.Shutdown();

        await act.Should().NotThrowAsync(
            "the store is shutting down, and a handler that cannot close is a leak rather than a reason "
            + "to leave the scheduler wedged");

        recorder.Entries.Should().Contain(x => x.Message.Contains("failed to shut down", StringComparison.Ordinal),
            "an operator whose connection is leaked has to be able to read why");
    }

    /// <summary>
    /// A handler that declares everything the interface declared before the hook existed, and nothing
    /// more. Its <c>Shutdown</c> is the interface's own body.
    /// </summary>
    private sealed class HandlerFromBeforeTheHook : ILockHandler
    {
        public ValueTask<bool> AcquireLock(
            Guid requestorId,
            ConnectionAndTransactionHolder? conn,
            SchedulerLock lockKind,
            CancellationToken cancellationToken = default) => new(true);

        public ValueTask ReleaseLock(
            Guid requestorId,
            SchedulerLock lockKind,
            CancellationToken cancellationToken = default) => default;

        public bool RequiresConnection => false;
    }
}
