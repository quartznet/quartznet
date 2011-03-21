#region License

/* 
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved. 
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

using NUnit.Framework;

using Quartz.Impl;

using System;

namespace Quartz.Tests.Unit.Impl
{
    /// <summary>
    /// Tests for StdSchedulerFactory.
    /// </summary>
    /// <author>Marko Lahma (.NET)</author>
    [TestFixture]
    public class StdSchedulerFactoryTest
    {
        [Test]
        public void TestFactoryCanBeUsedWithNoProperties()
        {
            StdSchedulerFactory factory = new StdSchedulerFactory();
            factory.GetScheduler();
        }

        [Test]
        public void TestFactoryCanBeUsedWithEmptyProperties()
        {
            StdSchedulerFactory factory = new StdSchedulerFactory(new NameValueCollection());
            factory.GetScheduler();
        }

        [Test]
        [ExpectedException(
            ExpectedException = typeof (SchedulerConfigException),
            ExpectedMessage = "Unknown configuration property 'quartz.unknown.property'")]
        public void TestFactoryShouldThrowConfigurationErrorIfUnknownQuartzSetting()
        {
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.unknown.property"] = "1";
            new StdSchedulerFactory(properties);
        }

        [Test]
        [ExpectedException(
            ExpectedException = typeof (SchedulerConfigException),
            ExpectedMessage = "Unknown configuration property 'quartz.jobstore.type'")]
        public void TestFactoryShouldThrowConfigurationErrorIfCaseErrorInQuartzSetting()
        {
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.jobstore.type"] = "";
            new StdSchedulerFactory(properties);
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
        public void TestFactoryShouldOverrideConfigurationWithSysProperties()
        {
            NameValueCollection properties = new NameValueCollection();
            var factory = new StdSchedulerFactory();
            factory.Initialize();
            var scheduler = factory.GetScheduler();
            Assert.AreEqual("DefaultQuartzScheduler", scheduler.SchedulerName);

            Environment.SetEnvironmentVariable("quartz.scheduler.instanceName", "fromSystemProperties");
            factory = new StdSchedulerFactory();
            scheduler = factory.GetScheduler();
            Assert.AreEqual("fromSystemProperties", scheduler.SchedulerName);
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

        private class TestStdSchedulerFactory : StdSchedulerFactory
        {
            public const string PropertyTest = "quartz.scheduler.test";

            public TestStdSchedulerFactory(NameValueCollection nameValueCollection) : base(nameValueCollection)
            {
            }
        }
    }
}