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
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using NUnit.Framework;

using Quartz.Util;

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
        public void Indexer_Get_KeyIsFound_ValueIsNotNull()
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
        public void Indexer_Get_KeyIsFound_ValueIsNull()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Put("a", null);
            dirtyFlagMap.ClearDirtyFlag();

            var actual = dirtyFlagMap["a"];

            Assert.IsFalse(dirtyFlagMap.Dirty);
            Assert.IsNull(actual);
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
        public void Remove_Key_KeyIsFound()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Add("a", "x");
            dirtyFlagMap.ClearDirtyFlag();

            Assert.IsTrue(dirtyFlagMap.Remove("a"));
            Assert.IsTrue(dirtyFlagMap.Dirty);
        }

        [Test]
        public void Remove_Key_KeyIsNotFound()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Add("a", "x");
            dirtyFlagMap.ClearDirtyFlag();

            Assert.IsFalse(dirtyFlagMap.Remove("x"));
            Assert.IsFalse(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
        }

        [Test]
        public void Remove_Key_KeyIsNull()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            const string key = null;

            try
            {
                dirtyFlagMap.Remove(key);
                Assert.Fail();
            }
            catch (ArgumentNullException ex)
            {
                Assert.AreEqual(nameof(key), ex.ParamName);
            }

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
        public void Remove_KeyValuePair_KeyIsNotFound()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            var kvp = new KeyValuePair<string, string>("a", "x");

            Assert.IsFalse(dirtyFlagMap.Remove(kvp));
            Assert.IsFalse(dirtyFlagMap.Dirty);
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
        public void Add_KeyAndValue_KeyIsFound_ValueDoesNotEqualCurrentValue()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Put("a", "x");
            dirtyFlagMap.ClearDirtyFlag();

            try
            {
                dirtyFlagMap.Add("a", "y");
                Assert.Fail();
            }
            catch (ArgumentException)
            {
                // An item with the same key has already been added. Key: a
            }

            Assert.IsFalse(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.AreEqual("x", dirtyFlagMap["a"]);

            dirtyFlagMap.Put("b", null);
            dirtyFlagMap.ClearDirtyFlag();

            try
            {
                dirtyFlagMap.Add("b", "x");
                Assert.Fail();
            }
            catch (ArgumentException)
            {
                // An item with the same key has already been added. Key: b
            }

            Assert.IsFalse(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("b"));
            Assert.IsNull(dirtyFlagMap["b"]);

            dirtyFlagMap.Put("c", "z");
            dirtyFlagMap.ClearDirtyFlag();

            try
            {
                dirtyFlagMap.Add("c", null);
                Assert.Fail();
            }
            catch (ArgumentException)
            {
                // An item with the same key has already been added. Key: c
            }

            Assert.IsFalse(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("c"));
            Assert.AreEqual("z", dirtyFlagMap["c"]);
        }

        [Test]
        public void Add_KeyAndValue_KeyIsFound_ValueEqualsCurrentValue()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Put("a", "x");
            dirtyFlagMap.ClearDirtyFlag();

            try
            {
                dirtyFlagMap.Add("a", "x");
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
            dirtyFlagMap.Put("a", null);
            dirtyFlagMap.ClearDirtyFlag();

            try
            {
                dirtyFlagMap.Add("a", null);
                Assert.Fail();
            }
            catch (ArgumentException)
            {
                // An item with the same key has already been added. Key: a
            }

            Assert.IsFalse(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.IsNull(dirtyFlagMap["a"]);
        }

        [Test]
        public void Add_KeyAndValue_KeyIsNotFound()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();

            dirtyFlagMap.Add("a", "x");

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.AreEqual("x", dirtyFlagMap["a"]);

            dirtyFlagMap.ClearDirtyFlag();

            dirtyFlagMap.Add("b", null);

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("b"));
            Assert.IsNull(dirtyFlagMap["b"]);
        }

        [Test]
        public void Add_KeyAndValue_KeyIsNull()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            const string key = null;

            try
            {
                dirtyFlagMap.Add(key, "x");
                Assert.Fail();
            }
            catch (ArgumentNullException ex)
            {
                Assert.AreEqual(nameof(key), ex.ParamName);
            }

            Assert.IsFalse(dirtyFlagMap.Dirty);
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

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.AreEqual("x", dirtyFlagMap["a"]);
        }

        [Test]
        public void Add_KeyValuePair_KeyIsFound_ValueDoesNotEqualCurrentValue()
        {
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

            dirtyFlagMap.Add(new KeyValuePair<string, string>("a", "x"));

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.AreEqual("x", dirtyFlagMap["a"]);

            dirtyFlagMap.Clear();
            dirtyFlagMap.Put("a", null);
            dirtyFlagMap.ClearDirtyFlag();

            dirtyFlagMap.Add(new KeyValuePair<string, string>("a", null));

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.IsNull(dirtyFlagMap["a"]);
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
        public void IDictionary_Indexer_Get_KeyCannotBeAssignedToTKey()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Add("a", "x");
            dirtyFlagMap.ClearDirtyFlag();

            // #1417: This should not throw, see commented code below

            try
            {
                var actual = ((IDictionary) dirtyFlagMap)[5];
                Assert.Fail("Should have thrown, but returned " + actual);
            }
            catch (InvalidCastException)
            {
            }

            /*
            var actual = ((IDictionary) dirtyFlagMap)[5];

            Assert.IsFalse(dirtyFlagMap.Dirty);
            Assert.IsNull(actual);
            */
        }

        [Test]
        public void IDictionary_Indexer_Get_KeyIsFound_ValueIsNotNull()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Put("a", "x");
            dirtyFlagMap.ClearDirtyFlag();

            var actual = ((IDictionary) dirtyFlagMap)["a"];

            Assert.IsFalse(dirtyFlagMap.Dirty);
            Assert.IsNotNull(actual);
            Assert.AreEqual("x", actual);
        }

        [Test]
        public void IDictionary_Indexer_Get_KeyIsFound_ValueIsNull()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Put("a", null);
            dirtyFlagMap.ClearDirtyFlag();

            var actual = ((IDictionary) dirtyFlagMap)["a"];

            Assert.IsFalse(dirtyFlagMap.Dirty);
            Assert.IsNull(actual);
        }

        [Test]
        public void IDictionary_Indexer_Get_KeyIsNotFound_TValueIsNonNullableStruct()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, int>();

            // #1417: This should throw a KeyNotFoundException, see commented code below

            var actual = ((IDictionary) dirtyFlagMap)["a"];

            Assert.IsFalse(dirtyFlagMap.Dirty);
            Assert.IsNotNull(actual);
            Assert.AreEqual(default(int), actual);

            /*
            try
            {
                var actual = ((IDictionary) dirtyFlagMap)["a"];
                Assert.Fail("Should have thrown, but returned " + actual);
            }
            catch (KeyNotFoundException)
            {
            }

            Assert.IsFalse(dirtyFlagMap.Dirty);
            */
        }

        [Test]
        public void IDictionary_Indexer_Get_KeyIsNotFound_TValueIsNullableStruct()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, int?>();

            // #1417: This should throw a KeyNotFoundException, see commented code below

            var actual = ((IDictionary) dirtyFlagMap)["a"];

            Assert.IsFalse(dirtyFlagMap.Dirty);
            Assert.IsNull(actual);

            /*
            try
            {
                var actual = ((IDictionary) dirtyFlagMap)["a"];
                Assert.Fail("Should have thrown, but returned " + actual);
            }
            catch (KeyNotFoundException)
            {
            }

            Assert.IsFalse(dirtyFlagMap.Dirty);
            */
        }

        [Test]
        public void IDictionary_Indexer_Get_KeyIsNotFound_TValueIsReferenceType()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();

            // #1417: This should throw a KeyNotFoundException, see commented code below

            var value = ((IDictionary) dirtyFlagMap)["a"];

            Assert.IsFalse(dirtyFlagMap.Dirty);
            Assert.IsNull(value);

            /*
            try
            {
                var actual = ((IDictionary) dirtyFlagMap)["a"];
                Assert.Fail("Should have thrown, but returned " + actual);
            }
            catch (KeyNotFoundException)
            {
            }

            Assert.IsFalse(dirtyFlagMap.Dirty);
            */
        }

        [Test]
        public void IDictionary_Indexer_Get_KeyIsNull()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            const string key = null;

            try
            {
                var actual = ((IDictionary) dirtyFlagMap)[key];
                Assert.Fail("Should have thrown, but returned " + actual);
            }
            catch (ArgumentNullException ex)
            {
                Assert.AreEqual(nameof(key), ex.ParamName);
            }

            Assert.IsFalse(dirtyFlagMap.Dirty);
        }

        [Test]
        public void IDictionary_Indexer_Set_KeyCannotBeAssignedToTKey()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            object key = true;

            // #1417: This should throw an ArgumentException, see commented code below

            try
            {
                ((IDictionary) dirtyFlagMap)[key] = "y";
                Assert.Fail();
            }
            catch (InvalidCastException)
            {
            }

            Assert.IsFalse(dirtyFlagMap.Dirty);

            /*
            try
            {
                ((IDictionary) dirtyFlagMap)[key] = "y";
                Assert.Fail();
            }
            catch (ArgumentException ex)
            {
                // The value "True" is not of type "System.String" and cannot be used in this generic collection
                Assert.AreEqual(nameof(key), ex.ParamName);
            }

            Assert.IsFalse(dirtyFlagMap.Dirty);
            */
        }

        [Test]
        public void IDictionary_Indexer_Set_KeyIsFound_ValidDoesNotEqualCurrentValue()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Put("a", "x");
            dirtyFlagMap.ClearDirtyFlag();

            ((IDictionary) dirtyFlagMap)["a"] = "y";

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.AreEqual("y", dirtyFlagMap["a"]);

            dirtyFlagMap.ClearDirtyFlag();

            ((IDictionary) dirtyFlagMap)["a"] = null;

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.IsNull(dirtyFlagMap["a"]);

            dirtyFlagMap.ClearDirtyFlag();

            ((IDictionary) dirtyFlagMap)["a"] = "b";

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.AreEqual("b", dirtyFlagMap["a"]);
        }

        [Test]
        public void IDictionary_Indexer_Set_KeyIsFound_ValidEqualsCurrentValue()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Put("a", "x");
            dirtyFlagMap.Put("b", null);
            dirtyFlagMap.ClearDirtyFlag();

            ((IDictionary) dirtyFlagMap)["a"] = "y";

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.AreEqual("y", dirtyFlagMap["a"]);

            dirtyFlagMap.ClearDirtyFlag();

            ((IDictionary) dirtyFlagMap)["b"] = null;

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("b"));
            Assert.IsNull(dirtyFlagMap["b"]);
        }

        [Test]
        public void IDictionary_Indexer_Set_KeyIsNotFound()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();

            ((IDictionary) dirtyFlagMap)["a"] = "x";

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.AreEqual("x", dirtyFlagMap["a"]);

            dirtyFlagMap.ClearDirtyFlag();

            ((IDictionary) dirtyFlagMap)["b"] = null;

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("b"));
            Assert.IsNull(dirtyFlagMap["b"]);
        }

        [Test]
        public void IDictionary_Indexer_Set_KeyIsNull()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            const string key = null;

            try
            {
                ((IDictionary) dirtyFlagMap)[key] = "x";
                Assert.Fail();
            }
            catch (ArgumentNullException ex)
            {
                Assert.AreEqual(nameof(key), ex.ParamName);
            }

            Assert.IsFalse(dirtyFlagMap.Dirty);
        }


        [Test]
        public void IDictionary_Indexer_Set_ValueCannotBeAssignedToTValue()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();

            // #1417: This should throw an ArgumentException, see commented code below

            try
            {
                ((IDictionary) dirtyFlagMap)["a"] = 5;
                Assert.Fail();
            }
            catch (InvalidCastException)
            {
            }

            Assert.IsFalse(dirtyFlagMap.Dirty);

            /*
            try
            {
                ((IDictionary) dirtyFlagMap)["a"] = true;
                Assert.Fail();
            }
            catch (ArgumentException ex)
            {
                // The value "True" is not of type "System.String" and cannot be used in this generic collection
                Assert.AreEqual("value", ex.ParamName);
            }

            Assert.IsFalse(dirtyFlagMap.Dirty);
            */
        }


        [Test]
        public void IDictionary_Remove_KeyIsNull()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            const object key = null;

            try
            {
                ((IDictionary) dirtyFlagMap).Remove(key);
                Assert.Fail();
            }
            catch (ArgumentNullException ex)
            {
                Assert.AreEqual("key", ex.ParamName);
            }

            Assert.IsFalse(dirtyFlagMap.Dirty);
        }

        [Test]
        public void IDictionary_Remove_KeyCannotBeAssignedToTKey()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            object key = false;

            // #1417: this should not throw, see commented code below

            try
            {
                ((IDictionary) dirtyFlagMap).Remove(key);
                Assert.Fail();
            }
            catch (InvalidCastException)
            {
            }

            Assert.IsFalse(dirtyFlagMap.Dirty);

            /*
            ((IDictionary) dirtyFlagMap).Remove(key);

            Assert.IsFalse(dirtyFlagMap.Dirty);
            */
        }

        [Test]
        public void IDictionary_Remove_KeyIsFound()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Add("a", "x");
            dirtyFlagMap.ClearDirtyFlag();

            ((IDictionary) dirtyFlagMap).Remove("a");

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsFalse(dirtyFlagMap.ContainsKey("a"));
        }

        [Test]
        public void IDictionary_Remove_KeyIsNotFound()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            object key = "a";

            ((IDictionary) dirtyFlagMap).Remove(key);

            Assert.IsFalse(dirtyFlagMap.Dirty);
        }

        [Test]
        public void Contains_Key_KeyCannotBeAssignedToTKey()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Add("a", "x");
            dirtyFlagMap.ClearDirtyFlag();

            Assert.IsFalse(((IDictionary) dirtyFlagMap).Contains((object) 5));
            Assert.IsFalse(dirtyFlagMap.Dirty);
        }

        [Test]
        public void Contains_Key_KeyIsFound()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Put("a", "x");
            dirtyFlagMap.ClearDirtyFlag();

            Assert.IsTrue(dirtyFlagMap.Contains((object) "a"));
            Assert.IsFalse(dirtyFlagMap.Dirty);
        }

        [Test]
        public void Contains_Key_KeyIsNotFound()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            object key = "a";

            Assert.IsFalse(dirtyFlagMap.Contains(key));
            Assert.IsFalse(dirtyFlagMap.Dirty);
        }

        [Test]
        public void Contains_Key_KeyIsNull()
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
        public void Contains_KeyValuePair_KeyIsNull()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            var kvp = new KeyValuePair<string, string>(null, "x");

            try
            {
                ((IDictionary<string, string>) dirtyFlagMap).Contains(kvp);
                Assert.Fail();
            }
            catch (ArgumentNullException ex)
            {
                Assert.AreEqual("key", ex.ParamName);
            }
        }

        [Test]
        public void Contains_KeyValuePair_KeyIsNotFound()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();

            Assert.IsFalse(dirtyFlagMap.Contains(new KeyValuePair<string, string>("a", "x")));
            Assert.IsFalse(dirtyFlagMap.Dirty);
        }

        [Test]
        public void Contains_KeyValuePair_KeyIsFound_ValueDoesNotEqualCurrentValue()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Add("a", "x");
            dirtyFlagMap.ClearDirtyFlag();

            // #1417: this should return false
            Assert.IsTrue(dirtyFlagMap.Contains(new KeyValuePair<string, string>("a", "y")));
            Assert.IsFalse(dirtyFlagMap.Dirty);

            // #1417: this should return false
            Assert.IsTrue(dirtyFlagMap.Contains(new KeyValuePair<string, string>("a", null)));
            Assert.IsFalse(dirtyFlagMap.Dirty);
        }

        [Test]
        public void Contains_KeyValuePair_KeyIsFound_ValueEqualsCurrentValue()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Add("a", "x");
            dirtyFlagMap.ClearDirtyFlag();

            Assert.IsTrue(dirtyFlagMap.Contains(new KeyValuePair<string, string>("a", "x")));
            Assert.IsFalse(dirtyFlagMap.Dirty);

            dirtyFlagMap.Add("b", null);
            dirtyFlagMap.ClearDirtyFlag();

            Assert.IsTrue(dirtyFlagMap.Contains(new KeyValuePair<string, string>("b", null)));
            Assert.IsFalse(dirtyFlagMap.Dirty);
        }

        [Test]
        public void ContainsKey_KeyIsFound()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Put("a", "x");
            dirtyFlagMap.ClearDirtyFlag();

            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.IsFalse(dirtyFlagMap.Dirty);
        }

        [Test]
        public void ContainsKey_KeyIsNotFound()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Put("a", "x");
            dirtyFlagMap.ClearDirtyFlag();

            Assert.IsFalse(dirtyFlagMap.ContainsKey("x"));
            Assert.IsFalse(dirtyFlagMap.Dirty);
        }

        [Test]
        public void ContainsKey_KeyIsNull()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            const string key = null;

            try
            {
                dirtyFlagMap.ContainsKey(key);
                Assert.Fail();
            }
            catch (ArgumentNullException ex)
            {
                Assert.AreEqual(nameof(key), ex.ParamName);
            }
        }

        [Test]
        public void ContainsValue_ValueIsFound()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Put("a", "x");
            dirtyFlagMap.Put("b", null);
            dirtyFlagMap.ClearDirtyFlag();

            Assert.IsTrue(dirtyFlagMap.ContainsValue("x"));
            Assert.IsFalse(dirtyFlagMap.Dirty);

            Assert.IsTrue(dirtyFlagMap.ContainsValue(null));
            Assert.IsFalse(dirtyFlagMap.Dirty);
        }

        [Test]
        public void ContainsValue_ValueIsNotFound()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Put("a", "x");
            dirtyFlagMap.ClearDirtyFlag();

            Assert.IsFalse(dirtyFlagMap.ContainsValue("y"));
            Assert.IsFalse(dirtyFlagMap.Dirty);

            Assert.IsFalse(dirtyFlagMap.ContainsValue("a"));
            Assert.IsFalse(dirtyFlagMap.Dirty);

            Assert.IsFalse(dirtyFlagMap.ContainsValue(null));
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
        public void Put_KeyAndValue_KeyIsFound_ValueDoesNotEqualCurrentValue_TValueIsNonNullableStruct()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, int>();
            dirtyFlagMap.Add("a", 5);
            dirtyFlagMap.ClearDirtyFlag();

            var original = dirtyFlagMap.Put("a", 4);

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsNotNull(original);
            Assert.AreEqual(5, original);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.AreEqual(4, dirtyFlagMap["a"]);

            dirtyFlagMap.ClearDirtyFlag();

            original = dirtyFlagMap.Put("a", 0);

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsNotNull(original);
            Assert.AreEqual(4, original);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.AreEqual(0, dirtyFlagMap["a"]);

            dirtyFlagMap.ClearDirtyFlag();

            original = dirtyFlagMap.Put("a", 7);

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsNotNull(original);
            Assert.AreEqual(0, original);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.AreEqual(7, dirtyFlagMap["a"]);
        }

        [Test]
        public void Put_KeyAndValue_KeyIsFound_ValueDoesNotEqualCurrentValue_TValueIsNullableStruct()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, int?>();
            dirtyFlagMap.Add("a", 5);
            dirtyFlagMap.ClearDirtyFlag();

            var original = dirtyFlagMap.Put("a", 4);

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsNotNull(original);
            Assert.AreEqual(5, original);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.AreEqual(4, dirtyFlagMap["a"]);

            dirtyFlagMap.ClearDirtyFlag();

            original = dirtyFlagMap.Put("a", null);

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsNotNull(original);
            Assert.AreEqual(4, original);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.IsNull(dirtyFlagMap["a"]);

            dirtyFlagMap.ClearDirtyFlag();

            original = dirtyFlagMap.Put("a", 7);

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsNull(original);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.AreEqual(7, dirtyFlagMap["a"]);
        }

        [Test]
        public void Put_KeyAndValue_KeyIsFound_ValueDoesNotEqualCurrentValue_TValueIsReferenceType()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Add("a", "x");
            dirtyFlagMap.ClearDirtyFlag();

            var original = dirtyFlagMap.Put("a", "y");

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsNotNull(original);
            Assert.AreEqual("x", original);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.AreEqual("y", dirtyFlagMap["a"]);

            dirtyFlagMap.ClearDirtyFlag();

            original = dirtyFlagMap.Put("a", null);

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsNotNull(original);
            Assert.AreEqual("y", original);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.IsNull(dirtyFlagMap["a"]);

            dirtyFlagMap.ClearDirtyFlag();

            original = dirtyFlagMap.Put("a", "z");

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsNull(original);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.AreEqual("z", dirtyFlagMap["a"]);
        }

        [Test]
        public void Put_KeyAndValue_KeyIsFound_ValueEqualsCurrentValue_TValueIsNonNullableStruct()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, int>();
            dirtyFlagMap.Add("a", 5);
            dirtyFlagMap.ClearDirtyFlag();

            var original = dirtyFlagMap.Put("a", 5);

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsNotNull(original);
            Assert.AreEqual(5, original);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.AreEqual(5, dirtyFlagMap["a"]);

            dirtyFlagMap.Clear();
            dirtyFlagMap.Put("a", 0);
            dirtyFlagMap.ClearDirtyFlag();

            original = dirtyFlagMap.Put("a", 0);

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsNotNull(original);
            Assert.AreEqual(0, original);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.AreEqual(0, dirtyFlagMap["a"]);
        }

        [Test]
        public void Put_KeyAndValue_KeyIsFound_ValueEqualsCurrentValue_TValueIsNullableStruct()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, int?>();
            dirtyFlagMap.Add("a", 5);
            dirtyFlagMap.ClearDirtyFlag();

            var original = dirtyFlagMap.Put("a", 5);

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsNotNull(original);
            Assert.AreEqual(5, original);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.AreEqual(5, dirtyFlagMap["a"]);

            dirtyFlagMap.Clear();
            dirtyFlagMap.Put("a", null);
            dirtyFlagMap.ClearDirtyFlag();

            original = dirtyFlagMap.Put("a", null);

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsNull(original);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.IsNull(dirtyFlagMap["a"]);
        }

        [Test]
        public void Put_KeyAndValue_KeyIsFound_ValueEqualsCurrentValue_TValueIsReferenceType()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            dirtyFlagMap.Add("a", "x");
            dirtyFlagMap.ClearDirtyFlag();

            var original = dirtyFlagMap.Put("a", "x");

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsNotNull(original);
            Assert.AreEqual("x", original);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.AreEqual("x", dirtyFlagMap["a"]);

            dirtyFlagMap.Clear();
            dirtyFlagMap.Put("a", null);
            dirtyFlagMap.ClearDirtyFlag();

            original = dirtyFlagMap.Put("a", null);

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsNull(original);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.IsNull(dirtyFlagMap["a"]);
        }

        [Test]
        public void Put_KeyAndValue_KeyIsNotFound()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();

            dirtyFlagMap.Put("a", "x");

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("a"));
            Assert.AreEqual("x", dirtyFlagMap["a"]);

            dirtyFlagMap.ClearDirtyFlag();

            dirtyFlagMap.Put("b", null);

            Assert.IsTrue(dirtyFlagMap.Dirty);
            Assert.IsTrue(dirtyFlagMap.ContainsKey("b"));
            Assert.IsNull(dirtyFlagMap["b"]);
        }

        [Test]
        public void Put_KeyAndValue_KeyIsNull()
        {
            var dirtyFlagMap = new DirtyFlagMap<string, string>();
            const string key = null;

            try
            {
                dirtyFlagMap.Put(key, "x");
                Assert.Fail();
            }
            catch (ArgumentNullException ex)
            {
                Assert.AreEqual(nameof(key), ex.ParamName);
            }

            // #1417: should return false
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

#if NETCORE
        [Test]
        public void IReadOnlyDictionary_GetValueOrDefault()
        {
            DirtyFlagMap<string, string> dirtyFlagMap = new()
            {
                { "One", "First Value" },
                { "Two", "Second Value" }
            };

            Assert.Multiple(() =>
            {
                Assert.That(dirtyFlagMap.GetValueOrDefault("One"), Is.EqualTo("First Value"));
                Assert.That(dirtyFlagMap.GetValueOrDefault("Two"), Is.EqualTo("Second Value"));
            });
        }
#endif

        [Test]
        public void IReadOnlyDictionary_Keys()
        {
            DirtyFlagMap<string, string> dirtyFlagMap = new()
            {
                { "One", "First Value" },
                { "Two", "Second Value" }
            };

            IEnumerable<string> keys = ((IReadOnlyDictionary<string, string>) dirtyFlagMap).Keys;

            Assert.Multiple(() =>
            {
                Assert.That(keys.Count(), Is.EqualTo(2));
                Assert.That(keys, Contains.Item("One"));
                Assert.That(keys, Contains.Item("Two"));
            });
        }

        [Test]
        public void IReadOnlyDictionary_Values()
        {
            DirtyFlagMap<string, string> dirtyFlagMap = new()
            {
                { "One", "First Value" },
                { "Two", "Second Value" }
            };

            IEnumerable<string> values = ((IReadOnlyDictionary<string, string>) dirtyFlagMap).Values;

            Assert.Multiple(() =>
            {
                Assert.That(values.Count(), Is.EqualTo(2));
                Assert.That(values, Contains.Item("First Value"));
                Assert.That(values, Contains.Item("Second Value"));
            });
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