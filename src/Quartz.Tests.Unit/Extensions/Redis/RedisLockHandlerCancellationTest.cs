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

using Quartz.Extensions.Redis;
using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Extensions.Redis;

/// <summary>
/// What <see cref="RedisLockHandler" /> answers when an acquire does not end in a lock, which is the
/// half of its behaviour that needs no Redis server: the local gate it takes before the round-trip is a
/// <see cref="SemaphoreSlim" /> in this process, and the round-trip itself can be made to fail by
/// pointing the handler at nothing.
/// </summary>
/// <remarks>
/// It answered <see langword="false" /> to a cancelled acquire, which is the store's word for a
/// re-entrant one — "you already hold this, do not release it" — so the caller would have gone on to run
/// the operation the lock exists to guard with nothing holding it, and would have released nothing on
/// the way out. #3583. The paths that need a live server — cancellation while the <c>SET NX</c> loop is
/// polling — are covered by <c>Quartz.Tests.Integration</c>.
/// </remarks>
public sealed class RedisLockHandlerCancellationTest
{
    /// <summary>
    /// An endpoint nothing listens on, so the Redis round-trip fails rather than succeeding or hanging.
    /// Port 1 is refused immediately; the timeout is a backstop for a host that drops instead.
    /// </summary>
    private const string UnreachableRedis = "127.0.0.1:1,connectTimeout=1000,connectRetry=1,abortConnect=true";

    /// <summary>
    /// How long an acquire may take before the test gives up. Not a timing assertion — it decides
    /// whether a handler that never answers is reported as a failure or hangs the run.
    /// </summary>
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(30);

    private static RedisLockHandler Handler() => new()
    {
        RedisConfiguration = UnreachableRedis,
        KeyPrefix = "quartz:unit:lock:",
    };

    [Test]
    public async Task ACancelledAcquireReportsTheCancellationRatherThanAnsweringFalse()
    {
        RedisLockHandler lockHandler = Handler();

        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Task<bool> cancelled = lockHandler.AcquireLock(Guid.NewGuid(), null, SchedulerLock.TriggerAccess, cancellation.Token).AsTask();

        Func<Task> act = () => cancelled.WaitAsync(GiveUpAfter);
        await act.Should().ThrowAsync<OperationCanceledException>(
            "false says the calling context already holds the lock, so a caller told that would run its "
            + "guarded operation unlocked");

        cancelled.IsCanceled.Should().BeTrue(
            "the caller asked to stop, so this is a cancelled task rather than a faulted one — a caller "
            + "matching on cancellation has to be able to tell it from Redis being unreachable");
    }

    /// <summary>
    /// The cancelled attempt never reached Redis, so it holds no part of the lock — including the local
    /// gate that every requestor in this process queues on. That the next requestor gets as far as Redis
    /// at all is the assertion: a leaked gate would leave it waiting forever instead.
    /// </summary>
    [Test]
    public async Task ACancelledAcquireLeavesTheLocalGateForTheNextRequestor()
    {
        RedisLockHandler lockHandler = Handler();

        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Func<Task> cancelled = async () => await lockHandler.AcquireLock(Guid.NewGuid(), null, SchedulerLock.TriggerAccess, cancellation.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();

        Func<Task> next = () => lockHandler.AcquireLock(Guid.NewGuid(), null, SchedulerLock.TriggerAccess).AsTask().WaitAsync(GiveUpAfter);

        await next.Should().ThrowAsync<LockException>(
            "reaching Redis and failing there is the proof that the gate was free; had the cancelled "
            + "acquire kept it, this request would still be queued behind a lock nobody owns");
    }

    /// <summary>
    /// The same of an acquire that fails at Redis instead of being cancelled: the gate is given back
    /// before the <see cref="LockException" /> escapes, so one unreachable moment does not shut every
    /// requestor in the process out permanently.
    /// </summary>
    [Test]
    public async Task AnAcquireThatFailsAtRedisGivesTheLocalGateBack()
    {
        RedisLockHandler lockHandler = Handler();

        Func<Task> first = () => lockHandler.AcquireLock(Guid.NewGuid(), null, SchedulerLock.TriggerAccess).AsTask().WaitAsync(GiveUpAfter);
        await first.Should().ThrowAsync<LockException>();

        Func<Task> second = () => lockHandler.AcquireLock(Guid.NewGuid(), null, SchedulerLock.TriggerAccess).AsTask().WaitAsync(GiveUpAfter);
        await second.Should().ThrowAsync<LockException>(
            "the failed acquire released the gate it had taken, so the next one is answered rather than "
            + "queued behind it");
    }
}
