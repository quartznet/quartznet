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

using NUnit.Framework;

using Quartz.Util;
using System;
using System.Collections;
using System.Collections.Generic;

namespace Quartz.Tests.Unit.Utils
{
    /// <summary>
    /// Unit test for DirtyFlagMap.  These tests focus on making
    /// sure the isDirty flag is set correctly.
    /// </summary>
    /// <author>Marko Lahma (.NET)</author>
    [TestFixture]
    public class DirtyFlagMapTest
    {
        [Test]
        public void TryGetValue_KeyIsNull()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            const string key = null;

            try
            {
                dirtyFlagMap.TryGetValue(key, out var value);
                Assert.Fail();
            }
            catch (ArgumentNullException ex)
            {
                Assert.AreEqual(nameof(key), ex.ParamName);
            }

            Assert.IsFalse(dirtyFlagMap.Dirty);
        }

        [Test]
        public void TryGetValue_KeyIsFound_ValueIsNull()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Put("a", null);
            dirtyFlagMap.ClearDirtyFlag();

            Assert.IsTrue(dirtyFlagMap.TryGetValue("a", out var value));
            Assert.IsFalse(dirtyFlagMap.Dirty);
            Assert.IsNull(value);
        }

        [Test]
        public void TryGetValue_KeyIsFound_ValueIsNotNull()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Put("a", "x");
            dirtyFlagMap.ClearDirtyFlag();

            Assert.IsTrue(dirtyFlagMap.TryGetValue("a", out var value));
            Assert.IsFalse(dirtyFlagMap.Dirty);
            Assert.IsNotNull(value);
            Assert.AreEqual("x", value);
        }

        [Test]
        public void TryGetValue_KeyIsNotFound_TValueIsReferenceType()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();

            Assert.IsFalse(dirtyFlagMap.TryGetValue("a", out var value));
            Assert.IsFalse(dirtyFlagMap.Dirty);
            Assert.IsNull(value);
        }

        [Test]
        public void TryGetValue_KeyIsNotFound_TValueIsNonNullableStruct()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, int>();

            Assert.IsFalse(dirtyFlagMap.TryGetValue("a", out var value));
            Assert.IsFalse(dirtyFlagMap.Dirty);
            Assert.IsNotNull(value);
            Assert.AreEqual(default(int), value);
        }

        [Test]
        public void TryGetValue_KeyIsNotFound_TValueIsNullableStruct()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, int?>();

            Assert.IsFalse(dirtyFlagMap.TryGetValue("a", out var value));
            Assert.IsFalse(dirtyFlagMap.Dirty);
            Assert.IsNull(value);
        }

        [Test]
        public void Indexer_Get_KeyIsNull()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            const string key = null;

            try
            {
                var actual = dirtyFlagMap[key];
                Assert.Fail("Should have thrown, but returned " + actual);
            }
            catch (ArgumentNullException ex)
            {
                Assert.AreEqual(nameof(key), ex.ParamName);
            }

            Assert.IsFalse(dirtyFlagMap.Dirty);
        }

        [Test]
        public void Indexer_Get_KeyIsFound_ValidIsNotNull()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Put("a", "x");
            dirtyFlagMap.ClearDirtyFlag();

            var actual = dirtyFlagMap["a"];

            Assert.IsFalse(dirtyFlagMap.Dirty);
            Assert.IsNotNull(actual);
            Assert.AreEqual("x", actual);
        }

        [Test]
        public void Indexer_Get_KeyIsFound_ValidIsNull()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Put("a", null);
            dirtyFlagMap.ClearDirtyFlag();

            var actual = dirtyFlagMap["a"];

            Assert.IsFalse(dirtyFlagMap.Dirty);
            Assert.IsNull(actual);
        }

        [Test]
        public void Indexer_Get_KeyIsNotFound_TValueIsReferenceType()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();

            // #1417: This should throw a KeyNotFoundException, see commented code below

            var value = dirtyFlagMap["a"];

            Assert.IsFalse(dirtyFlagMap.Dirty);
            Assert.IsNull(value);

            /*
            try
            {
                var actual = dirtyFlagMap["a"];
                Assert.Fail("Should have thrown, but returned " + actual);
            }
            catch (KeyNotFoundException)
            {
            }

            Assert.IsFalse(dirtyFlagMap.Dirty);
            */
        }

        [Test]
        public void Indexer_Get_KeyIsNotFound_TValueIsNonNullableStruct()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, int>();

            // #1417: This should throw a KeyNotFoundException, see commented code below

            var actual = dirtyFlagMap["a"];

            Assert.IsFalse(dirtyFlagMap.Dirty);
            Assert.IsNotNull(actual);
            Assert.AreEqual(default(int), actual);

            /*
            try
            {
                var actual = dirtyFlagMap["a"];
                Assert.Fail("Should have thrown, but returned " + actual);
            }
            catch (KeyNotFoundException)
            {
            }

            Assert.IsFalse(dirtyFlagMap.Dirty);
            */
        }

        [Test]
        public void Indexer_Get_KeyIsNotFound_TValueIsNullableStruct()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, int?>();

            // #1417: This should throw a KeyNotFoundException, see commented code below

            var actual = dirtyFlagMap["a"];

            Assert.IsFalse(dirtyFlagMap.Dirty);
            Assert.IsNull(actual);

            /*
            try
            {
                var actual = dirtyFlagMap["a"];
                Assert.Fail("Should have thrown, but returned " + actual);
            }
            catch (KeyNotFoundException)
            {
            }

            Assert.IsFalse(dirtyFlagMap.Dirty);
            */
        }

        [Test]
        public void Indexer_Set_KeyIsNull()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            const string key = null;

            try
            {
                dirtyFlagMap[key] = "x";
                Assert.Fail();
            }
            catch (ArgumentNullException ex)
            {
                Assert.AreEqual(nameof(key), ex.ParamName);
            }

            Assert.IsFalse(dirtyFlagMap.Dirty);
        }

        [Test]
        public void Indexer_Set_KeyIsFound_ValidDoesNotEqualCurrentValue()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Put("a", "x");
            dirtyFlagMap.ClearDirtyFlag();

            dirtyFlagMap["a"] = "y";

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.AreEqual("y", dirtyFlagMap["a"]);

            dirtyFlagMap.ClearDirtyFlag();

            dirtyFlagMap["a"] = null;

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.IsNull(dirtyFlagMap["a"]);

            dirtyFlagMap.ClearDirtyFlag();

            dirtyFlagMap["a"] = "b";

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.AreEqual("b", dirtyFlagMap["a"]);
        }

        [Test]
        public void Indexer_Set_KeyIsFound_ValidEqualsCurrentValue()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Put("a", "x");
            dirtyFlagMap.Put("b", null);
            dirtyFlagMap.ClearDirtyFlag();

            dirtyFlagMap["a"] = "y";

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.AreEqual("y", dirtyFlagMap["a"]);

            dirtyFlagMap.ClearDirtyFlag();

            dirtyFlagMap["b"] = null;

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("b"));
            Assert.IsNull(dirtyFlagMap["b"]);
        }

        [Test]
        public void Indexer_Set_KeyIsNotFound()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();

            dirtyFlagMap["a"] = "x";

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.AreEqual("x", dirtyFlagMap["a"]);

            dirtyFlagMap.ClearDirtyFlag();

            dirtyFlagMap["b"] = null;

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("b"));
            Assert.IsNull(dirtyFlagMap["b"]);

        }

        [Test]
        public void Remove_KeyValuePair_KeyIsNull()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            var kvp = new KeyValuePair<string, string>(null, "x");

            try
            {
                dirtyFlagMap.Remove(kvp);
                Assert.Fail();
            }
            catch (ArgumentNullException ex)
            {
                Assert.AreEqual("key", ex.ParamName);
            }

            Assert.IsFalse(dirtyFlagMap.Dirty);
        }

        [Test]
        public void Remove_KeyValuePair_KeyIsNotFound()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            var kvp = new KeyValuePair<string, string>("a", "x");

            Assert.IsFalse(dirtyFlagMap.Remove(kvp));
            Assert.IsFalse(dirtyFlagMap.Dirty);
        }

        [Test]
        public void Remove_KeyValuePair_KeyIsFound_ValueDoesNotEqualCurrentValue()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Put("a", "x");
            dirtyFlagMap.ClearDirtyFlag();

            // #1417: We should not remove entry if values are not equal, see commented code below

            Assert.IsTrue(dirtyFlagMap.Remove(new KeyValuePair<string, string>("a", null)));
            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsFalse(dirtyFlagMap.ContainsKey("a"));

            dirtyFlagMap.Clear();
            dirtyFlagMap.Put("a", null);
            dirtyFlagMap.ClearDirtyFlag();

            Assert.IsTrue(dirtyFlagMap.Remove(new KeyValuePair<string, string>("a", "y")));
            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsFalse(dirtyFlagMap.ContainsKey("a"));

            /*
            Assert.IsFalse(dirtyFlagMap.Remove(new KeyValuePair<string, string>("a", null)));
            Assert.IsFalse(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.AreEqual("x", dirtyFlagMap["a"]);

            dirtyFlagMap.ClearDirtyFlag();

            Assert.IsFalse(dirtyFlagMap.Remove(new KeyValuePair<string, string>("a", "y")));
            Assert.IsFalse(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.AreEqual("x", dirtyFlagMap["a"]);

            dirtyFlagMap.Clear();
            dirtyFlagMap.Put("a", null);
            dirtyFlagMap.ClearDirtyFlag();

            Assert.IsTrue(dirtyFlagMap.Remove(new KeyValuePair<string, string>("a", "z")));
            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsFalse(dirtyFlagMap.ContainsKey("a"));
            */
        }

        [Test]
        public void Remove_KeyValuePair_KeyIsFound_ValueEqualsCurrentValue()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Put("a", "x");
            dirtyFlagMap.ClearDirtyFlag();

            Assert.IsTrue(dirtyFlagMap.Remove(new KeyValuePair<string, string>("a", "x")));
            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsFalse(dirtyFlagMap.ContainsKey("a"));

            dirtyFlagMap.Put("a", null);
            dirtyFlagMap.ClearDirtyFlag();

            Assert.IsTrue(dirtyFlagMap.Remove(new KeyValuePair<string, string>("a", null)));
            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsFalse(dirtyFlagMap.ContainsKey("a"));
        }


        [Test]
        public void Add_KeyValuePair_KeyIsNull()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            var kvp = new KeyValuePair<string, string>(null, "x");

            try
            {
                dirtyFlagMap.Add(kvp);
                Assert.Fail();
            }
            catch (ArgumentNullException ex)
            {
                Assert.AreEqual("key", ex.ParamName);
            }

            // #1417: this should return false
            Assert.IsTrue(dirtyFlagMap.Dirty);
        }

        [Test]
        public void Add_KeyValuePair_KeyIsNotFound()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            var kvp = new KeyValuePair<string, string>("a", "x");

            dirtyFlagMap.Add(kvp);
        }

        [Test]
        public void Add_KeyValuePair_KeyIsFound_ValueDoesNotEqualCurrentValue()
        {
            /*
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Put("a", "x");
            dirtyFlagMap.ClearDirtyFlag();

            // #1417: We should throw ArgumentException, see commented code below

            dirtyFlagMap.Add(new KeyValuePair<string, string>("a", "y"));

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.AreEqual("y", dirtyFlagMap["a"]);

            dirtyFlagMap.ClearDirtyFlag();

            dirtyFlagMap.Add(new KeyValuePair<string, string>("a", null));

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.IsNull(dirtyFlagMap["a"]);

            dirtyFlagMap.ClearDirtyFlag();

            dirtyFlagMap.Add(new KeyValuePair<string, string>("a", "z"));

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.AreEqual("z", dirtyFlagMap["a"]);
            */

            /*
            try
            {
                dirtyFlagMap.Add(new KeyValuePair<string, string>("a", "y"));
                Assert.Fail();
            }
            catch (ArgumentException)
            {
                // An item with the same key has already been added. Key: a
            }

            Assert.IsFalse(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.AreEqual("x", dirtyFlagMap["a"]);

            dirtyFlagMap.ClearDirtyFlag();

            try
            {
                dirtyFlagMap.Add(new KeyValuePair<string, string>("a", null));
                Assert.Fail();
            }
            catch (ArgumentException)
            {
                // An item with the same key has already been added. Key: a
            }

            Assert.IsFalse(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.AreEqual("x", dirtyFlagMap["a"]);

            dirtyFlagMap.Clear();
            dirtyFlagMap.Add("a", null);
            dirtyFlagMap.ClearDirtyFlag();

            try
            {
                dirtyFlagMap.Add(new KeyValuePair<string, string>("a", "z"));
                Assert.Fail();
            }
            catch (ArgumentException)
            {
                // An item with the same key has already been added. Key: a
            }

            Assert.IsFalse(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.IsNull(dirtyFlagMap["a"]);
            */
        }

        [Test]
        public void Add_KeyValuePair_KeyIsFound_ValueEqualsCurrentValue()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Put("a", "x");
            dirtyFlagMap.ClearDirtyFlag();

            Assert.IsTrue(dirtyFlagMap.Remove(new KeyValuePair<string, string>("a", "x")));
            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsFalse(dirtyFlagMap.ContainsKey("a"));

            dirtyFlagMap.Clear();
            dirtyFlagMap.Put("a", null);
            dirtyFlagMap.ClearDirtyFlag();

            Assert.IsTrue(dirtyFlagMap.Remove(new KeyValuePair<string, string>("a", null)));
            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsFalse(dirtyFlagMap.ContainsKey("a"));
        }

        [Test]
        public void IDictionary_Add_KeyAndValue_KeyIsNull()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            const object key = null;

            try
            {
                ((IDictionary) dirtyFlagMap).Add(key, "x");
                Assert.Fail();
            }
            catch (ArgumentNullException ex)
            {
                Assert.AreEqual("key", ex.ParamName);
            }

            // #1417: this should return false
            Assert.IsTrue(dirtyFlagMap.Dirty);
        }

        [Test]
        public void IDictionary_Add_KeyAndValue_KeyCannotBeAssignedToTKey()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            object key = true;

            // #1417: this should throw ArgumentException, see commented code below

            try
            {
                ((IDictionary) dirtyFlagMap).Add(key, 5);
                Assert.Fail();
            }
            catch (InvalidCastException)
            {
            }

            Assert.IsFalse(dirtyFlagMap.Dirty);

            /*
            try
            {
                ((IDictionary) dirtyFlagMap).Add(key, 5);
                Assert.Fail();
            }
            catch (ArgumentException ex)
            {
                // The value "5" is not of type "System.String" and cannot be used in this generic collection
                Assert.AreEqual("key", ex.ParamName);
            }

            Assert.IsFalse(dirtyFlagMap.Dirty);
            */
        }


        [Test]
        public void IDictionary_Add_KeyAndValue_KeyIsNotFound_ValueIsNotNull_ValueCannotBeAssignedToTValue()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            object key = "x";

            // #1417: this should throw ArgumentException, see commented code below

            try
            {
                ((IDictionary) dirtyFlagMap).Add(key, 5);
                Assert.Fail();
            }
            catch (InvalidCastException)
            {
            }

            Assert.IsFalse(dirtyFlagMap.Dirty);

            /*

            try
            {
                ((IDictionary) dirtyFlagMap).Add(key, 5);
                Assert.Fail();
            }
            catch (ArgumentException ex)
            {
                // The value "5" is not of type "System.String" and cannot be used in this generic collection
                Assert.AreEqual("value", ex.ParamName);
            }

            Assert.IsFalse(dirtyFlagMap.Dirty);
            */
        }

        [Test]
        public void IDictionary_Add_KeyAndValue_KeyIsNotFound_ValueIsNotNull_ValueCanBeAssignedToTValue()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            object key = "a";

            ((IDictionary) dirtyFlagMap).Add(key, "x");

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.AreEqual("x", dirtyFlagMap["a"]);
        }

        [Test]
        public void IDictionary_Add_KeyAndValue_KeyIsNotFound_ValueIsNull_TValueIsNonNullableStruct()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, int>();
            object key = "a";

            // #1417: this should throw ArgumentNullException, see commented code below

            try
            {
                ((IDictionary) dirtyFlagMap).Add(key, null);
                Assert.Fail();
            }
            catch (NullReferenceException)
            {
            }

            Assert.IsFalse(dirtyFlagMap.Dirty);

            /*
            try
            {
                ((IDictionary) dirtyFlagMap).Add(key, null);
                Assert.Fail();
            }
            catch (ArgumentNullException ex)
            {
                Assert.AreEqual("value", ex.ParamName);
            }

            Assert.IsFalse(dirtyFlagMap.Dirty);
            Assert.IsFalse(dirtyFlagMap.ContainsKey("a"));
            */
        }

        [Test]
        public void IDictionary_Add_KeyAndValue_KeyIsNotFound_ValueIsNull_TValueIsNullableStruct()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, int?>();
            object key = "a";

            ((IDictionary) dirtyFlagMap).Add(key, null);

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.IsNull(dirtyFlagMap["a"]);
        }

        [Test]
        public void IDictionary_Add_KeyAndValue_KeyIsNotFound_ValueIsNull_TValueIsReferenceType()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            object key = "a";

            ((IDictionary) dirtyFlagMap).Add(key, null);

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.IsNull(dirtyFlagMap["a"]);
        }

        [Test]
        public void IDictionary_Add_KeyAndValue_KeyIsFound_ValueDoesNotEqualCurrentValue()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Add("a", "x");
            dirtyFlagMap.ClearDirtyFlag();

            // #1417: We should throw ArgumentException, see commented code below

            ((IDictionary) dirtyFlagMap).Add("a", "y");

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.AreEqual("y", dirtyFlagMap["a"]);

            dirtyFlagMap.ClearDirtyFlag();

            ((IDictionary) dirtyFlagMap).Add("a", null);

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.IsNull(dirtyFlagMap["a"]);

            dirtyFlagMap.ClearDirtyFlag();

            ((IDictionary) dirtyFlagMap).Add("a", "z");

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.AreEqual("z", dirtyFlagMap["a"]);

            /*
            try
            {
                ((IDictionary) dirtyFlagMap).Add("a", "y");
                Assert.Fail();
            }
            catch (ArgumentException)
            {
                // An item with the same key has already been added. Key: a
            }

            Assert.IsFalse(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.AreEqual("x", dirtyFlagMap["a"]);

            dirtyFlagMap.ClearDirtyFlag();

            try
            {
                ((IDictionary) dirtyFlagMap).Add("a", null);
                Assert.Fail();
            }
            catch (ArgumentException)
            {
                // An item with the same key has already been added. Key: a
            }

            Assert.IsFalse(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.AreEqual("x", dirtyFlagMap["a"]);

            dirtyFlagMap.Clear();
            dirtyFlagMap.Add("a", null);
            dirtyFlagMap.ClearDirtyFlag();

            try
            {
                ((IDictionary) dirtyFlagMap).Add("a", "z");
                Assert.Fail();
            }
            catch (ArgumentException)
            {
                // An item with the same key has already been added. Key: a
            }

            Assert.IsFalse(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.IsNull(dirtyFlagMap["a"]);
            */
        }

        [Test]
        public void IDictionary_Add_KeyAndValue_KeyIsFound_ValueEqualsCurrentValue()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Put("a", "x");
            dirtyFlagMap.ClearDirtyFlag();

            Assert.IsTrue(dirtyFlagMap.Remove(new KeyValuePair<string, string>("a", "x")));
            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsFalse(dirtyFlagMap.ContainsKey("a"));

            dirtyFlagMap.Clear();
            dirtyFlagMap.Put("a", null);
            dirtyFlagMap.ClearDirtyFlag();

            Assert.IsTrue(dirtyFlagMap.Remove(new KeyValuePair<string, string>("a", null)));
            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsFalse(dirtyFlagMap.ContainsKey("a"));
        }

        [Test]
        public void Contains_KeyIsNull()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            const object key = null;

            try
            {
                dirtyFlagMap.Contains(key);
                Assert.Fail();
            }
            catch (ArgumentNullException ex)
            {
                Assert.AreEqual(nameof(key), ex.ParamName);
            }
        }

        [Test]
        public void Contains_KeyIsNotFound()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            object key = "a";

            Assert.IsFalse(dirtyFlagMap.Contains(key));
            Assert.IsFalse(dirtyFlagMap.Dirty);
        }

        [Test]
        public void Contains_KeyIsFound()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Put("a", "x");
            dirtyFlagMap.ClearDirtyFlag();

            Assert.IsTrue(dirtyFlagMap.Contains((object) "a"));
            Assert.IsFalse(dirtyFlagMap.Dirty);
        }

        [Test]
        public void Contains_KeyCannotBeAssignedToTKey()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Add("a", "x");
            dirtyFlagMap.ClearDirtyFlag();

            Assert.IsFalse(((IDictionary) dirtyFlagMap).Contains((object) 5));
            Assert.IsFalse(dirtyFlagMap.Dirty);
        }

        [Test]
        public void TestClear()
        {
            DirtyFlagMap<string, string> dirtyFlagMap = new DirtyFlagMap<string, string>();
            Assert.IsFalse(dirtyFlagMap.Dirty);

            dirtyFlagMap.Clear();
            Assert.IsFalse(dirtyFlagMap.Dirty);
            dirtyFlagMap.Put("X", "Y");
            dirtyFlagMap.ClearDirtyFlag();
            dirtyFlagMap.Clear();
            Assert.IsTrue(dirtyFlagMap.Dirty);
        }

        [Test]
        public void TestPut()
        {
            DirtyFlagMap<string, string> dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Put("a", "Y");
            Assert.IsTrue(dirtyFlagMap.Dirty);
        }

        [Test]
        public void TestRemove()
        {
            DirtyFlagMap<string, string> dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Put("a", "Y");
            dirtyFlagMap.ClearDirtyFlag();

            dirtyFlagMap.Remove("b");
            Assert.IsFalse(dirtyFlagMap.Dirty);

            dirtyFlagMap.Remove("a");
            Assert.IsTrue(dirtyFlagMap.Dirty);
        }

        //[Test]
        //[Ignore]
        //public void TestEntrySetRemove() 
        //{
        //    DirtyFlagMap<string, string> dirtyFlagMap = new DirtyFlagMap<string, string>();
        //    ISet<string> entrySet = dirtyFlagMap.EntrySet();
        //    dirtyFlagMap.Remove("a");
        //    Assert.IsFalse(dirtyFlagMap.Dirty);
        //    dirtyFlagMap.Put("a", "Y");
        //    dirtyFlagMap.ClearDirtyFlag();
        //    entrySet.Remove("b");
        //    Assert.IsFalse(dirtyFlagMap.Dirty);
        //    entrySet.Remove(entrySet.First());
        //    Assert.IsTrue(dirtyFlagMap.Dirty);
        //}

        //		public void TestEntrySetRetainAll() 
        //		{
        //			DirtyFlagMap dirtyFlagMap = new DirtyFlagMap();
        //			ISet entrySet = dirtyFlagMap.EntrySet();
        //			entrySet.retainAll(Collections.EMPTY_LIST);
        //			Assert.IsFalse(dirtyFlagMap.Dirty);
        //			dirtyFlagMap.Put("a", "Y");
        //			dirtyFlagMap.ClearDirtyFlag();
        //			entrySet.retainAll(Collections.singletonList(entrySet.iterator().next()));
        //			Assert.IsFalse(dirtyFlagMap.Dirty);
        //			entrySet.retainAll(Collections.EMPTY_LIST);
        //			Assert.IsTrue(dirtyFlagMap.Dirty);
        //		}

        //		public void TestEntrySetRemoveAll() 
        //		{
        //			DirtyFlagMap dirtyFlagMap = new DirtyFlagMap();
        //			ISet entrySet = dirtyFlagMap.EntrySet();
        //			entrySet.removeAll(Collections.EMPTY_LIST);
        //			Assert.IsFalse(dirtyFlagMap.Dirty);
        //			dirtyFlagMap.Put("a", "Y");
        //			dirtyFlagMap.ClearDirtyFlag();
        //			entrySet.removeAll(Collections.EMPTY_LIST);
        //			Assert.IsFalse(dirtyFlagMap.Dirty);
        //			entrySet.removeAll(Collections.singletonList(entrySet.iterator().next()));
        //			Assert.IsTrue(dirtyFlagMap.Dirty);
        //		}

        //[Test]
        //[Ignore]
        //public void TestEntrySetClear() 
        //{
        //    DirtyFlagMap<string,string> dirtyFlagMap = new DirtyFlagMap<string, string>();
        //    Dictionary<string, string>.Enumerator entrySet = dirtyFlagMap.EntrySet();
        //    entrySet.Clear();
        //    Assert.IsFalse(dirtyFlagMap.Dirty);
        //    dirtyFlagMap.Put("a", "Y");
        //    dirtyFlagMap.ClearDirtyFlag();
        //    entrySet.Clear();
        //    Assert.IsTrue(dirtyFlagMap.Dirty);
        //}

        //[Test]
        //[Ignore]
        //public void TestEntrySetIterator() 
        //{
        //    DirtyFlagMap<string, string> dirtyFlagMap = new DirtyFlagMap<string, string>();
        //    IDictionary<string, string> entrySet = dirtyFlagMap.EntrySet();
        //    dirtyFlagMap.Put("a", "A");
        //    dirtyFlagMap.Put("b", "B");
        //    dirtyFlagMap.Put("c", "C");
        //    dirtyFlagMap.ClearDirtyFlag();
        //    DictionaryEntry entryToBeRemoved = (DictionaryEntry) entrySet.First();
        //    string removedKey = (string) entryToBeRemoved.Key;
        //    entrySet.Remove(entryToBeRemoved);
        //    Assert.AreEqual(2, dirtyFlagMap.Count);
        //    Assert.IsTrue(dirtyFlagMap.Dirty);
        //    Assert.IsFalse(dirtyFlagMap.ContainsKey(removedKey));
        //    dirtyFlagMap.ClearDirtyFlag();
        //    DictionaryEntry entry = (DictionaryEntry)entrySet.First();
        //    entry.Value = "BB";
        //    Assert.IsTrue(dirtyFlagMap.Dirty);
        //    Assert.IsTrue(dirtyFlagMap.ContainsValue("BB"));
        //}

        //[Test]
        //[Ignore]
        //public void TestEntrySetToArray() 
        //{
        //    DirtyFlagMap<string,string> dirtyFlagMap = new DirtyFlagMap<string, string>();
        //    Dictionary<string, string>.Enumerator entrySet = dirtyFlagMap.EntrySet();
        //    dirtyFlagMap.Put("a", "A");
        //    dirtyFlagMap.Put("b", "B");
        //    dirtyFlagMap.Put("c", "C");
        //    dirtyFlagMap.ClearDirtyFlag();
        //    object[] array = (object[]) new List<DictionaryEntry>(entrySet).ToArray();
        //    Assert.AreEqual(3, array.Length);
        //    DictionaryEntry entry = (DictionaryEntry) array[0];
        //    entry.Value = "BB";
        //    Assert.IsTrue(dirtyFlagMap.Dirty);
        //    Assert.IsTrue(dirtyFlagMap.ContainsValue("BB"));
        //}

        //[Test]
        //[Ignore]
        //public void TestEntrySetToArrayWithArg() 
        //{
        //    DirtyFlagMap dirtyFlagMap = new DirtyFlagMap();
        //    ISet entrySet = dirtyFlagMap.EntrySet();
        //    dirtyFlagMap.Put("a", "A");
        //    dirtyFlagMap.Put("b", "B");
        //    dirtyFlagMap.Put("c", "C");
        //    dirtyFlagMap.ClearDirtyFlag();
        //    object[] array = (object[]) new ArrayList(entrySet).ToArray(typeof(DictionaryEntry));
        //    Assert.AreEqual(3, array.Length);
        //    DictionaryEntry entry = (DictionaryEntry)array[0];
        //    entry.Value = "BB";
        //    Assert.IsTrue(dirtyFlagMap.Dirty);
        //    Assert.IsTrue(dirtyFlagMap.ContainsValue("BB"));
        //}

        //[Test]
        //[Ignore]
        //public void TestKeySetClear() 
        //{
        //    DirtyFlagMap dirtyFlagMap = new DirtyFlagMap();
        //    ISet keySet = dirtyFlagMap.KeySet();
        //    keySet.Clear();
        //    Assert.IsFalse(dirtyFlagMap.Dirty);
        //    dirtyFlagMap.Put("a", "Y");
        //    dirtyFlagMap.ClearDirtyFlag();
        //    keySet.Clear();
        //    Assert.IsTrue(dirtyFlagMap.Dirty);
        //    Assert.AreEqual(0, dirtyFlagMap.Count);
        //}

        //[Test]
        //[Ignore]
        //public void TestValuesClear() 
        //{
        //    DirtyFlagMap dirtyFlagMap = new DirtyFlagMap();
        //    IList values = new ArrayList(dirtyFlagMap.Values);
        //    values.Clear();
        //    Assert.IsFalse(dirtyFlagMap.Dirty);
        //    dirtyFlagMap.Put("a", "Y");
        //    dirtyFlagMap.ClearDirtyFlag();
        //    values.Clear();
        //    Assert.IsTrue(dirtyFlagMap.Dirty);
        //    Assert.AreEqual(0, dirtyFlagMap.Count);
        //}    
    }
}