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

using System;
using System.Collections.Specialized;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Simpl;

namespace Quartz.Tests.Unit.Simpl
{
    /// <summary>
    /// Unit test for SystemPropertyInstanceIdGenerator.
    /// </summary>
    [TestFixture]
    public class SystemPropertyInstanceIdGeneratorTest
    {
        [SetUp]
        public void SetUp()
        {
            Environment.SetEnvironmentVariable(SystemPropertyInstanceIdGenerator.SystemProperty, "foo");
            Environment.SetEnvironmentVariable("blah.blah", "goo");
        }

        [Test]
        public void TestGetInstanceId()
        {
            SystemPropertyInstanceIdGenerator gen = new SystemPropertyInstanceIdGenerator();

            string instId = gen.GenerateInstanceId();

            Assert.AreEqual("foo", instId);
        }

        [Test]
        public void TestGetInstanceIdWithPrepend()
        {
            SystemPropertyInstanceIdGenerator gen = new SystemPropertyInstanceIdGenerator();
            gen.Prepend = "1";

            string instId = gen.GenerateInstanceId();

            Assert.AreEqual("1foo", instId);
        }

        [Test]
        public void TestGetInstanceIdWithPostpend()
        {
            SystemPropertyInstanceIdGenerator gen = new SystemPropertyInstanceIdGenerator();
            gen.Postpend = "2";

            string instId = gen.GenerateInstanceId();

            Assert.AreEqual("foo2", instId);
        }

        [Test]
        public void TestGetInstanceIdWithPrependAndPostpend()
        {
            SystemPropertyInstanceIdGenerator gen = new SystemPropertyInstanceIdGenerator();
            gen.Prepend = "1";
            gen.Postpend = "2";

            string instId = gen.GenerateInstanceId();

            Assert.AreEqual("1foo2", instId);
        }

        [Test]
        public void TestGetInstanceIdFromCustomSystemProperty()
        {
            SystemPropertyInstanceIdGenerator gen = new SystemPropertyInstanceIdGenerator();
            gen.SystemPropertyName = "blah.blah";

            string instId = gen.GenerateInstanceId();

            Assert.AreEqual("goo", instId);
        }

        [Test]
        [Ignore("Work in progress")]
        public void TestGeneratorThroughSchedulerInstantiation()
        {
            // TODO
            //JdbcQuartzTestUtilities.createDatabase("MeSchedulerDatabase");

            NameValueCollection config = new NameValueCollection();
            config["quartz.scheduler.instanceName"] = "MeScheduler";
            config["quartz.scheduler.instanceId"] = "AUTO";
            config["quartz.scheduler.instanceIdGenerator.type"] = typeof (SystemPropertyInstanceIdGenerator).AssemblyQualifiedName;
            config["quartz.scheduler.instanceIdGenerator.prepend"] = "1";
            config["quartz.scheduler.instanceIdGenerator.postpend"] = "2";
            config["quartz.scheduler.instanceIdGenerator.systemPropertyName"] = "blah.blah";
            config["quartz.threadPool.threadCount"] = "1";
            config["quartz.threadPool.type"] = typeof(SimpleThreadPool).AssemblyQualifiedName;
            config["quartz.jobStore.type"] = typeof (JobStoreTX).AssemblyQualifiedName;
            config["quartz.jobStore.clustered"] = "true";
            config["quartz.jobStore.dataSource"] = "MeSchedulerDatabase";

            IScheduler sched = new StdSchedulerFactory(config).GetScheduler();

            Assert.AreEqual("1goo2", sched.SchedulerInstanceId);
        }
    }
}