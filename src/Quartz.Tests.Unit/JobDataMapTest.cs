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

namespace Quartz.Tests.Unit
{
	/// <summary>
	/// Unit test for JobDataMap serialization backwards compatibility.
	/// </summary>
    /// <author>Marko Lahma (.NET)</author>
    public class JobDataMapTest : SerializationTestSupport
	{
		private static readonly string[] Versions = new string[] {"0.6.0"};

		/// <summary>
		/// Get the object to serialize when generating serialized file for future
		/// tests, and against which to validate deserialized object.
		/// </summary>
		/// <returns></returns>
		protected override object GetTargetObject()
		{
			JobDataMap m = new JobDataMap();
			m.Put("key", 5);
			return m;
		}

		/// <summary>
		/// Get the Quartz versions for which we should verify
		/// serialization backwards compatibility.
		/// </summary>
		/// <returns></returns>
		protected override string[] GetVersions()
		{
			return Versions;
		}


		/// <summary>
		/// Verify that the target object and the object we just deserialized 
		/// match.
		/// </summary>
		/// <param name="target"></param>
		/// <param name="deserialized"></param>
		protected override void VerifyMatch(object target, object deserialized)
		{
			JobDataMap targetMap = (JobDataMap) target;
			JobDataMap deserializedMap = (JobDataMap) deserialized;

			Assert.IsNotNull(deserializedMap);
			Assert.AreEqual(targetMap.WrappedMap, deserializedMap.WrappedMap);
			Assert.AreEqual(targetMap.Dirty, deserializedMap.Dirty);
		}
	}
}