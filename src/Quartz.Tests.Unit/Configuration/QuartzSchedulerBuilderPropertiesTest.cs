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

using System.Collections.Specialized;

using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// Tests for configuring a standalone scheduler from flat <c>quartz.*</c> properties, which is what
/// <see cref="QuartzSchedulerBuilder.UseProperties"/> does and what the properties-based factory used
/// to do.
/// </summary>
/// <author>Marko Lahma (.NET)</author>
[NonParallelizable]
public class QuartzSchedulerBuilderPropertiesTest
{
    [Test]
    public async Task CanBeUsedWithEmptyProperties()
    {
        NameValueCollection properties = new NameValueCollection
        {
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType
        };

        using StandaloneSchedulerFactory factory = QuartzSchedulerBuilder.Create().UseProperties(properties).Build();

        IScheduler scheduler = await factory.GetScheduler();

        try
        {
            scheduler.Should().NotBeNull();
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Test]
    public void ShouldThrowConfigurationErrorIfUnknownQuartzSetting()
    {
        NameValueCollection properties = new NameValueCollection();
        properties["quartz.unknown.property"] = "1";

        Action act = () => QuartzSchedulerBuilder.Create().UseProperties(properties);

        act.Should().Throw<SchedulerConfigException>().WithMessage("*quartz.unknown.property*");
    }

    [Test]
    public void ShouldThrowConfigurationErrorIfCaseErrorInQuartzSetting()
    {
        NameValueCollection properties = new NameValueCollection();
        properties["quartz.jobstore.type"] = "";

        Action act = () => QuartzSchedulerBuilder.Create().UseProperties(properties);

        act.Should().Throw<SchedulerConfigException>().WithMessage("*quartz.jobstore.type*");
    }

    [Test]
    public void ShouldRejectRemovedLockHandlerIdentityKeysWithAdvice()
    {
        NameValueCollection properties = new NameValueCollection();
        properties["quartz.jobStore.lockHandler.tablePrefix"] = "MYAPP_QRTZ_";

        Action act = () => QuartzSchedulerBuilder.Create().UseProperties(properties);

        act.Should().Throw<SchedulerConfigException>(
                "the key configured a real thing in 3.x, so the error must say what replaced it rather than reading like a typo")
            .WithMessage("*quartz.jobStore.lockHandler.tablePrefix*ISemaphore.Initialize*");

        properties = new NameValueCollection();
        properties["quartz.jobStore.lockHandler.schedulerName"] = "MyScheduler";

        act = () => QuartzSchedulerBuilder.Create().UseProperties(properties);

        act.Should().Throw<SchedulerConfigException>()
            .WithMessage("*quartz.jobStore.lockHandler.schedulerName*ISemaphore.Initialize*");
    }

    [Test]
    public void ShouldRejectTheDeadSchedulerThreadKeysAndSayWhyTheyDied()
    {
        NameValueCollection properties = new NameValueCollection();
        properties["quartz.scheduler.threadName"] = "my-thread";

        Action act = () => QuartzSchedulerBuilder.Create().UseProperties(properties);

        act.Should().Throw<SchedulerConfigException>(
                "the key set something real in 3.x, so the error has to say the loop is a Task rather than read like a typo")
            .WithMessage("*quartz.scheduler.threadName*Task*");

        properties = new NameValueCollection();
        properties["quartz.scheduler.makeSchedulerThreadDaemon"] = "true";

        act = () => QuartzSchedulerBuilder.Create().UseProperties(properties);

        act.Should().Throw<SchedulerConfigException>()
            .WithMessage("*quartz.scheduler.makeSchedulerThreadDaemon*quartz.jobStore.makeThreadsDaemons*");
    }

    [Test]
    public void ShouldCheckKeysHandedToAddQuartzTheSameWayAsTheStandaloneBuilder()
    {
        NameValueCollection properties = new NameValueCollection();
        properties["quartz.jobstore.type"] = "";

        Action act = () => new ServiceCollection().AddQuartz(properties);

        act.Should().Throw<SchedulerConfigException>(
                "a NameValueCollection is written by the caller, so a misspelling in it is a mistake wherever it is handed in")
            .WithMessage("*quartz.jobstore.type*");

        act = () => new ServiceCollection().AddQuartz("reporting", properties);

        act.Should().Throw<SchedulerConfigException>().WithMessage("*quartz.jobstore.type*");

        properties["quartz.checkConfiguration"] = "false";

        act = () => new ServiceCollection().AddQuartz(properties);
        act.Should().NotThrow("the escape hatch is the same one the standalone builder honours");
    }

    [Test]
    public void ShouldNotThrowConfigurationErrorIfUnknownQuartzSettingAndCheckingTurnedOff()
    {
        NameValueCollection properties = new NameValueCollection();
        properties["quartz.checkConfiguration"] = "false";
        properties["quartz.unknown.property"] = "1";

        Action act = () => QuartzSchedulerBuilder.Create().UseProperties(properties);

        act.Should().NotThrow();
    }

    [Test]
    public void ShouldNotThrowConfigurationErrorIfNotQuartzPrefixedProperty()
    {
        NameValueCollection properties = new NameValueCollection();
        properties["my.unknown.property"] = "1";

        Action act = () => QuartzSchedulerBuilder.Create().UseProperties(properties);

        act.Should().NotThrow();
    }

    [Test]
    public async Task ShouldBeAbleToDefineThreadPriority()
    {
        NameValueCollection properties = new NameValueCollection
        {
            ["quartz.threadPool.type"] = "Quartz.Impl.DefaultThreadPool, Quartz",
            ["quartz.threadPool.threadCount"] = "3"
        };

        using StandaloneSchedulerFactory factory = QuartzSchedulerBuilder.Create().UseProperties(properties).Build();

        IScheduler scheduler = await factory.GetScheduler();
        await scheduler.Shutdown();
    }

    [Test]
    public async Task IdleWaitTime_Zero_ShouldThrow()
    {
        NameValueCollection properties = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = "IdleWaitTimeZeroTest",
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
            ["quartz.scheduler.idleWaitTime"] = "0"
        };

        await AssertSchedulerConfigurationRejected(properties);
    }

    [Test]
    public async Task IdleWaitTime_Negative_ShouldThrow()
    {
        NameValueCollection properties = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = "IdleWaitTimeNegativeTest",
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
            ["quartz.scheduler.idleWaitTime"] = "-1000"
        };

        await AssertSchedulerConfigurationRejected(properties);
    }

    [Test]
    public async Task IdleWaitTime_LessThan1000ms_ShouldThrow()
    {
        NameValueCollection properties = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = "IdleWaitTimeLowTest",
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
            ["quartz.scheduler.idleWaitTime"] = "500"
        };

        await AssertSchedulerConfigurationRejected(properties);
    }

    [Test]
    public async Task ShouldCreateSchedulerWhenLookedUpByItsConfiguredName()
    {
        const string SchedulerName = "NamedLookupCreatesScheduler";
        using StandaloneSchedulerFactory factory = QuartzSchedulerBuilder.Create().UseProperties(PropertiesForScheduler(SchedulerName)).Build();

        // no GetScheduler() call first, which used to be the only way to get a non-null result here
        IScheduler scheduler = await factory.LookupScheduler(SchedulerName);

        try
        {
            scheduler.Should().NotBeNull();
            scheduler.SchedulerName.Should().Be(SchedulerName);
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Test]
    public async Task ShouldReturnSameSchedulerForNamedAndDefaultLookup()
    {
        const string SchedulerName = "NamedLookupReturnsSameScheduler";
        using StandaloneSchedulerFactory factory = QuartzSchedulerBuilder.Create().UseProperties(PropertiesForScheduler(SchedulerName)).Build();

        IScheduler defaultScheduler = await factory.GetScheduler();

        try
        {
            IScheduler namedScheduler = await factory.LookupScheduler(SchedulerName);
            namedScheduler.Should().BeSameAs(defaultScheduler);
        }
        finally
        {
            await defaultScheduler.Shutdown();
        }
    }

    [Test]
    public async Task ShouldMatchConfiguredSchedulerNameCaseInsensitively()
    {
        const string SchedulerName = "NamedLookupIgnoresCase";
        using StandaloneSchedulerFactory factory = QuartzSchedulerBuilder.Create().UseProperties(PropertiesForScheduler(SchedulerName)).Build();

        IScheduler scheduler = await factory.LookupScheduler(SchedulerName.ToLowerInvariant());

        try
        {
            scheduler.Should().NotBeNull("the repository indexes names case-insensitively, so creating by name should too");
            scheduler.SchedulerName.Should().Be(SchedulerName);
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Test]
    public async Task ShouldNotCreateSchedulerWhenLookedUpByAnotherName()
    {
        const string SchedulerName = "NamedLookupOtherName";
        using StandaloneSchedulerFactory factory = QuartzSchedulerBuilder.Create().UseProperties(PropertiesForScheduler(SchedulerName)).Build();

        IScheduler scheduler = await factory.LookupScheduler("SchedulerThisFactoryDoesNotProduce");

        scheduler.Should().BeNull();
        (await factory.GetAllSchedulers()).Should().BeEmpty("asking for another name must not create this factory's own scheduler");
    }

    private static async Task AssertSchedulerConfigurationRejected(NameValueCollection properties)
    {
        using StandaloneSchedulerFactory factory = QuartzSchedulerBuilder.Create().UseProperties(properties).Build();

        Func<Task<IScheduler>> act = async () => await factory.GetScheduler();

        await act.Should().ThrowAsync<SchedulerConfigException>();
    }

    private static NameValueCollection PropertiesForScheduler(string schedulerName)
    {
        return new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = schedulerName,
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType
        };
    }
}
