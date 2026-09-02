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

using Quartz.Extensions.Redis;
using Quartz.Impl.AdoJobStore;

using StackExchange.Redis;

namespace Quartz.Tests.Unit.Extensions.Redis;

/// <summary>
/// What a scheduler's shutdown does to the Redis connection its lock handler opened.
/// </summary>
/// <remarks>
/// <para>
/// #3639: the handler opened a <c>ConnectionMultiplexer</c> on its first lock and nothing ever closed
/// it, because <see cref="ILockHandler" /> had no member that could. A host that built a scheduler,
/// ran it and shut it down therefore left a live Redis connection and its heartbeat behind — once per
/// scheduler, for the life of the process.
/// </para>
/// <para>
/// The multiplexer is substituted rather than connected: StackExchange.Redis has no in-process server,
/// and the round trip is not what is under test. <c>Quartz.Tests.Integration</c>'s
/// <c>RedisLockHandlerTest</c> is where a real server takes part.
/// </para>
/// </remarks>
public sealed class RedisLockHandlerShutdownTest
{
    private static RedisLockHandler Handler(IConnectionMultiplexer connection) => new()
    {
        KeyPrefix = "quartz:unit:lock:",
        Connect = _ => Task.FromResult(connection),
    };

    /// <summary>
    /// How long an acquire may take before the test gives up. Not a timing assertion — the handler
    /// polls <c>SET NX</c> until it wins, so a stub that answered "not taken" would loop for ever and
    /// hang the run instead of failing it.
    /// </summary>
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A multiplexer that answers one <c>SET NX</c> with success, so the handler gets as far as opening
    /// and keeping a connection — which is the state the leak was in.
    /// </summary>
    /// <remarks>
    /// The four-argument <c>StringSetAsync</c>, because that is the overload the handler binds to:
    /// <c>StringSetAsync(key, value, ttl, When.NotExists)</c> matches it exactly, so stubbing the
    /// five-argument one would leave the real call answered by a dummy.
    /// </remarks>
    private static IConnectionMultiplexer ConnectedMultiplexer()
    {
        IDatabase database = A.Fake<IDatabase>();
        A.CallTo(() => database.StringSetAsync(A<RedisKey>._, A<RedisValue>._, A<TimeSpan?>._, A<When>._))
            .Returns(true);

        IConnectionMultiplexer connection = A.Fake<IConnectionMultiplexer>();
        A.CallTo(() => connection.GetDatabase(A<int>._, A<object>._)).Returns(database);

        return connection;
    }

    private static Task<bool> Acquire(RedisLockHandler lockHandler) =>
        lockHandler.AcquireLock(Guid.NewGuid(), null, SchedulerLock.TriggerAccess).AsTask().WaitAsync(GiveUpAfter);

    [Test]
    public async Task AShutdownClosesTheConnectionTheHandlerOpened()
    {
        IConnectionMultiplexer connection = ConnectedMultiplexer();
        RedisLockHandler lockHandler = Handler(connection);

        (await Acquire(lockHandler)).Should().BeTrue();
        lockHandler.Connection.Should().BeSameAs(connection, "the handler holds the connection it opened");

        await lockHandler.Shutdown();

        // The multiplexer is the handler's, opened on its first lock, and a scheduler that has shut down
        // owns nothing - before #3639 nothing ever closed it.
        A.CallTo(() => connection.DisposeAsync()).MustHaveHappenedOnceExactly();
        lockHandler.Connection.Should().BeNull("the handler has let go of what it closed");
    }

    /// <summary>
    /// A handler that never took a lock never connected, so its shutdown has nothing to close and must
    /// not manufacture a connection in order to close one.
    /// </summary>
    [Test]
    public async Task AHandlerThatNeverLockedClosesNothing()
    {
        IConnectionMultiplexer connection = ConnectedMultiplexer();
        RedisLockHandler lockHandler = Handler(connection);

        await lockHandler.Shutdown();

        A.CallTo(() => connection.DisposeAsync()).MustNotHaveHappened();
        lockHandler.Connection.Should().BeNull();
    }

    /// <summary>
    /// Shutting down twice closes the connection once. The job store calls this once; a handler shared
    /// between two stores would not, and a second close of a multiplexer somebody else has since opened
    /// would take a live connection down.
    /// </summary>
    [Test]
    public async Task ASecondShutdownDoesNothing()
    {
        IConnectionMultiplexer connection = ConnectedMultiplexer();
        RedisLockHandler lockHandler = Handler(connection);

        await Acquire(lockHandler);

        await lockHandler.Shutdown();
        await lockHandler.Shutdown();

        A.CallTo(() => connection.DisposeAsync()).MustHaveHappenedOnceExactly();
    }
}
