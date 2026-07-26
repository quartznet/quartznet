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


using Quartz.Impl;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// Tests for StdSchedulerFactory.
/// </summary>
/// <author>Marko Lahma (.NET)</author>
[NonParallelizable]
public class StdSchedulerFactoryTest
{
    [TearDown]
    public void ClearEnvironmentOverrides()
    {
        // TestFactoryShouldOverrideConfigurationWithSysProperties sets these, and a leftover
        // instanceName would silently decide the name any later test reads back.
        Environment.SetEnvironmentVariable("quartz.scheduler.instanceName", null);
        Environment.SetEnvironmentVariable("quartz.serializer.type", null);
    }

    [Test]
    public async ValueTask TestFactoryCanBeUsedWithEmptyProperties()
    {
        var props = new NameValueCollection();
        props["quartz.serializer.type"] = TestConstants.DefaultSerializerType;
        StdSchedulerFactory factory = new StdSchedulerFactory(props);
        var result = await factory.GetScheduler();
        result.Should().NotBeNull();
    }

    [Test]
    public void TestFactoryShouldThrowConfigurationErrorIfUnknownQuartzSetting()
    {
        NameValueCollection properties = new NameValueCollection();
        properties["quartz.unknown.property"] = "1";
        Assert.Throws<SchedulerConfigException>(() => new StdSchedulerFactory(properties), "Unknown configuration property 'quartz.unknown.property'");
    }

    [Test]
    public void TestFactoryShouldThrowConfigurationErrorIfCaseErrorInQuartzSetting()
    {
        NameValueCollection properties = new NameValueCollection();
        properties["quartz.jobstore.type"] = "";
        Assert.Throws<SchedulerConfigException>(() => new StdSchedulerFactory(properties), "Unknown configuration property 'quartz.jobstore.type'");
    }

    [Test]
    public void TestFactoryShouldNotThrowConfigurationErrorIfUnknownQuartzSettingAndCheckingTurnedOff()
    {
        NameValueCollection properties = new NameValueCollection();
        properties["quartz.checkConfiguration"] = "false";
        properties["quartz.unknown.property"] = "1";
        new StdSchedulerFactory(properties);
    }

    [Test]
    public void TestFactoryShouldNotThrowConfigurationErrorIfNotQuartzPrefixedProperty()
    {
        NameValueCollection properties = new NameValueCollection();
        properties["my.unknown.property"] = "1";
        new StdSchedulerFactory(properties);
    }

    [Test]
    public async Task TestFactoryShouldOverrideConfigurationWithSysProperties()
    {
        NameValueCollection properties = new NameValueCollection();
        properties["quartz.serializer.type"] = TestConstants.DefaultSerializerType;
        var factory = new StdSchedulerFactory(properties);

        factory.Initialize();
        var scheduler = await factory.GetScheduler();
        Assert.That(scheduler.SchedulerName, Is.EqualTo("QuartzScheduler"));

        Environment.SetEnvironmentVariable("quartz.scheduler.instanceName", "fromSystemProperties");
        // Make sure to pass the serializer type as an env var instead of in a NameValueCollection (as in the previous test)
        // since passing an explicit NameValueCollection causes the scheduler factory to not check environment variables
        Environment.SetEnvironmentVariable("quartz.serializer.type", TestConstants.DefaultSerializerType);
        factory = new StdSchedulerFactory();
        scheduler = await factory.GetScheduler();
        Assert.That(scheduler.SchedulerName, Is.EqualTo("fromSystemProperties"));
    }

    /// <summary>
    /// The embedded <c>quartz.config</c> is gone, but the defaults it supplied to a factory given no
    /// properties of its own are not: they are seeded by <c>Initialize()</c> instead. A factory handed
    /// properties never read that file, and still falls back to the typed options.
    /// </summary>
    [Test]
    public async Task AFactoryGivenNoPropertiesKeepsTheDefaultsTheEmbeddedConfigSupplied()
    {
        var scheduler = await new StdSchedulerFactory().GetScheduler();
        try
        {
            var metaData = await scheduler.GetMetaData();
            var jobStore = (RAMJobStore) ((StdScheduler) scheduler).scheduler.resources.JobStore;

            scheduler.SchedulerName.Should().Be("DefaultQuartzScheduler");
            metaData.ThreadPoolSize.Should().Be(10);
            jobStore.MisfireThreshold.Should().Be(TimeSpan.FromSeconds(60));
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Test]
    public void ShouldAllowInheritingStdSchedulerFactory()
    {
        // check that property names are validated through inheritance hierarchy
        NameValueCollection collection = new NameValueCollection();
        collection["quartz.scheduler.idleWaitTime"] = "123";
        collection["quartz.scheduler.test"] = "foo";
        StdSchedulerFactory factory = new TestStdSchedulerFactory(collection);
    }

    [Test]
    public async Task ShouldBeAbleToDefineThreadPriority()
    {
        var properties = new NameValueCollection
        {
            ["quartz.threadPool.type"] = "Quartz.Impl.DefaultThreadPool, Quartz",
            ["quartz.threadPool.threadCount"] = "3"
        };

        ISchedulerFactory schedulerFactory = new StdSchedulerFactory(properties);

        await schedulerFactory.GetScheduler();
    }

    [Test]
    public void IdleWaitTime_Zero_ShouldThrow()
    {
        var properties = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = "IdleWaitTimeZeroTest",
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
            ["quartz.scheduler.idleWaitTime"] = "0"
        };

        var factory = new StdSchedulerFactory(properties);
        Assert.ThrowsAsync<SchedulerConfigException>(async () => await factory.GetScheduler());
    }

    [Test]
    public void IdleWaitTime_Negative_ShouldThrow()
    {
        var properties = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = "IdleWaitTimeNegativeTest",
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
            ["quartz.scheduler.idleWaitTime"] = "-1000"
        };

        var factory = new StdSchedulerFactory(properties);
        Assert.ThrowsAsync<SchedulerConfigException>(async () => await factory.GetScheduler());
    }

    [Test]
    public void IdleWaitTime_LessThan1000ms_ShouldThrow()
    {
        var properties = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = "IdleWaitTimeLowTest",
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
            ["quartz.scheduler.idleWaitTime"] = "500"
        };

        var factory = new StdSchedulerFactory(properties);
        Assert.ThrowsAsync<SchedulerConfigException>(async () => await factory.GetScheduler());
    }

    [Test]
    public async Task ShouldCreateSchedulerWhenLookedUpByItsConfiguredName()
    {
        const string SchedulerName = "NamedLookupCreatesScheduler";
        using var factory = new StdSchedulerFactory(PropertiesForScheduler(SchedulerName));

        // no GetScheduler() call first, which used to be the only way to get a non-null result here
        var scheduler = await factory.GetScheduler(SchedulerName);

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
        using var factory = new StdSchedulerFactory(PropertiesForScheduler(SchedulerName));

        var defaultScheduler = await factory.GetScheduler();

        try
        {
            var namedScheduler = await factory.GetScheduler(SchedulerName);
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
        using var factory = new StdSchedulerFactory(PropertiesForScheduler(SchedulerName));

        var scheduler = await factory.GetScheduler(SchedulerName.ToLowerInvariant());

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
        using var factory = new StdSchedulerFactory(PropertiesForScheduler(SchedulerName));

        var scheduler = await factory.GetScheduler("SchedulerThisFactoryDoesNotProduce");

        scheduler.Should().BeNull();
        (await factory.GetAllSchedulers()).Should().BeEmpty("asking for another name must not create this factory's own scheduler");
    }

    private static NameValueCollection PropertiesForScheduler(string schedulerName)
    {
        return new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = schedulerName,
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType
        };
    }

    /// <summary>
    /// A factory owns its scheduler repository, so a fresh factory per call would find no existing
    /// scheduler and build a second live one carrying the same instance name and instance id.
    /// </summary>
    [Test]
    public async Task GetDefaultSchedulerReturnsTheSameSchedulerEveryTime()
    {
        var first = await StdSchedulerFactory.GetDefaultScheduler();
        var second = await StdSchedulerFactory.GetDefaultScheduler();

        second.Should().BeSameAs(first, "two schedulers sharing one instance id would both check in as that node");
    }

    /// <summary>
    /// Querying a disposed factory used to build a whole new container, hang it off the disposed factory
    /// where nothing would dispose it, and report an empty repository as though the schedulers had gone.
    /// </summary>
    [Test]
    public async Task ADisposedFactoryDoesNotBuildAnotherContainer()
    {
        var factory = new StdSchedulerFactory();
        await factory.GetScheduler();
        factory.Dispose();

        var query = async () => await factory.GetAllSchedulers();

        await query.Should().ThrowAsync<ObjectDisposedException>();
    }

    private class TestStdSchedulerFactory : StdSchedulerFactory
    {
        public const string PropertyTest = "quartz.scheduler.test";

        public TestStdSchedulerFactory(NameValueCollection nameValueCollection) : base(nameValueCollection)
        {
        }

        protected override bool IsSupportedConfigurationKey(string configurationKey)
        {
            return configurationKey == PropertyTest || base.IsSupportedConfigurationKey(configurationKey);
        }
    }
}