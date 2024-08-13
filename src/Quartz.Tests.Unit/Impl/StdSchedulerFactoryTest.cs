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

using FluentAssertions;

using Quartz.Impl;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// Tests for StdSchedulerFactory.
/// </summary>
/// <author>Marko Lahma (.NET)</author>
[TestFixture]
public class StdSchedulerFactoryTest
{
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
    public async Task TestFactoryShouldLoadPropertiesFromFileWhosePathIsGivenByEnvVariable()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            const string InstanceName = "TestInstance";

            File.WriteAllText(tempFile, $"{StdSchedulerFactory.PropertySchedulerInstanceName}={InstanceName}");

            Environment.SetEnvironmentVariable(StdSchedulerFactory.PropertiesFile, tempFile);

            var factory = new StdSchedulerFactory();
            factory.Initialize(); // <- optional, because `GetScheduler` does it anyway
            var scheduler = await factory.GetScheduler();

            Assert.That(scheduler.SchedulerName, Is.EqualTo(InstanceName));
        }
        finally
        {
            // clean up of temp file and env var
            try
            {
                File.Delete(tempFile);
            }
            catch (Exception)
            {
                // ignore temp file delete error
            }

            Environment.SetEnvironmentVariable(StdSchedulerFactory.PropertiesFile, null);
        }
    }

    [Test]
    public async Task ShouldBeAbleToDefineThreadPriority()
    {
        var properties = new NameValueCollection
        {
            ["quartz.threadPool.type"] = "Quartz.Simpl.DefaultThreadPool, Quartz",
            ["quartz.threadPool.threadCount"] = "3"
        };

        ISchedulerFactory schedulerFactory = new StdSchedulerFactory(properties);

        await schedulerFactory.GetScheduler();
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