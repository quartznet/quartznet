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