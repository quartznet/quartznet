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

using Quartz.Configuration;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// The resolution table: what a bare key and a targeted key each resolve to, out of a repository holding
/// local schedulers, windows and proxies with and without targets.
/// </summary>
public sealed class SchedulerLookupTest
{
    private SchedulerRepository repository = null!;
    private SchedulerWindowRegistry windows = null!;

    [SetUp]
    public void SetUp()
    {
        repository = new SchedulerRepository();
        windows = new SchedulerWindowRegistry();
    }

    [Test]
    public void ABareKeyResolvesToTheLocalScheduler()
    {
        IScheduler local = Local("reporting", "node-1");
        repository.Bind(local);

        SchedulerResolution? resolved = SchedulerLookup.Resolve(repository, windows, new SchedulerRef("reporting"));

        resolved.Should().NotBeNull();
        resolved!.Scheduler.Should().BeSameAs(local);
        resolved.Ref.Should().Be(new SchedulerRef("reporting"));
        resolved.IsWindow.Should().BeFalse();
    }

    [Test]
    public void ABareKeyResolvesToAnUntargetedProxyAndNeverToATargetedOne()
    {
        IScheduler targeted = Proxy("QuartzScheduler", "w1");
        IScheduler untargeted = Proxy("QuartzScheduler", target: null);
        repository.Bind(targeted);
        repository.Bind(untargeted);

        SchedulerResolution? resolved = SchedulerLookup.Resolve(repository, windows, new SchedulerRef("QuartzScheduler"));

        resolved!.Scheduler.Should().BeSameAs(untargeted,
            "a bare key is reserved for the scheduler reached through no target, which is what every key written before 4.5 meant");
    }

    [Test]
    public void ABareNameHeldOnlyByTargetsResolvesToNothingAndTheRefusalNamesThem()
    {
        repository.Bind(Proxy("QuartzScheduler", "w2"));
        repository.Bind(Proxy("QuartzScheduler", "w1"));

        SchedulerRef key = new("QuartzScheduler");

        SchedulerLookup.Resolve(repository, windows, key).Should().BeNull(
            "answering with an arbitrary one of the targets would act on a process the caller did not name");
        SchedulerLookup.NotFoundMessage(repository, key).Should()
            .Contain("'w1/QuartzScheduler'").And.Contain("'w2/QuartzScheduler'",
                "the keys that would have resolved are what the caller needs next");
    }

    [Test]
    public void ATargetedKeyResolvesToTheProxyBehindThatTargetIgnoringCase()
    {
        IScheduler w1 = Proxy("QuartzScheduler", "w1");
        IScheduler w2 = Proxy("QuartzScheduler", "w2");
        repository.Bind(w1);
        repository.Bind(w2);

        SchedulerResolution? resolved = SchedulerLookup.Resolve(repository, windows, SchedulerRef.Parse("W2/QuartzScheduler"));

        resolved!.Scheduler.Should().BeSameAs(w2);
        resolved.Ref.Key.Should().Be("w2/QuartzScheduler", "the resolution carries the identity as it would be listed");
    }

    [Test]
    public void ATargetedKeyResolvesToTheWindowOntoThatStore()
    {
        IScheduler window = Local("reporting", "WINDOW");
        repository.Bind(window);
        windows.Add("reporting", "prod");

        SchedulerResolution? byKey = SchedulerLookup.Resolve(repository, windows, SchedulerRef.Parse("prod/reporting"));
        SchedulerResolution? byName = SchedulerLookup.Resolve(repository, windows, new SchedulerRef("reporting"));

        byKey!.Scheduler.Should().BeSameAs(window);
        byKey.IsWindow.Should().BeTrue();
        byKey.Ref.Key.Should().Be("prod/reporting");

        byName!.Scheduler.Should().BeSameAs(window, "a window is reached by its bare name too, as it was before 4.5");
        byName.IsWindow.Should().BeTrue();
        byName.Ref.Key.Should().Be("prod/reporting", "whichever spelling resolved it, the identity is the listed one");
    }

    [Test]
    public void AWindowKeyNamingAnotherStoreResolvesToNothing()
    {
        repository.Bind(Local("reporting", "WINDOW"));
        windows.Add("reporting", "prod");

        SchedulerLookup.Resolve(repository, windows, SchedulerRef.Parse("staging/reporting")).Should().BeNull();
    }

    /// <summary>
    /// A scheduler of this process may contain <c>/</c>, and its exact name wins over a key that reads
    /// like <c>target/name</c>.
    /// </summary>
    [Test]
    public void ALocalSchedulerWhoseNameContainsTheSeparatorWinsOverTheTargetReading()
    {
        IScheduler local = Local("x/y", "node-1");
        IScheduler behindX = Proxy("y", "x");
        repository.Bind(local);
        repository.Bind(behindX);

        SchedulerResolution? resolved = SchedulerLookup.Resolve(repository, windows, SchedulerRef.Parse("x/y"));

        resolved!.Scheduler.Should().BeSameAs(local, "the exact bare match is unambiguous and the target reading is not");
        resolved.Ref.Should().Be(new SchedulerRef("x/y"));
        resolved.IsWindow.Should().BeFalse();
    }

    [Test]
    public void ATargetedKeyWithNoSuchTargetResolvesToNothing()
    {
        repository.Bind(Proxy("QuartzScheduler", "w1"));

        SchedulerLookup.Resolve(repository, windows, SchedulerRef.Parse("w9/QuartzScheduler")).Should().BeNull();
        SchedulerLookup.NotFoundMessage(repository, SchedulerRef.Parse("w9/QuartzScheduler"))
            .Should().Be("Scheduler 'w9/QuartzScheduler' was not found.");
    }

    [Test]
    public void TheRepositoryBindsAProxyUnderItsTarget()
    {
        repository.Bind(Proxy("QuartzScheduler", "w1"));
        repository.Bind(Proxy("QuartzScheduler", "w2"));

        repository.LookupByName("QuartzScheduler").Should().HaveCount(2,
            "two targets fronting schedulers of one name are two entries, told apart by the target");
        repository.Lookup("QuartzScheduler", "w2").Should().NotBeNull();

        Action third = () => repository.Bind(Proxy("QuartzScheduler", "W1"));
        third.Should().Throw<SchedulerException>("one target is one remote scheduler");
    }

    private static IScheduler Local(string name, string instanceId)
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.SchedulerName).Returns(name);
        A.CallTo(() => scheduler.SchedulerInstanceId).Returns(instanceId);
        A.CallTo(() => scheduler.Status).Returns(SchedulerStatus.Running);
        return scheduler;
    }

    private static IScheduler Proxy(string name, string? target)
    {
        IScheduler inner = A.Fake<IScheduler>();
        A.CallTo(() => inner.SchedulerName).Returns(name);
        return new TestProxy(inner, target);
    }

    private sealed class TestProxy : DelegatingScheduler, IProxyScheduler
    {
        public TestProxy(IScheduler inner, string? target) : base(inner)
        {
            Target = target;
        }

        public string? Target { get; }

        public SchedulerOrigin Origin => SchedulerOrigin.Remote;
    }
}
