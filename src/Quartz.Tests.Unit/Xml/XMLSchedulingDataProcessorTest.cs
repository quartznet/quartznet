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

using System.IO;

using NUnit.Framework;

using Quartz.Simpl;
using Quartz.Xml;

using Rhino.Mocks;

namespace Quartz.Tests.Unit.Xml
{
    /// <summary>
    /// Tests for <see cref="XMLSchedulingDataProcessor" />.
    /// </summary>
    /// <author>Marko Lahma (.NET)</author>
    [TestFixture]
    public class XMLSchedulingDataProcessorTest
    {
        private XMLSchedulingDataProcessor processor;
        private IScheduler mockScheduler;

        [SetUp]
        public void SetUp()
        {
            processor = new XMLSchedulingDataProcessor(new SimpleTypeLoadHelper());
            mockScheduler = MockRepository.GenerateMock<IScheduler>();
        }

        [Test]
        public void TestScheduling_RichConfiguration()
        {
            Stream s = ReadJobXmlFromEmbeddedResource("RichConfiguration_20.xml");
            processor.ProcessStream(s, null);
            Assert.IsFalse(processor.OverWriteExistingData);
            Assert.IsTrue(processor.IgnoreDuplicates);


            processor.ScheduleJobs(mockScheduler);

            mockScheduler.AssertWasCalled(x => x.ScheduleJob(Arg<ITrigger>.Is.NotNull), options => options.Repeat.Twice());
        }


        [Test]
        public void TestScheduling_MinimalConfiguration()
        {
            Stream s = ReadJobXmlFromEmbeddedResource("MinimalConfiguration_20.xml");
            processor.ProcessStream(s, null);
            Assert.IsFalse(processor.OverWriteExistingData);

            processor.ScheduleJobs(mockScheduler);
        }

        private static Stream ReadJobXmlFromEmbeddedResource(string resourceName)
        {
            string fullName = "Quartz.Tests.Unit.Xml.TestData." + resourceName;
            return new StreamReader(typeof(XMLSchedulingDataProcessorTest).Assembly.GetManifestResourceStream(fullName)).BaseStream;
        }
    }
}