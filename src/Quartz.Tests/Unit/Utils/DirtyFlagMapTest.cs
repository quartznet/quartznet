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
using System.Collections;

using NUnit.Framework;

using Quartz.Collection;
using Quartz.Util;

namespace Quartz.Tests.Unit.Simpl
{

	/// <summary>
	/// Unit test for DirtyFlagMap.  These tests focus on making
	/// sure the isDirty flag is set correctly.
	/// </summary>
	[TestFixture]
	public class DirtyFlagMapTest 
	{

		public void TestClear() 
		{
			DirtyFlagMap dirtyFlagMap = new DirtyFlagMap();
			Assert.IsFalse(dirtyFlagMap.Dirty);
        
			dirtyFlagMap.Clear();
			Assert.IsFalse(dirtyFlagMap.Dirty);
			dirtyFlagMap.Put("X", "Y");
			dirtyFlagMap.ClearDirtyFlag();
			dirtyFlagMap.Clear();
			Assert.IsTrue(dirtyFlagMap.Dirty);
		}
    
		public void TestPut() 
		{
			DirtyFlagMap dirtyFlagMap = new DirtyFlagMap();
			dirtyFlagMap.Put("a", "Y");
			Assert.IsTrue(dirtyFlagMap.Dirty);
		}
    
		public void TestRemove() 
		{
			DirtyFlagMap dirtyFlagMap = new DirtyFlagMap();
			dirtyFlagMap.Put("a", "Y");
			dirtyFlagMap.ClearDirtyFlag();
        
			dirtyFlagMap.Remove("b");
			Assert.IsFalse(dirtyFlagMap.Dirty);

			dirtyFlagMap.Remove("a");
			Assert.IsTrue(dirtyFlagMap.Dirty);
		}
    
		[Ignore]
		public void TestEntrySetRemove() 
		{
			DirtyFlagMap dirtyFlagMap = new DirtyFlagMap();
			ISet entrySet = dirtyFlagMap.EntrySet();
			dirtyFlagMap.Remove("a");
			Assert.IsFalse(dirtyFlagMap.Dirty);
			dirtyFlagMap.Put("a", "Y");
			dirtyFlagMap.ClearDirtyFlag();
			entrySet.Remove("b");
			Assert.IsFalse(dirtyFlagMap.Dirty);
			entrySet.Remove(entrySet.First());
			Assert.IsTrue(dirtyFlagMap.Dirty);
		}

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
    
		[Ignore]
		public void TestEntrySetClear() 
		{
			DirtyFlagMap dirtyFlagMap = new DirtyFlagMap();
			ISet entrySet = dirtyFlagMap.EntrySet();
			entrySet.Clear();
			Assert.IsFalse(dirtyFlagMap.Dirty);
			dirtyFlagMap.Put("a", "Y");
			dirtyFlagMap.ClearDirtyFlag();
			entrySet.Clear();
			Assert.IsTrue(dirtyFlagMap.Dirty);
		}        

		[Ignore]
		public void TestEntrySetIterator() 
		{
			DirtyFlagMap dirtyFlagMap = new DirtyFlagMap();
			ISet entrySet = dirtyFlagMap.EntrySet();
			dirtyFlagMap.Put("a", "A");
			dirtyFlagMap.Put("b", "B");
			dirtyFlagMap.Put("c", "C");
			dirtyFlagMap.ClearDirtyFlag();
			DictionaryEntry entryToBeRemoved = (DictionaryEntry) entrySet.First();
			string removedKey = (string) entryToBeRemoved.Key;
			entrySet.Remove(entryToBeRemoved);
			Assert.AreEqual(2, dirtyFlagMap.Count);
			Assert.IsTrue(dirtyFlagMap.Dirty);
			Assert.IsFalse(dirtyFlagMap.Contains(removedKey));
			dirtyFlagMap.ClearDirtyFlag();
			DictionaryEntry entry = (DictionaryEntry)entrySet.First();
			entry.Value = "BB";
			Assert.IsTrue(dirtyFlagMap.Dirty);
			Assert.IsTrue(dirtyFlagMap.ContainsValue("BB"));
		}

		[Ignore]
		public void TestEntrySetToArray() 
		{
			DirtyFlagMap dirtyFlagMap = new DirtyFlagMap();
			ISet entrySet = dirtyFlagMap.EntrySet();
			dirtyFlagMap.Put("a", "A");
			dirtyFlagMap.Put("b", "B");
			dirtyFlagMap.Put("c", "C");
			dirtyFlagMap.ClearDirtyFlag();
			object[] array = (object[]) new ArrayList(entrySet).ToArray(typeof(DictionaryEntry));
			Assert.AreEqual(3, array.Length);
			DictionaryEntry entry = (DictionaryEntry) array[0];
			entry.Value = "BB";
			Assert.IsTrue(dirtyFlagMap.Dirty);
			Assert.IsTrue(dirtyFlagMap.ContainsValue("BB"));
		}

		[Ignore]
		public void TestEntrySetToArrayWithArg() 
		{
			DirtyFlagMap dirtyFlagMap = new DirtyFlagMap();
			ISet entrySet = dirtyFlagMap.EntrySet();
			dirtyFlagMap.Put("a", "A");
			dirtyFlagMap.Put("b", "B");
			dirtyFlagMap.Put("c", "C");
			dirtyFlagMap.ClearDirtyFlag();
			object[] array = (object[]) new ArrayList(entrySet).ToArray(typeof(DictionaryEntry));
			Assert.AreEqual(3, array.Length);
			DictionaryEntry entry = (DictionaryEntry)array[0];
			entry.Value = "BB";
			Assert.IsTrue(dirtyFlagMap.Dirty);
			Assert.IsTrue(dirtyFlagMap.ContainsValue("BB"));
		}
    
		[Ignore]
		public void TestKeySetClear() 
		{
			DirtyFlagMap dirtyFlagMap = new DirtyFlagMap();
			ISet keySet = dirtyFlagMap.KeySet();
			keySet.Clear();
			Assert.IsFalse(dirtyFlagMap.Dirty);
			dirtyFlagMap.Put("a", "Y");
			dirtyFlagMap.ClearDirtyFlag();
			keySet.Clear();
			Assert.IsTrue(dirtyFlagMap.Dirty);
			Assert.AreEqual(0, dirtyFlagMap.Count);
		}    
		
        [Ignore]
		public void TestValuesClear() 
		{
			DirtyFlagMap dirtyFlagMap = new DirtyFlagMap();
			IList values = new ArrayList(dirtyFlagMap.Values);
			values.Clear();
			Assert.IsFalse(dirtyFlagMap.Dirty);
			dirtyFlagMap.Put("a", "Y");
			dirtyFlagMap.ClearDirtyFlag();
			values.Clear();
			Assert.IsTrue(dirtyFlagMap.Dirty);
			Assert.AreEqual(0, dirtyFlagMap.Count);
		}    
	}
}
