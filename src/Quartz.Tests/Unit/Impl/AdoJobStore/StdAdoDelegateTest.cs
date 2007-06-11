/**
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

using System.Runtime.Serialization;

using Common.Logging;
using NUnit.Framework;
using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore
{
	[TestFixture]
	public class StdAdoDelegateTest
	{
		[Test]
		public void TestSerializeJobData()
		{
			StdAdoDelegate del = new StdAdoDelegate(LogManager.GetLogger(GetType()), "QRTZ_", "INSTANCE");

			JobDataMap jdm = new JobDataMap();
			del.serializeJobData(jdm).close();

			jdm.Clear();
			jdm.Put("key", "value");
			jdm.Put("key2", null);
			del.serializeJobData(jdm).close();

			jdm.Clear();
			jdm.Put("key1", "value");
			jdm.Put("key2", null);
			jdm.Put("key3", new object());
			try
			{
				del.serializeJobData(jdm);
				Assert.Fail();
			}
			catch (SerializationException e)
			{
				Assert.IsTrue(e.Message.IndexOf("key3") >= 0);
			}
		}
	}
}