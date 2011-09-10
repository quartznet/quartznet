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

using System.Runtime.Serialization;

using Common.Logging;
using NUnit.Framework;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Simpl;

namespace Quartz.Tests.Unit.Impl.AdoJobStore
{
    /// <author>Marko Lahma (.NET)</author>
    [TestFixture]
	public class StdAdoDelegateTest
	{
		[Test]
		public void TestSerializeJobData()
		{
            var args = new DelegateInitializationArgs();
		    args.Logger = LogManager.GetLogger(GetType());
		    args.TablePrefix = "QRTZ_";
		    args.InstanceName = "TESTSCHED";
		    args.InstanceId = "INSTANCE";
		    args.DbProvider = new DbProvider("SqlServer-20", "");
		    args.TypeLoadHelper = new SimpleTypeLoadHelper();
            args.ObjectSerializer = new DefaultObjectSerializer();
 
			var del = new StdAdoDelegate();
            del.Initialize(args);

			var jdm = new JobDataMap();
			del.SerializeJobData(jdm);

			jdm.Clear();
			jdm.Put("key", "value");
			jdm.Put("key2", null);
			del.SerializeJobData(jdm);

			jdm.Clear();
			jdm.Put("key1", "value");
			jdm.Put("key2", null);
			jdm.Put("key3", new NonSerializableTestClass());

            try
			{
				del.SerializeJobData(jdm);
				Assert.Fail();
			}
			catch (SerializationException e)
			{
				Assert.IsTrue(e.Message.IndexOf("key3") >= 0);
			}
		}

        class NonSerializableTestClass
        {
            
        }
	}
}