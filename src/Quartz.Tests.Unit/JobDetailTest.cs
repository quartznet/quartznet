/* 
 * Copyright 2004-2006 OpenSymphony 
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
 */
using System;
using System.Collections;

using NUnit.Framework;

using Quartz.Collection;
using Quartz.Job;

namespace Quartz.Tests.Unit
{
	/**
	 * Unit test for JobDetail.
	 */
	[TestFixture]
	public class JobDetailTest
	{
		[Test]
		public void TestAddJobListener() 
		{
			string[] listenerNames = new string[] {"X", "A", "B"};
        
			// Verify that a HashSet shuffles order, so we know that order test
			// below is actually testing something
			HashSet hashSet = new HashSet(listenerNames);
			Assert.IsFalse(new ArrayList(listenerNames).Equals(new ArrayList(hashSet)));
        
			JobDetail jobDetail = new JobDetail();
			for (int i = 0; i < listenerNames.Length; i++) 
			{
				jobDetail.AddJobListener(listenerNames[i]);
			}

			// Make sure order was maintained
			TestUtil.AssertCollectionEquality(new ArrayList(listenerNames), new ArrayList(jobDetail.JobListenerNames));
        
			// Make sure uniqueness is enforced
			for (int i = 0; i < listenerNames.Length; i++) 
			{
				try 
				{
					jobDetail.AddJobListener(listenerNames[i]);
					Assert.Fail();
				} 
				catch (ArgumentException) 
				{
				}
			}
		}

        [Test]
        public void TestRemoveJobListener()
        {
            string[] listenerNames = new string[] { "X", "A", "B" };

            JobDetail jobDetail = new JobDetail();
            for (int i = 0; i < listenerNames.Length; i++)
            {
                jobDetail.AddJobListener(listenerNames[i]);
            }

            // Make sure uniqueness is enforced
            for (int i = 0; i < listenerNames.Length; i++)
            {
                Assert.IsTrue(jobDetail.RemoveJobListener(listenerNames[i]));
            }
        }


        [Test]
        public void TestEquals()
        {
            JobDetail jd1 = new JobDetail("name", "group", typeof(NoOpJob));
            JobDetail jd2 = new JobDetail("name", "group", typeof(NoOpJob));
            JobDetail jd3 = new JobDetail("namediff", "groupdiff", typeof(NoOpJob));
            Assert.AreEqual(jd1, jd2);
            Assert.AreNotEqual(jd1, jd3);
            Assert.AreNotEqual(jd2, jd3);
            Assert.AreNotEqual(jd1, null);
        }
    
		[Test]
		public void TestClone() 
		{
			JobDetail jobDetail = new JobDetail();
			jobDetail.AddJobListener("A");

            // verify order
            for (int i = 0; i < 10; i++)
            {
                jobDetail.AddJobListener("A" + i);
            }

			JobDetail clonedJobDetail = (JobDetail)jobDetail.Clone();
			TestUtil.AssertCollectionEquality(new ArrayList(clonedJobDetail.JobListenerNames),
				new ArrayList(jobDetail.JobListenerNames));
        
			jobDetail.AddJobListener("B");
        
			// Verify deep clone of jobListenerNames 
			Assert.IsTrue(new ArrayList(jobDetail.JobListenerNames).Contains("A"));
			Assert.IsTrue(new ArrayList(jobDetail.JobListenerNames).Contains("B"));
			Assert.IsTrue(new ArrayList(clonedJobDetail.JobListenerNames).Contains("A"));
			Assert.IsFalse(new ArrayList(clonedJobDetail.JobListenerNames).Contains("B"));
		}


	}
}