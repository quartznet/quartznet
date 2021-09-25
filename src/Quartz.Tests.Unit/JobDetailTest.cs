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

using System.Threading.Tasks;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Job;
using Quartz.Simpl;
using Quartz.Util;

#if REMOTING
using Quartz.Tests.Unit.Utils;
#endif

namespace Quartz.Tests.Unit
{
    /// <author>Marko Lahma (.NET)</author>
    [TestFixture]
    public class JobDetailTest
    {
        [Test]
        public void TestEquals()
        {
            JobDetailImpl jd1 = new JobDetailImpl("name", "group", typeof (NoOpJob));
            JobDetailImpl jd2 = new JobDetailImpl("name", "group", typeof (NoOpJob));
            JobDetailImpl jd3 = new JobDetailImpl("namediff", "groupdiff", typeof (NoOpJob));
            Assert.AreEqual(jd1, jd2);
            Assert.AreNotEqual(jd1, jd3);
            Assert.AreNotEqual(jd2, jd3);
            Assert.AreNotEqual(jd1, null);
        }

        [Test]
        public void TestClone()
        {
            JobDetailImpl jobDetail = new JobDetailImpl();
            JobDetailImpl clonedJobDetail = (JobDetailImpl) jobDetail.Clone();

            Assert.AreEqual(jobDetail, clonedJobDetail);
        }

#if REMOTING
        [Test]
        public void JobDetailsShouldBeSerializable()
        {
            JobDetailImpl original = new JobDetailImpl("name", "group", typeof (NoOpJob));

            JobDetailImpl cloned = original.DeepClone();

            Assert.That(cloned.Name, Is.EqualTo(original.Name));
            Assert.That(cloned.Group, Is.EqualTo(original.Group));
            Assert.That(cloned.Key, Is.EqualTo(original.Key));
        }
#endif

        [Test]
        public void SettingKeyShouldAlsoSetNameAndGroup()
        {
            JobDetailImpl detail = new JobDetailImpl();
            detail.Key = new JobKey("name", "group");

            Assert.That(detail.Name, Is.EqualTo("name"));
            Assert.That(detail.Group, Is.EqualTo("group"));
        }

        [Test]
        public void GenericJobTypeShouldBeLoadable()
        {
            var type = typeof(GenericJob<IJobSubType>);
            var typeString = type.AssemblyQualifiedNameWithoutVersion();
            var loadedType = new SimpleTypeLoadHelper().LoadType(typeString);

            Assert.That(typeString, Is.Not.Contains(", Version="));

            Assert.That(loadedType, Is.Not.Null);
            Assert.That(loadedType, Is.EqualTo(type));
        }

        public class GenericJob<T> : IJob
        {
            public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
        }

        public interface IJobSubType { }
    }
}