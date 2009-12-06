#region License
/* 
 * Copyright 2001-2009 Terracotta, Inc. 
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
using System.Collections.Generic;

using NUnit.Framework;

using Quartz.Job;
using Quartz.Xml;

namespace Quartz.Tests.Unit.Xml
{
    /// <summary>
    /// Tests for JobSchedulingBundle.
    /// </summary>
    /// <author>Marko Lahma (.NET)</author>
    [TestFixture]
    public class JobSchedulingBundleTest
    {
        private JobSchedulingBundle bundle;

        [SetUp]
        public void SetUp()
        {
            bundle = new JobSchedulingBundle();
        }
        
        [Test]
        public void TestValid()
        {
            Assert.IsFalse(bundle.Valid);
            bundle.JobDetail = new JobDetail("foo", "bar", typeof(NoOpJob));
            Assert.IsTrue(bundle.Valid);
        }


        [Test]
        public void TestName_NoJobDetail()
        {
            Assert.IsNull(bundle.Name);
        }

        [Test]
        public void TestName_JobDetailSet()
        {
            const string name = "TEST_NAME";
            JobDetail jd = new JobDetail(name, "group", typeof(NoOpJob));
            bundle.JobDetail = jd;
            Assert.AreEqual(name, bundle.Name);
        }

        [Test]
        public void TestFullName_NoJobDetail()
        {
            Assert.IsNull(bundle.FullName);
        }

        [Test]
        public void TestFullName_JobDetailSet()
        {
            const string name = "TEST_FULL_NAME";
            JobDetail jd = new JobDetail(name, "group", typeof(NoOpJob));
            bundle.JobDetail = jd;
            Assert.AreEqual(jd.FullName, bundle.FullName);
        }

        [Test]
        public void TestAddTriggerAddsTrigger()
        {
            SimpleTrigger st = new SimpleTrigger("foo", "bar", DateTime.MinValue);
            bundle.AddTrigger(st);
            Assert.AreEqual(1, bundle.Triggers.Count);
        }

        [Test]
        public void TestSetTriggersAddsTrigger()
        {
            SimpleTrigger st = new SimpleTrigger("foo", "bar", DateTime.MinValue);
            List<Trigger> triggers = new List<Trigger>();
            triggers.Add(st);
            bundle.Triggers = triggers;
            Assert.AreSame(triggers, bundle.Triggers);
            Assert.AreEqual(1, bundle.Triggers.Count);
        }

        [Test]
        public void TestRemoveTriggerRemovesTrigger()
        {
            SimpleTrigger st = new SimpleTrigger("foo", "bar", DateTime.MinValue);
            bundle.AddTrigger(st);
            Assert.AreEqual(1, bundle.Triggers.Count);
            bundle.RemoveTrigger(st);
            Assert.AreEqual(0, bundle.Triggers.Count);
        }

        [Test]
        public void TestAddTriggerSetsStartTime()
        {
            SimpleTrigger st = new SimpleTrigger("foo", "bar", DateTime.MinValue);
            bundle.AddTrigger(st);
            Assert.AreNotEqual(DateTime.MinValue, st.StartTimeUtc);
        }

        [Test]
        public void TestAddTriggerTimeZoneForCronTriggerNotNull()
        {
            CronTrigger ct = new CronTrigger("foo", "bar");
            bundle.AddTrigger(ct);
            Assert.IsNotNull(ct.TimeZone);
        }

    }
}
