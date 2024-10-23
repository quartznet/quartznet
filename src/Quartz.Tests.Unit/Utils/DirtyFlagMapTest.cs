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

using System.Collections;
using System.Runtime.Serialization.Formatters;
using System.Runtime.Serialization.Formatters.Binary;
using Quartz.Util;
using static System.Collections.Generic.CollectionExtensions;

namespace Quartz.Tests.Unit.Utils;

/// <summary>
/// Unit test for DirtyFlagMap.  These tests focus on making
/// sure the isDirty flag is set correctly.
/// </summary>
/// <author>Marko Lahma (.NET)</author>
[TestFixture]
public class DirtyFlagMapTest
{
    [Test]
    public void EmptyAndDirty_V1_CanBeDeserialized()
    {
        var map = Deserialize<DirtyFlagMap<string, int>>("DirtyFlagMap_EmptyAndDirty_V1");

        Assert.That(map, Is.Not.Null);
        Assert.That(map, Is.Empty);
        Assert.That(map.Dirty, Is.True);
    }

    [Test]
    public void EmptyAndNotDirty_V1_CanBeDeserialized()
    {
        var map = Deserialize<DirtyFlagMap<string, int>>("DirtyFlagMap_EmptyAndNotDirty_V1");

        Assert.That(map, Is.Not.Null);
        Assert.That(map, Is.Empty);
        Assert.That(map.Dirty, Is.False);
    }

    [Test]
    public void NotEmptyAndDirty_V1_CanBeDeserialized()
    {
        var map = Deserialize<DirtyFlagMap<string, int>>("DirtyFlagMap_NotEmptyAndDirty_V1");
        Assert.Multiple(() =>
        {
            Assert.That(map, Is.Not.Null);
            Assert.That(map, Has.Count.EqualTo(2));
            Assert.That(map.ContainsKey("A"), Is.True);
            Assert.That(map["A"], Is.EqualTo(2));
            Assert.That(map.ContainsKey("B"), Is.True);
            Assert.That(map["B"], Is.EqualTo(7));
            Assert.That(map.Dirty, Is.True);
        });
    }

    [Test]
    public void NotEmptyAndNotDirty_V1_CanBeDeserialized()
    {
        var map = Deserialize<DirtyFlagMap<string, int>>("DirtyFlagMap_NotEmptyAndNotDirty_V1");
        Assert.Multiple(() =>
        {
            Assert.That(map, Is.Not.Null);
            Assert.That(map, Has.Count.EqualTo(2));
            Assert.That(map.ContainsKey("C"), Is.True);
            Assert.That(map["C"], Is.EqualTo(3));
            Assert.That(map.ContainsKey("F"), Is.True);
            Assert.That(map["F"], Is.EqualTo(1));
            Assert.That(map.Dirty, Is.False);
        });
    }

    [Test]
    public void EmptyAndDirty_CanBeSerializedAndDeserialized()
    {
        var map = new DirtyFlagMap<string, int>();
        map.Add("C", 3);
        map.Clear();

        var deserialized = SerializeAndDeserialize(map);
        Assert.Multiple(() =>
        {
            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized, Is.Empty);
            Assert.That(deserialized.Dirty, Is.True);
        });
    }

    [Test]
    public void EmptyAndNotDirty_CanBeSerializedAndDeserialized()
    {
        var map = new DirtyFlagMap<string, int>();

        var deserialized = SerializeAndDeserialize(map);
        Assert.Multiple(() =>
        {
            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized, Is.Empty);
            Assert.That(deserialized.Dirty, Is.False);
        });
    }

    [Test]
    public void NotEmptyAndDirty_CanBeSerializedAndDeserialized()
    {
        var map = new DirtyFlagMap<string, int>();
        map.Add("A", 2);
        map.Add("B", 7);

        var deserialized = SerializeAndDeserialize(map);
        Assert.Multiple(() =>
        {
            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized, Has.Count.EqualTo(2));
            Assert.That(map.ContainsKey("A"), Is.True);
            Assert.That(map["A"], Is.EqualTo(2));
            Assert.That(map.ContainsKey("B"), Is.True);
            Assert.That(map["B"], Is.EqualTo(7));
            Assert.That(map.Dirty, Is.True);
        });
    }

    [Test]
    public void NotEmptyAndNotDirty_CanBeSerializedAndDeserialized()
    {
        var map = new DirtyFlagMap<string, int>();
        map.Add("C", 3);
        map.Add("F", 1);
        map.ClearDirtyFlag();

        var deserialized = SerializeAndDeserialize(map);
        Assert.Multiple(() =>
        {
            Assert.That(map, Is.Not.Null);
            Assert.That(map, Has.Count.EqualTo(2));
            Assert.That(map.ContainsKey("C"), Is.True);
            Assert.That(map["C"], Is.EqualTo(3));
            Assert.That(map.ContainsKey("F"), Is.True);
            Assert.That(map["F"], Is.EqualTo(1));
            Assert.That(map.Dirty, Is.False);
        });
    }

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
            Assert.That(ex.ParamName, Is.EqualTo(nameof(key)));
        }

        Assert.That(dirtyFlagMap.Dirty, Is.False);
    }

    [Test]
    public void TryGetValue_KeyIsFound_ValueIsNull()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        dirtyFlagMap.Put("a", null);
        dirtyFlagMap.ClearDirtyFlag();

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.TryGetValue("a", out var value), Is.True);
            Assert.That(dirtyFlagMap.Dirty, Is.False);
            Assert.That(value, Is.Null);
        });
    }

    [Test]
    public void TryGetValue_KeyIsFound_ValueIsNotNull()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        dirtyFlagMap.Put("a", "x");
        dirtyFlagMap.ClearDirtyFlag();

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.TryGetValue("a", out var value), Is.True);
            Assert.That(dirtyFlagMap.Dirty, Is.False);
            Assert.That(value, Is.Not.Null);
            Assert.That(value, Is.EqualTo("x"));
        });
    }

    [Test]
    public void TryGetValue_KeyIsNotFound_TValueIsReferenceType()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.TryGetValue("a", out var value), Is.False);
            Assert.That(dirtyFlagMap.Dirty, Is.False);
            Assert.That(value, Is.Null);
        });
    }

    [Test]
    public void TryGetValue_KeyIsNotFound_TValueIsNonNullableStruct()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, int>();

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.TryGetValue("a", out var value), Is.False);
            Assert.That(dirtyFlagMap.Dirty, Is.False);
            Assert.That(value, Is.EqualTo(default(int)));
        });
    }

    [Test]
    public void TryGetValue_KeyIsNotFound_TValueIsNullableStruct()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, int?>();

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.TryGetValue("a", out var value), Is.False);
            Assert.That(dirtyFlagMap.Dirty, Is.False);
            Assert.That(value, Is.Null);
        });
    }

    [Test]
    public void Indexer_Get_KeyIsFound_ValueIsNotNull()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        dirtyFlagMap.Put("a", "x");
        dirtyFlagMap.ClearDirtyFlag();

        var actual = dirtyFlagMap["a"];

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.False);
            Assert.That(actual, Is.Not.Null);
            Assert.That(actual, Is.EqualTo("x"));
        });
    }

    [Test]
    public void Indexer_Get_KeyIsFound_ValueIsNull()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        dirtyFlagMap.Put("a", null);
        dirtyFlagMap.ClearDirtyFlag();

        var actual = dirtyFlagMap["a"];

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.False);
            Assert.That(actual, Is.Null);
        });
    }

    [Test]
    public void Indexer_Get_KeyIsNotFound_TValueIsNonNullableStruct()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, int>();

        // #1417: This should throw a KeyNotFoundException, see commented code below

        var actual = dirtyFlagMap["a"];

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.False);
            Assert.That(actual, Is.EqualTo(default(int)));
        });

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

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.False);
            Assert.That(actual, Is.Null);
        });

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

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.False);
            Assert.That(value, Is.Null);
        });

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
            Assert.That(ex.ParamName, Is.EqualTo(nameof(key)));
        }

        Assert.That(dirtyFlagMap.Dirty, Is.False);
    }

    [Test]
    public void Indexer_Set_KeyIsFound_ValidDoesNotEqualCurrentValue()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        dirtyFlagMap.Put("a", "x");
        dirtyFlagMap.ClearDirtyFlag();

        dirtyFlagMap["a"] = "y";

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.EqualTo("y"));
        });

        dirtyFlagMap.ClearDirtyFlag();

        dirtyFlagMap["a"] = null;

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.Null);
        });

        dirtyFlagMap.ClearDirtyFlag();

        dirtyFlagMap["a"] = "b";

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.EqualTo("b"));
        });
    }

    [Test]
    public void Indexer_Set_KeyIsFound_ValidEqualsCurrentValue()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        dirtyFlagMap.Put("a", "x");
        dirtyFlagMap.Put("b", null);
        dirtyFlagMap.ClearDirtyFlag();

        dirtyFlagMap["a"] = "y";

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.EqualTo("y"));
        });

        dirtyFlagMap.ClearDirtyFlag();

        dirtyFlagMap["b"] = null;

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("b"), Is.True);
            Assert.That(dirtyFlagMap["b"], Is.Null);
        });
    }

    [Test]
    public void Indexer_Set_KeyIsNotFound()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();

        dirtyFlagMap["a"] = "x";

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.EqualTo("x"));
        });

        dirtyFlagMap.ClearDirtyFlag();

        dirtyFlagMap["b"] = null;

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("b"), Is.True);
            Assert.That(dirtyFlagMap["b"], Is.Null);
        });
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
            Assert.That(ex.ParamName, Is.EqualTo(nameof(key)));
        }

        Assert.That(dirtyFlagMap.Dirty, Is.False);
    }

    [Test]
    public void Remove_Key_KeyIsFound()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        dirtyFlagMap.Add("a", "x");
        dirtyFlagMap.ClearDirtyFlag();

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Remove("a"), Is.True);
            Assert.That(dirtyFlagMap.Dirty, Is.True);
        });
    }

    [Test]
    public void Remove_Key_KeyIsNotFound()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        dirtyFlagMap.Add("a", "x");
        dirtyFlagMap.ClearDirtyFlag();

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Remove("x"), Is.False);
            Assert.That(dirtyFlagMap.Dirty, Is.False);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
        });
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
            Assert.That(ex.ParamName, Is.EqualTo(nameof(key)));
        }

        Assert.That(dirtyFlagMap.Dirty, Is.False);
    }

    [Test]
    public void Remove_KeyValuePair_KeyIsFound_ValueDoesNotEqualCurrentValue()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        dirtyFlagMap.Put("a", "x");
        dirtyFlagMap.ClearDirtyFlag();

        Assert.Multiple(() =>
        {
            // #1417: We should not remove entry if values are not equal, see commented code below

            Assert.That(dirtyFlagMap.Remove(new KeyValuePair<string, string>("a", null)), Is.True);
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.False);
        });

        dirtyFlagMap.Clear();
        dirtyFlagMap.Put("a", null);
        dirtyFlagMap.ClearDirtyFlag();

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Remove(new KeyValuePair<string, string>("a", "y")), Is.True);
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.False);
        });

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

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Remove(new KeyValuePair<string, string>("a", "x")), Is.True);
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.False);
        });

        dirtyFlagMap.Put("a", null);
        dirtyFlagMap.ClearDirtyFlag();

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Remove(new KeyValuePair<string, string>("a", null)), Is.True);
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.False);
        });
    }

    [Test]
    public void Remove_KeyValuePair_KeyIsNotFound()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        var kvp = new KeyValuePair<string, string>("a", "x");

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Remove(kvp), Is.False);
            Assert.That(dirtyFlagMap.Dirty, Is.False);
        });
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
            Assert.That(ex.ParamName, Is.EqualTo("key"));
        }

        Assert.That(dirtyFlagMap.Dirty, Is.False);
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

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.False);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.EqualTo("x"));
        });

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

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.False);
            Assert.That(dirtyFlagMap.ContainsKey("b"), Is.True);
            Assert.That(dirtyFlagMap["b"], Is.Null);
        });

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

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.False);
            Assert.That(dirtyFlagMap.ContainsKey("c"), Is.True);
            Assert.That(dirtyFlagMap["c"], Is.EqualTo("z"));
        });
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

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.False);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.EqualTo("x"));
        });

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

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.False);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.Null);
        });
    }

    [Test]
    public void Add_KeyAndValue_KeyIsNotFound()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();

        dirtyFlagMap.Add("a", "x");

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.EqualTo("x"));
        });

        dirtyFlagMap.ClearDirtyFlag();

        dirtyFlagMap.Add("b", null);

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("b"), Is.True);
            Assert.That(dirtyFlagMap["b"], Is.Null);
        });
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
            Assert.That(ex.ParamName, Is.EqualTo(nameof(key)));
        }

        Assert.That(dirtyFlagMap.Dirty, Is.False);
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
            Assert.That(ex.ParamName, Is.EqualTo("key"));
        }

        Assert.That(dirtyFlagMap.Dirty, Is.False);
    }

    [Test]
    public void Add_KeyValuePair_KeyIsNotFound()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        var kvp = new KeyValuePair<string, string>("a", "x");

        dirtyFlagMap.Add(kvp);

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.EqualTo("x"));
        });
    }

    [Test]
    public void Add_KeyValuePair_KeyIsFound_ValueDoesNotEqualCurrentValue()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        dirtyFlagMap.Put("a", "x");
        dirtyFlagMap.ClearDirtyFlag();

        // #1417: We should throw ArgumentException, see commented code below

        dirtyFlagMap.Add(new KeyValuePair<string, string>("a", "y"));

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.EqualTo("y"));
        });

        dirtyFlagMap.ClearDirtyFlag();

        dirtyFlagMap.Add(new KeyValuePair<string, string>("a", null));

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.Null);
        });

        dirtyFlagMap.ClearDirtyFlag();

        dirtyFlagMap.Add(new KeyValuePair<string, string>("a", "z"));

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.EqualTo("z"));
        });

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

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.EqualTo("x"));
        });

        dirtyFlagMap.Clear();
        dirtyFlagMap.Put("a", null);
        dirtyFlagMap.ClearDirtyFlag();

        dirtyFlagMap.Add(new KeyValuePair<string, string>("a", null));

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.Null);
        });
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
            Assert.That(ex.ParamName, Is.EqualTo("key"));
        }

        Assert.That(dirtyFlagMap.Dirty, Is.False);
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

        Assert.That(dirtyFlagMap.Dirty, Is.False);

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

        Assert.That(dirtyFlagMap.Dirty, Is.False);

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

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.EqualTo("x"));
        });
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

        Assert.That(dirtyFlagMap.Dirty, Is.False);

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

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.Null);
        });
    }

    [Test]
    public void IDictionary_Add_KeyAndValue_KeyIsNotFound_ValueIsNull_TValueIsReferenceType()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        object key = "a";

        ((IDictionary) dirtyFlagMap).Add(key, null);

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.Null);
        });
    }

    [Test]
    public void IDictionary_Add_KeyAndValue_KeyIsFound_ValueDoesNotEqualCurrentValue()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        dirtyFlagMap.Add("a", "x");
        dirtyFlagMap.ClearDirtyFlag();

        // #1417: We should throw ArgumentException, see commented code below

        ((IDictionary) dirtyFlagMap).Add("a", "y");

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.EqualTo("y"));
        });

        dirtyFlagMap.ClearDirtyFlag();

        ((IDictionary) dirtyFlagMap).Add("a", null);

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.Null);
        });

        dirtyFlagMap.ClearDirtyFlag();

        ((IDictionary) dirtyFlagMap).Add("a", "z");

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.EqualTo("z"));
        });

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

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Remove(new KeyValuePair<string, string>("a", "x")), Is.True);
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.False);
        });

        dirtyFlagMap.Clear();
        dirtyFlagMap.Put("a", null);
        dirtyFlagMap.ClearDirtyFlag();

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Remove(new KeyValuePair<string, string>("a", null)), Is.True);
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.False);
        });
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

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.False);
            Assert.That(actual, Is.Not.Null);
        });
        Assert.That(actual, Is.EqualTo("x"));
    }

    [Test]
    public void IDictionary_Indexer_Get_KeyIsFound_ValueIsNull()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        dirtyFlagMap.Put("a", null);
        dirtyFlagMap.ClearDirtyFlag();

        var actual = ((IDictionary) dirtyFlagMap)["a"];

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.False);
            Assert.That(actual, Is.Null);
        });
    }

    [Test]
    public void IDictionary_Indexer_Get_KeyIsNotFound_TValueIsNonNullableStruct()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, int>();

        // #1417: This should throw a KeyNotFoundException, see commented code below

        var actual = ((IDictionary) dirtyFlagMap)["a"];

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.False);
            Assert.That(actual, Is.Not.Null);
            Assert.That(actual, Is.EqualTo(default(int)));
        });

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

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.False);
            Assert.That(actual, Is.Null);
        });

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

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.False);
            Assert.That(value, Is.Null);
        });

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
            Assert.That(ex.ParamName, Is.EqualTo(nameof(key)));
        }

        Assert.That(dirtyFlagMap.Dirty, Is.False);
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

        Assert.That(dirtyFlagMap.Dirty, Is.False);

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

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.EqualTo("y"));
        });

        dirtyFlagMap.ClearDirtyFlag();

        ((IDictionary) dirtyFlagMap)["a"] = null;

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.Null);
        });

        dirtyFlagMap.ClearDirtyFlag();

        ((IDictionary) dirtyFlagMap)["a"] = "b";

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.EqualTo("b"));
        });
    }

    [Test]
    public void IDictionary_Indexer_Set_KeyIsFound_ValidEqualsCurrentValue()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        dirtyFlagMap.Put("a", "x");
        dirtyFlagMap.Put("b", null);
        dirtyFlagMap.ClearDirtyFlag();

        ((IDictionary) dirtyFlagMap)["a"] = "y";

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.EqualTo("y"));
        });

        dirtyFlagMap.ClearDirtyFlag();

        ((IDictionary) dirtyFlagMap)["b"] = null;

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("b"), Is.True);
            Assert.That(dirtyFlagMap["b"], Is.Null);
        });
    }

    [Test]
    public void IDictionary_Indexer_Set_KeyIsNotFound()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();

        ((IDictionary) dirtyFlagMap)["a"] = "x";

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.EqualTo("x"));
        });

        dirtyFlagMap.ClearDirtyFlag();

        ((IDictionary) dirtyFlagMap)["b"] = null;

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("b"), Is.True);
            Assert.That(dirtyFlagMap["b"], Is.Null);
        });
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
            Assert.That(ex.ParamName, Is.EqualTo(nameof(key)));
        }

        Assert.That(dirtyFlagMap.Dirty, Is.False);
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

        Assert.That(dirtyFlagMap.Dirty, Is.False);

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
            Assert.That(ex.ParamName, Is.EqualTo("key"));
        }

        Assert.That(dirtyFlagMap.Dirty, Is.False);
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

        Assert.That(dirtyFlagMap.Dirty, Is.False);

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

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.False);
        });
    }

    [Test]
    public void IDictionary_Remove_KeyIsNotFound()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        object key = "a";

        ((IDictionary) dirtyFlagMap).Remove(key);

        Assert.That(dirtyFlagMap.Dirty, Is.False);
    }

    [Test]
    public void Contains_Key_KeyCannotBeAssignedToTKey()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        dirtyFlagMap.Add("a", "x");
        dirtyFlagMap.ClearDirtyFlag();

        Assert.Multiple(() =>
        {
            Assert.That(((IDictionary) dirtyFlagMap).Contains(5), Is.False);
            Assert.That(dirtyFlagMap.Dirty, Is.False);
        });
    }

    [Test]
    public void Contains_Key_KeyIsFound()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        dirtyFlagMap.Put("a", "x");
        dirtyFlagMap.ClearDirtyFlag();

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Contains("a"), Is.True);
            Assert.That(dirtyFlagMap.Dirty, Is.False);
        });
    }

    [Test]
    public void Contains_Key_KeyIsNotFound()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        object key = "a";

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Contains(key), Is.False);
            Assert.That(dirtyFlagMap.Dirty, Is.False);
        });
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
            Assert.That(ex.ParamName, Is.EqualTo(nameof(key)));
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
            Assert.That(ex.ParamName, Is.EqualTo("key"));
        }
    }

    [Test]
    public void Contains_KeyValuePair_KeyIsNotFound()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Contains(new KeyValuePair<string, string>("a", "x")), Is.False);
            Assert.That(dirtyFlagMap.Dirty, Is.False);
        });
    }

    [Test]
    public void Contains_KeyValuePair_KeyIsFound_ValueDoesNotEqualCurrentValue()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        dirtyFlagMap.Add("a", "x");
        dirtyFlagMap.ClearDirtyFlag();

        Assert.Multiple(() =>
        {
            // #1417: this should return false
            Assert.That(dirtyFlagMap.Contains(new KeyValuePair<string, string>("a", "y")), Is.True);
            Assert.That(dirtyFlagMap.Dirty, Is.False);

            // #1417: this should return false
            Assert.That(dirtyFlagMap.Contains(new KeyValuePair<string, string>("a", null)), Is.True);
            Assert.That(dirtyFlagMap.Dirty, Is.False);
        });
    }

    [Test]
    public void Contains_KeyValuePair_KeyIsFound_ValueEqualsCurrentValue()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        dirtyFlagMap.Add("a", "x");
        dirtyFlagMap.ClearDirtyFlag();

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Contains(new KeyValuePair<string, string>("a", "x")), Is.True);
            Assert.That(dirtyFlagMap.Dirty, Is.False);
        });

        dirtyFlagMap.Add("b", null);
        dirtyFlagMap.ClearDirtyFlag();

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Contains(new KeyValuePair<string, string>("b", null)), Is.True);
            Assert.That(dirtyFlagMap.Dirty, Is.False);
        });
    }

    [Test]
    public void ContainsKey_KeyIsFound()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        dirtyFlagMap.Put("a", "x");
        dirtyFlagMap.ClearDirtyFlag();

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap.Dirty, Is.False);
        });
    }

    [Test]
    public void ContainsKey_KeyIsNotFound()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        dirtyFlagMap.Put("a", "x");
        dirtyFlagMap.ClearDirtyFlag();

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.ContainsKey("x"), Is.False);
            Assert.That(dirtyFlagMap.Dirty, Is.False);
        });
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
            Assert.That(ex.ParamName, Is.EqualTo(nameof(key)));
        }
    }

    [Test]
    public void ContainsValue_ValueIsFound()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        dirtyFlagMap.Put("a", "x");
        dirtyFlagMap.Put("b", null);
        dirtyFlagMap.ClearDirtyFlag();

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.ContainsValue("x"), Is.True);
            Assert.That(dirtyFlagMap.Dirty, Is.False);

            Assert.That(dirtyFlagMap.ContainsValue(null), Is.True);
            Assert.That(dirtyFlagMap.Dirty, Is.False);
        });
    }

    [Test]
    public void ContainsValue_ValueIsNotFound()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        dirtyFlagMap.Put("a", "x");
        dirtyFlagMap.ClearDirtyFlag();

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.ContainsValue("y"), Is.False);
            Assert.That(dirtyFlagMap.Dirty, Is.False);

            Assert.That(dirtyFlagMap.ContainsValue("a"), Is.False);
            Assert.That(dirtyFlagMap.Dirty, Is.False);

            Assert.That(dirtyFlagMap.ContainsValue(null), Is.False);
            Assert.That(dirtyFlagMap.Dirty, Is.False);
        });
    }

    [Test]
    public void TestClear()
    {
        DirtyFlagMap<string, string> dirtyFlagMap = new DirtyFlagMap<string, string>();
        Assert.That(dirtyFlagMap.Dirty, Is.False);

        dirtyFlagMap.Clear();
        Assert.That(dirtyFlagMap.Dirty, Is.False);
        dirtyFlagMap.Put("X", "Y");
        dirtyFlagMap.ClearDirtyFlag();
        dirtyFlagMap.Clear();
        Assert.That(dirtyFlagMap.Dirty, Is.True);
    }

    [Test]
    public void Put_KeyAndValue_KeyIsFound_ValueDoesNotEqualCurrentValue_TValueIsNonNullableStruct()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, int>();
        dirtyFlagMap.Add("a", 5);
        dirtyFlagMap.ClearDirtyFlag();

        var original = dirtyFlagMap.Put("a", 4);

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(original, Is.Not.Null);
            Assert.That(original, Is.EqualTo(5));
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.EqualTo(4));
        });

        dirtyFlagMap.ClearDirtyFlag();

        original = dirtyFlagMap.Put("a", 0);

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(original, Is.Not.Null);
            Assert.That(original, Is.EqualTo(4));
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.EqualTo(0));
        });

        dirtyFlagMap.ClearDirtyFlag();

        original = dirtyFlagMap.Put("a", 7);

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(original, Is.Not.Null);
            Assert.That(original, Is.EqualTo(0));
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.EqualTo(7));
        });
    }

    [Test]
    public void Put_KeyAndValue_KeyIsFound_ValueDoesNotEqualCurrentValue_TValueIsNullableStruct()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, int?>();
        dirtyFlagMap.Add("a", 5);
        dirtyFlagMap.ClearDirtyFlag();

        var original = dirtyFlagMap.Put("a", 4);

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(original, Is.Not.Null);
            Assert.That(original, Is.EqualTo(5));
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.EqualTo(4));
        });

        dirtyFlagMap.ClearDirtyFlag();

        original = dirtyFlagMap.Put("a", null);

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(original, Is.Not.Null);
            Assert.That(original, Is.EqualTo(4));
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.Null);
        });

        dirtyFlagMap.ClearDirtyFlag();

        original = dirtyFlagMap.Put("a", 7);

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(original, Is.Null);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.EqualTo(7));
        });
    }

    [Test]
    public void Put_KeyAndValue_KeyIsFound_ValueDoesNotEqualCurrentValue_TValueIsReferenceType()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        dirtyFlagMap.Add("a", "x");
        dirtyFlagMap.ClearDirtyFlag();

        var original = dirtyFlagMap.Put("a", "y");

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(original, Is.Not.Null);
            Assert.That(original, Is.EqualTo("x"));
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.EqualTo("y"));
        });

        dirtyFlagMap.ClearDirtyFlag();

        original = dirtyFlagMap.Put("a", null);

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(original, Is.Not.Null);
            Assert.That(original, Is.EqualTo("y"));
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.Null);
        });

        dirtyFlagMap.ClearDirtyFlag();

        original = dirtyFlagMap.Put("a", "z");

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(original, Is.Null);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.EqualTo("z"));
        });
    }

    [Test]
    public void Put_KeyAndValue_KeyIsFound_ValueEqualsCurrentValue_TValueIsNonNullableStruct()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, int>();
        dirtyFlagMap.Add("a", 5);
        dirtyFlagMap.ClearDirtyFlag();

        var original = dirtyFlagMap.Put("a", 5);

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(original, Is.Not.Null);
            Assert.That(original, Is.EqualTo(5));
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.EqualTo(5));
        });

        dirtyFlagMap.Clear();
        dirtyFlagMap.Put("a", 0);
        dirtyFlagMap.ClearDirtyFlag();

        original = dirtyFlagMap.Put("a", 0);

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(original, Is.Not.Null);
            Assert.That(original, Is.EqualTo(0));
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.EqualTo(0));
        });
    }

    [Test]
    public void Put_KeyAndValue_KeyIsFound_ValueEqualsCurrentValue_TValueIsNullableStruct()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, int?>();
        dirtyFlagMap.Add("a", 5);
        dirtyFlagMap.ClearDirtyFlag();

        var original = dirtyFlagMap.Put("a", 5);

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(original, Is.Not.Null);
            Assert.That(original, Is.EqualTo(5));
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.EqualTo(5));
        });

        dirtyFlagMap.Clear();
        dirtyFlagMap.Put("a", null);
        dirtyFlagMap.ClearDirtyFlag();

        original = dirtyFlagMap.Put("a", null);

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(original, Is.Null);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.Null);
        });
    }

    [Test]
    public void Put_KeyAndValue_KeyIsFound_ValueEqualsCurrentValue_TValueIsReferenceType()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        dirtyFlagMap.Add("a", "x");
        dirtyFlagMap.ClearDirtyFlag();

        var original = dirtyFlagMap.Put("a", "x");

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(original, Is.Not.Null);
            Assert.That(original, Is.EqualTo("x"));
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.EqualTo("x"));
        });

        dirtyFlagMap.Clear();
        dirtyFlagMap.Put("a", null);
        dirtyFlagMap.ClearDirtyFlag();

        original = dirtyFlagMap.Put("a", null);

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(original, Is.Null);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.Null);
        });
    }

    [Test]
    public void Put_KeyAndValue_KeyIsNotFound()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();

        dirtyFlagMap.Put("a", "x");

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.EqualTo("x"));
        });

        dirtyFlagMap.ClearDirtyFlag();

        dirtyFlagMap.Put("b", null);

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("b"), Is.True);
            Assert.That(dirtyFlagMap["b"], Is.Null);
        });
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
            Assert.That(ex.ParamName, Is.EqualTo(nameof(key)));
        }

        Assert.That(dirtyFlagMap.Dirty, Is.False);
    }

    [Test]
    public void TestRemove()
    {
        DirtyFlagMap<string, string> dirtyFlagMap = new DirtyFlagMap<string, string>();
        dirtyFlagMap.Put("a", "Y");
        dirtyFlagMap.ClearDirtyFlag();

        dirtyFlagMap.Remove("b");
        Assert.That(dirtyFlagMap.Dirty, Is.False);

        dirtyFlagMap.Remove("a");
        Assert.That(dirtyFlagMap.Dirty, Is.True);
    }

    [Test]
    public void ICollection_SyncRoot()
    {
        var dirtyFlagMap1 = new DirtyFlagMap<string, string>();
        var collection1 = (ICollection) dirtyFlagMap1;

        var syncRoot1 = collection1.SyncRoot;
        Assert.Multiple(() =>
        {
            Assert.That(syncRoot1, Is.Not.Null);
            Assert.That(collection1.SyncRoot, Is.SameAs(syncRoot1));
            Assert.That(syncRoot1.GetType(), Is.EqualTo(typeof(object)));
        });

        var dirtyFlagMap2 = new DirtyFlagMap<string, string>();
        var collection2 = (ICollection) dirtyFlagMap2;

        var syncRoot2 = collection2.SyncRoot;
        Assert.Multiple(() =>
        {
            Assert.That(syncRoot2, Is.Not.Null);
            Assert.That(collection2.SyncRoot, Is.SameAs(syncRoot2));
            Assert.That(syncRoot2.GetType(), Is.EqualTo(typeof(object)));
            Assert.That(syncRoot2, Is.Not.SameAs(syncRoot1));
        });
    }

    [Test]
    public void ICollectionKeyValuePairOfTKeyAndTValue_IsReadOnly()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        var collection = (ICollection<KeyValuePair<string, string>>) dirtyFlagMap;
        Assert.That(collection.IsReadOnly, Is.False);
    }

    [Test]
    public void IDictionary_IsReadOnly()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        var dictionary = (IDictionary) dirtyFlagMap;
        Assert.That(dictionary.IsReadOnly, Is.False);
    }

    [Test]
    public void IDictionary_IsSynchronized()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        var dictionary = (IDictionary) dirtyFlagMap;
        Assert.That(dictionary.IsSynchronized, Is.False);
    }

    [Test]
    public void IDictionary_IsFixedSize()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        var dictionary = (IDictionary) dirtyFlagMap;
        Assert.That(dictionary.IsFixedSize, Is.False);
    }

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
            Assert.That(keys.Count, Is.EqualTo(2));
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
            Assert.That(values.Count, Is.EqualTo(2));
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

    private static T SerializeAndDeserialize<T>(T value)
    {
        var formatter = new BinaryFormatter();

        using (var ms = new MemoryStream())
        {
            formatter.Serialize(ms, value);

            ms.Position = 0;

            return (T) formatter.Deserialize(ms);
        }
    }

    private static T Deserialize<T>(string name)
    {
#pragma warning disable SYSLIB0050
        using var fs = File.OpenRead(Path.Combine("Serialized", name + ".ser"));
        BinaryFormatter binaryFormatter = new BinaryFormatter();
        binaryFormatter.AssemblyFormat = FormatterAssemblyStyle.Simple;
        return (T) binaryFormatter.Deserialize(fs);
#pragma warning restore SYSLIB0050
    }
}