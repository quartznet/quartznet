#region License
/* 
 * Copyright 2001-2009 Terracotta, Inc. 
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

using Quartz.Util;

namespace Quartz.Tests.Unit.Utils
{
    /// <summary>
    /// Unit test for the Pair class.
    /// </summary>
    /// <author>Marko Lahma (.NET)</author>
    [TestFixture]
    public class PairTest
    {
        [Test]
        public void TestPair()
        {
            Pair p = new Pair();
            Assert.IsNull(p.First);
            Assert.IsNull(p.Second);
            p.First = "one";
            p.Second = "two";
            Assert.AreEqual("one", p.First);
            Assert.AreEqual("two", p.Second);

            Pair p2 = new Pair();
            p2.First = "one";
            p2.Second = "2";
            Assert.IsFalse(p.Equals(p2));
            p2.Second = "two";
            Assert.AreEqual(p, p2);
        }

        [Test]
        public void TestQuartz625()
        {
            Pair p = new Pair();

            Pair p2 = new Pair();
            p2.First = "one";
            Assert.IsFalse(p.Equals(p2));

            Pair p3 = new Pair();
            p3.Second = "two";
            Assert.IsFalse(p.Equals(p3));

            Pair p4 = new Pair();
            p4.First = "one";
            p4.Second = "two";
            Assert.IsFalse(p.Equals(p4));

            Pair p5 = new Pair();
            Assert.AreEqual(p, p5);
        }
    }
}