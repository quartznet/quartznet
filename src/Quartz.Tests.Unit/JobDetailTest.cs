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

using NUnit.Framework;

using Quartz.Job;

namespace Quartz.Tests.Unit
{
    /// <author>Marko Lahma (.NET)</author>
	[TestFixture]
	public class JobDetailTest
	{
       
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
			JobDetail clonedJobDetail = (JobDetail)jobDetail.Clone();

            Assert.AreEqual(jobDetail, clonedJobDetail);
		}
	}
}