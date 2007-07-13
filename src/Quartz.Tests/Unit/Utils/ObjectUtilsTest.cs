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

using NUnit.Framework;

using Quartz.Util;

namespace Quartz.Tests.Unit.Utils
{
    [TestFixture]
    public class ObjectUtilsTest
    {
        [Test]
        public void TestNullObjectForValueType()
        {
            try
            {
                ObjectUtils.ConvertValueIfNecessary(typeof (int), null);
                Assert.Fail("Accepted null");
            }
            catch
            {
                // ok
            }
        }

        [Test]
        public void TestNotConvertableData()
        {
            try
            {
                ObjectUtils.ConvertValueIfNecessary(typeof(int), new DirtyFlagMap());
                Assert.Fail("Accepted null");
            }
            catch
            {
                // ok
            }
        }

    }
}
