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

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Configuration;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// The discovery's timers across a host stopping (#4229).
/// </summary>
/// <remarks>
/// A host started with <c>Run</c> or <c>RunAsync</c> stops itself once its stopping token fires, so a
/// second stop from elsewhere - an application's own <c>StopAsync</c>, a test host's dispose - runs the
/// hosted service's <c>StopAsync</c> at the same time. The timer whose disposal starts the second stop
/// makes the overlap happen on every run rather than now and then.
/// </remarks>
public sealed class AttachedStoreDiscoveryTest
{
    private SqliteTestDatabase database = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        database = new SqliteTestDatabase("attached-store-discovery");
    }

    [TearDown]
    public void DeleteDatabase()
    {
        database.Dispose();
    }

    [Test]
    public async Task AStopThatArrivesWhileAnotherIsDisposingTheTimersDoesNotThrow()
    {
        HookedTimeProvider time = new();
        await using ServiceProvider application = Application(time);
        await using AttachedStores stores = new(new SchedulerWindowRegistry(), new SchedulerTargets());
        await using AttachedStoreDiscovery discovery = new(stores, [Watched("prod"), Watched("reporting")], application);

        await discovery.StartAsync(CancellationToken.None);
        time.Timers.Should().HaveCount(2);

        time.OnFirstDispose = () => discovery.StopAsync(CancellationToken.None).GetAwaiter().GetResult();

        Func<Task> stop = () => discovery.StopAsync(CancellationToken.None);
        await stop.Should().NotThrowAsync("a second stop may arrive while the first is still disposing the timers");
        time.Timers.Should().OnlyContain(timer => timer.Disposed);
    }

    [Test]
    public async Task ATimerMadeAfterAStopHasBegunIsDisposedRatherThanKept()
    {
        HookedTimeProvider time = new();
        await using ServiceProvider application = Application(time);
        await using AttachedStores stores = new(new SchedulerWindowRegistry(), new SchedulerTargets());
        await using AttachedStoreDiscovery discovery = new(stores, [Watched("prod")], application);

        await discovery.StopAsync(CancellationToken.None);
        await discovery.StartAsync(CancellationToken.None);

        time.Timers.Should().ContainSingle().Which.Disposed.Should().BeTrue(
            "a stop that has begun has already taken the timers, and one added after it would run for ever");
    }

    private AttachedStoreDescriptor Watched(string target)
    {
        return new AttachedStoreDescriptor(target, store => store.UseSqlite(SqliteFactory.Instance, database.ConnectionString), TimeSpan.FromMinutes(1));
    }

    private static ServiceProvider Application(TimeProvider time)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(time);
        services.AddQuartz("core", q => q.UseInMemoryStore());
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Timers that never fire, and a hook the first disposal of any of them runs.
    /// </summary>
    private sealed class HookedTimeProvider : TimeProvider
    {
        public List<HookedTimer> Timers { get; } = [];

        public Action OnFirstDispose { get; set; }

        public override ITimer CreateTimer(TimerCallback callback, object state, TimeSpan dueTime, TimeSpan period)
        {
            HookedTimer timer = new(this);
            Timers.Add(timer);
            return timer;
        }

        public void Disposing()
        {
            Action hook = OnFirstDispose;
            OnFirstDispose = null;
            hook?.Invoke();
        }
    }

    private sealed class HookedTimer(HookedTimeProvider owner) : ITimer
    {
        public bool Disposed { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period) => !Disposed;

        public void Dispose()
        {
            if (Disposed)
            {
                return;
            }

            Disposed = true;
            owner.Disposing();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }
    }
}
