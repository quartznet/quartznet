/* 
 * Copyright 2004-2007 OpenSymphony 
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
using System.Collections;

using NUnit.Framework;

using Quartz.Collection;

namespace Quartz.Tests.Unit
{
    [TestFixture]
    public class CollectionUtilTest
    {
        [Test]
        public void TestRemoveNonExistingElement()
        {
            ArrayList items = new ArrayList();
            items.Add("bar");
            bool removed = CollectionUtil.Remove(items, "foo");

            Assert.IsFalse(removed, "Collection did loose element when it shouldn't have");
        }

        [Test]
        public void TestRemoveExistingElement()
        {
            ArrayList items = new ArrayList();
            items.Add("foo");
            bool removed = CollectionUtil.Remove(items, "foo");

            Assert.IsTrue(removed, "Collection did'nt loose element when it should have");
        }


        [Test]
        public void TestRemoveFromNullCollection()
        {
            try
            {
                CollectionUtil.Remove(null, "foo");
                Assert.Fail("Removing from null collection succeeded");
            }
            catch
            {
                // ok
                ;
            }
        }

    }

}
