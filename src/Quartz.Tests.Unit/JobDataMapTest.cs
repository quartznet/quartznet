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

using FluentAssertions;

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

        [Test]
        public void HandlesGuid()
        {
            var map = new JobDataMap();
            map["key"] = Guid.NewGuid();
            map.TryGetGuidValue("key", out var g).Should().BeTrue();
            g.Should().NotBe(Guid.Empty);

            map["key"] = Guid.NewGuid().ToString();
            map.TryGetGuidValue("key", out g).Should().BeTrue();
            g.Should().NotBe(Guid.Empty);

            map.TryGetNullableGuid("key-not-found", out var nullable).Should().BeTrue();
            nullable.Should().Be(null);
        }
        
        [TestCase(null, true)] //nullable string is valid
        [TestCase("string", true)]
        public void TryGetString_ParseResult(object val, bool resultOutcome)
        {
            var map = new JobDataMap
            {
                ["key"] = val
            };
            var result = map.TryGetString("key", out _);
            result.Should().Be(resultOutcome);
        }

        [TestCase(null, false)]
        [TestCase(1, true)]
        public void TryGetIntValue_ParseResult(object val, bool resultOutcome)
        {
            var map = new JobDataMap
            {
                ["key"] = val
            };
            var result = map.TryGetIntValue("key", out _);
            result.Should().Be(resultOutcome);
        }

        [TestCase(null, false)]
        [TestCase(1, true)]
        public void TryGetLongValue_ParseResult(object val, bool resultOutcome)
        {
            var map = new JobDataMap
            {
                ["key"] = val
            };
            var result = map.TryGetLongValue("key", out _);
            result.Should().Be(resultOutcome);
        }

        [TestCase(null, false)]
        [TestCase(1, true)]
        public void TryGetFloatValue_ParseResult(object val, bool resultOutcome)
        {
            var map = new JobDataMap
            {
                ["key"] = val
            };
            var result = map.TryGetFloatValue("key", out _);
            result.Should().Be(resultOutcome);
        }

        [TestCase(null, false)]
        [TestCase(1, true)]
        public void TryGetDoubleValue_ParseResult(object val, bool resultOutcome)
        {
            var map = new JobDataMap
            {
                ["key"] = val
            };
            var result = map.TryGetDoubleValue("key", out _);
            result.Should().Be(resultOutcome);
        }

        [TestCase(null, false)]
        [TestCase(true, true)]
        [TestCase(false, true)]
        public void TryGetBooleanValue_ParseResult(object val, bool resultOutcome)
        {
            var map = new JobDataMap
            {
                ["key"] = val
            };
            var result = map.TryGetBooleanValue("key", out _);
            result.Should().Be(resultOutcome);
        }

        [Test]
        public void TryGetValBoolean_NonExistentKey()
        {
            var result = new JobDataMap().TryGetBooleanValue("key", out _);
            result.Should().BeFalse();
        }

        [Test]
        public void TryGetIntValue_NonExistentKey()
        {
            var result = new JobDataMap().TryGetIntValue("key", out _);
            result.Should().BeFalse();
        }

        [Test]
        public void TryGetLongValue_NonExistentKey()
        {
            var result = new JobDataMap().TryGetLongValue("key", out _);
            result.Should().BeFalse();
        }

        [Test]
        public void TryGetFloatValue_NonExistentKey()
        {
            var result = new JobDataMap().TryGetFloatValue("key", out _);
            result.Should().BeFalse();
        }
        
        [Test]
        public void TryGetString_NonExistentKey()
        {
            var result = new JobDataMap().TryGetString("key", out _);
            result.Should().BeFalse();
        }
    }
}