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

using System.Net;

using NUnit.Framework;

using Quartz.Simpl;

namespace Quartz.Tests.Unit.Simpl
{
    [TestFixture]
    public class SimpleInstanceIdGeneratorTest
    {
        private SimpleInstanceIdGenerator generator;

        [SetUp]
        public void SetUp()
        {
            generator = new TestInstanceIdGenerator();
        }

        [Test]
        public void IdShouldNotExceed50Chars()
        {
            string instanceId = generator.GenerateInstanceId();
            Assert.That(instanceId.Length, Is.LessThanOrEqualTo(50));
        }


        private class TestInstanceIdGenerator : SimpleInstanceIdGenerator
        {
            protected override IPHostEntry GetHostAddress()
            {
                return new IPHostEntry
                           {
                               HostName = "my-windows-machine-with-long-name.at.azurewebsites.net"
                           };
            }
        }
    }
}