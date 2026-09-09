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
using System.Threading;
using System.Threading.Tasks;

using FakeItEasy;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.Redis;

using StackExchange.Redis;

namespace Quartz.Tests.Unit.Impl.Redis;

/// <summary>
/// What a scheduler's shutdown does to the Redis connection its lock handler opened.
/// </summary>
/// <remarks>
/// <para>
/// #3639: the handler opened a <c>ConnectionMultiplexer</c> on its first lock and nothing ever closed
/// it, because <see cref="ISemaphore" /> had no member that could and cannot gain one on this branch.
/// A host that built a scheduler, ran it and shut it down therefore left a live Redis connection and
/// its heartbeat behind — once per scheduler, for the life of the process.
/// </para>
/// <para>
/// The multiplexer is substituted rather than connected: StackExchange.Redis has no in-process server,
/// and the round trip is not what is under test. <c>Quartz.Tests.Integration</c>'s
/// <c>RedisSemaphoreTest</c> is where a real server takes part.
/// </para>
/// </remarks>
public sealed class RedisSemaphoreShutdownTest
{
    /// <summary>
    /// How long an acquire may take before the test gives up. Not a timing assertion — the handler
    /// polls <c>SET NX</c> until it wins, so a stub that answered "not taken" would loop for ever and
    /// hang the run instead of failing it.
    /// </summary>
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(30);

    private static RedisSemaphore Handler(IConnectionMultiplexer connection)
    {
        return new RedisSemaphore
        {
            KeyPrefix = "quartz:unit:lock:",
            Connect = _ => Task.FromResult(connection)
        };
    }

    /// <summary>
    /// A multiplexer that answers one <c>SET NX</c> with success, so the handler gets as far as opening
    /// and keeping a connection — which is the state the leak was in.
    /// </summary>
    /// <remarks>
    /// The four-argument <c>StringSetAsync</c>, because that is the overload the handler binds to:
    /// <c>StringSetAsync(key, value, ttl, When.NotExists)</c> matches it exactly, so stubbing another
    /// would leave the real call answered by a dummy.
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

    private static async Task<bool> Acquire(RedisSemaphore semaphore)
    {
        using (CancellationTokenSource giveUp = new CancellationTokenSource(GiveUpAfter))
        {
            return await semaphore.ObtainLock(Guid.NewGuid(), null, JobStoreSupport.LockTriggerAccess, giveUp.Token);
        }
    }

    [Test]
    public async Task AShutdownClosesTheConnectionTheHandlerOpened()
    {
        IConnectionMultiplexer connection = ConnectedMultiplexer();
        RedisSemaphore semaphore = Handler(connection);

        (await Acquire(semaphore)).Should().BeTrue();
        semaphore.Connection.Should().BeSameAs(connection, "the handler holds the connection it opened");

        await semaphore.DisposeAsync();

        // The multiplexer is the handler's, opened on its first lock, and a scheduler that has shut down
        // owns nothing - before #3639 nothing ever closed it.
        A.CallTo(() => connection.CloseAsync(A<bool>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => connection.Dispose()).MustHaveHappenedOnceExactly();
        semaphore.Connection.Should().BeNull("the handler has let go of what it closed");
    }

    /// <summary>
    /// The synchronous close is not a lesser one: it is what the <c>net462</c> and
    /// <c>netstandard2.0</c> builds of <c>Quartz</c> reach for, because those frameworks have no
    /// <c>IAsyncDisposable</c> for the store to test against.
    /// </summary>
    [Test]
    public async Task TheSynchronousCloseClosesTheSameConnection()
    {
        IConnectionMultiplexer connection = ConnectedMultiplexer();
        RedisSemaphore semaphore = Handler(connection);

        (await Acquire(semaphore)).Should().BeTrue();

        semaphore.Dispose();

        A.CallTo(() => connection.Close(A<bool>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => connection.Dispose()).MustHaveHappenedOnceExactly();
        semaphore.Connection.Should().BeNull();
    }

    /// <summary>
    /// A handler that never took a lock never connected, so its shutdown has nothing to close and must
    /// not manufacture a connection in order to close one.
    /// </summary>
    [Test]
    public async Task AHandlerThatNeverLockedClosesNothing()
    {
        IConnectionMultiplexer connection = ConnectedMultiplexer();
        RedisSemaphore semaphore = Handler(connection);

        await semaphore.DisposeAsync();

        A.CallTo(() => connection.CloseAsync(A<bool>._)).MustNotHaveHappened();
        A.CallTo(() => connection.Dispose()).MustNotHaveHappened();
        semaphore.Connection.Should().BeNull();
    }

    /// <summary>
    /// Closing twice closes the connection once. The job store calls this once; a handler shared
    /// between two stores would not, and a second close of a multiplexer somebody else has since opened
    /// would take a live connection down.
    /// </summary>
    [Test]
    public async Task ASecondShutdownDoesNothing()
    {
        IConnectionMultiplexer connection = ConnectedMultiplexer();
        RedisSemaphore semaphore = Handler(connection);

        (await Acquire(semaphore)).Should().BeTrue();

        await semaphore.DisposeAsync();
        await semaphore.DisposeAsync();
        semaphore.Dispose();

        A.CallTo(() => connection.CloseAsync(A<bool>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => connection.Dispose()).MustHaveHappenedOnceExactly();
    }
}
