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

using System;

using NUnit.Framework;

using Quartz.Simpl;

namespace Quartz.Tests.Unit
{
    /// <summary>
    /// Unit test for JobDataMap serialization backwards compatibility.
    /// </summary>
    /// <author>Marko Lahma (.NET)</author>
    [TestFixture(typeof(BinaryObjectSerializer))]
    [TestFixture(typeof(JsonObjectSerializer))]
    public class JobDataMapTest : SerializationTestSupport<JobDataMap>
    {
        public JobDataMapTest(Type serializerType) : base(serializerType)
        {
        }

        /// <summary>
        /// Get the object to serialize when generating serialized file for future
        /// tests, and against which to validate deserialized object.
        /// </summary>
        /// <returns></returns>
        protected override JobDataMap GetTargetObject()
        {
            JobDataMap m = new JobDataMap();
            m.Put("key", 5);
            return m;
        }

        protected override void VerifyMatch(JobDataMap original, JobDataMap deserialized)
        {
            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized.WrappedMap, Is.EquivalentTo(original.WrappedMap));
            if (serializer is JsonObjectSerializer)
            {
                Assert.That(deserialized.Dirty, Is.False, "should not be dirty when returning from serialization");
            }
        }
    }
}