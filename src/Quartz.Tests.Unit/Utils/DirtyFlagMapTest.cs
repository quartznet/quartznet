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

using Quartz.Util;

namespace Quartz.Tests.Unit.Utils;

/// <summary>
/// Unit test for DirtyFlagMap.  These tests focus on making
/// sure the isDirty flag is set correctly.
/// </summary>
/// <author>Marko Lahma (.NET)</author>
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
            Assert.That(ex.ParamName, Is.EqualTo(nameof(key)));
        }

        Assert.That(dirtyFlagMap.Dirty, Is.False);
    }

    [Test]
    public void TryGetValue_KeyIsFound_ValueIsNull()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        dirtyFlagMap["a"] = null;
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
        dirtyFlagMap["a"] = "x";
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
        dirtyFlagMap["a"] = "x";
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
        dirtyFlagMap["a"] = null;
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

        try
        {
            var actual = dirtyFlagMap["a"];
            Assert.Fail("Should have thrown, but returned " + actual);
        }
        catch (KeyNotFoundException)
        {
        }

        Assert.That(dirtyFlagMap.Dirty, Is.False);
    }

    [Test]
    public void Indexer_Get_KeyIsNotFound_TValueIsNullableStruct()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, int?>();

        try
        {
            var actual = dirtyFlagMap["a"];
            Assert.Fail("Should have thrown, but returned " + actual);
        }
        catch (KeyNotFoundException)
        {
        }

        Assert.That(dirtyFlagMap.Dirty, Is.False);
    }

    [Test]
    public void Indexer_Get_KeyIsNotFound_TValueIsReferenceType()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();

        try
        {
            var actual = dirtyFlagMap["a"];
            Assert.Fail("Should have thrown, but returned " + actual);
        }
        catch (KeyNotFoundException)
        {
        }

        Assert.That(dirtyFlagMap.Dirty, Is.False);
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
        dirtyFlagMap["a"] = "x";
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
        dirtyFlagMap["a"] = "x";
        dirtyFlagMap["b"] = null;
        dirtyFlagMap.ClearDirtyFlag();

        dirtyFlagMap["a"] = "x";

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.False);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
            Assert.That(dirtyFlagMap["a"], Is.EqualTo("x"));
        });

        dirtyFlagMap.ClearDirtyFlag();

        dirtyFlagMap["b"] = null;

        Assert.Multiple(() =>
        {
            Assert.That(dirtyFlagMap.Dirty, Is.False);
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
        dirtyFlagMap["a"] = "x";
        dirtyFlagMap.ClearDirtyFlag();

        Assert.That(((ICollection<KeyValuePair<string, string>>) dirtyFlagMap).Remove(new KeyValuePair<string, string>("a", null)), Is.False);
        Assert.That(dirtyFlagMap.Dirty, Is.False);
        Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
        Assert.That(dirtyFlagMap["a"], Is.EqualTo("x"));

        dirtyFlagMap.ClearDirtyFlag();

        Assert.That(((ICollection<KeyValuePair<string, string>>) dirtyFlagMap).Remove(new KeyValuePair<string, string>("a", "y")), Is.False);
        Assert.That(dirtyFlagMap.Dirty, Is.False);
        Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
        Assert.That(dirtyFlagMap["a"], Is.EqualTo("x"));

        dirtyFlagMap.Clear();
        dirtyFlagMap["a"] = null;
        dirtyFlagMap.ClearDirtyFlag();

        Assert.That(((ICollection<KeyValuePair<string, string>>) dirtyFlagMap).Remove(new KeyValuePair<string, string>("a", "z")), Is.False);
        Assert.That(dirtyFlagMap.Dirty, Is.False);
        Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
    }

    [Test]
    public void Remove_KeyValuePair_KeyIsFound_ValueEqualsCurrentValue()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        dirtyFlagMap["a"] = "x";
        dirtyFlagMap.ClearDirtyFlag();

        Assert.Multiple(() =>
        {
            Assert.That(((ICollection<KeyValuePair<string, string>>) dirtyFlagMap).Remove(new KeyValuePair<string, string>("a", "x")), Is.True);
            Assert.That(dirtyFlagMap.Dirty, Is.True);
            Assert.That(dirtyFlagMap.ContainsKey("a"), Is.False);
        });

        dirtyFlagMap["a"] = null;
        dirtyFlagMap.ClearDirtyFlag();

        Assert.Multiple(() =>
        {
            Assert.That(((ICollection<KeyValuePair<string, string>>) dirtyFlagMap).Remove(new KeyValuePair<string, string>("a", null)), Is.True);
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
            Assert.That(((ICollection<KeyValuePair<string, string>>) dirtyFlagMap).Remove(kvp), Is.False);
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
            ((ICollection<KeyValuePair<string, string>>) dirtyFlagMap).Remove(kvp);
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
        dirtyFlagMap["a"] = "x";
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

        dirtyFlagMap["b"] = null;
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

        dirtyFlagMap["c"] = "z";
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
        dirtyFlagMap["a"] = "x";
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
        dirtyFlagMap["a"] = null;
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
            ((ICollection<KeyValuePair<string, string>>) dirtyFlagMap).Add(kvp);
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

        ((ICollection<KeyValuePair<string, string>>) dirtyFlagMap).Add(kvp);

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
        dirtyFlagMap["a"] = "x";
        dirtyFlagMap.ClearDirtyFlag();

        try
        {
            ((ICollection<KeyValuePair<string, string>>) dirtyFlagMap).Add(new KeyValuePair<string, string>("a", "y"));
            Assert.Fail();
        }
        catch (ArgumentException)
        {
            // An item with the same key has already been added. Key: a
        }

        Assert.That(dirtyFlagMap.Dirty, Is.False);
        Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
        Assert.That(dirtyFlagMap["a"], Is.EqualTo("x"));

        dirtyFlagMap.ClearDirtyFlag();

        try
        {
            ((ICollection<KeyValuePair<string, string>>) dirtyFlagMap).Add(new KeyValuePair<string, string>("a", null));
            Assert.Fail();
        }
        catch (ArgumentException)
        {
            // An item with the same key has already been added. Key: a
        }

        Assert.That(dirtyFlagMap.Dirty, Is.False);
        Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
        Assert.That(dirtyFlagMap["a"], Is.EqualTo("x"));

        dirtyFlagMap.Clear();
        dirtyFlagMap.Add("a", null);
        dirtyFlagMap.ClearDirtyFlag();

        try
        {
            ((ICollection<KeyValuePair<string, string>>) dirtyFlagMap).Add(new KeyValuePair<string, string>("a", "z"));
            Assert.Fail();
        }
        catch (ArgumentException)
        {
            // An item with the same key has already been added. Key: a
        }

        Assert.That(dirtyFlagMap.Dirty, Is.False);
        Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
        Assert.That(dirtyFlagMap["a"], Is.Null);
    }

    [Test]
    public void Add_KeyValuePair_KeyIsFound_ValueEqualsCurrentValue()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        dirtyFlagMap["a"] = "x";
        dirtyFlagMap.ClearDirtyFlag();

        try
        {
            ((ICollection<KeyValuePair<string, string>>) dirtyFlagMap).Add(new KeyValuePair<string, string>("a", "x"));
            Assert.Fail();
        }
        catch (ArgumentException)
        {
            // An item with the same key has already been added. Key: a
        }

        Assert.That(dirtyFlagMap.Dirty, Is.False);
        Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
        Assert.That(dirtyFlagMap["a"], Is.EqualTo("x"));

        dirtyFlagMap.Clear();
        dirtyFlagMap["a"] = null;
        dirtyFlagMap.ClearDirtyFlag();

        try
        {
            ((ICollection<KeyValuePair<string, string>>) dirtyFlagMap).Add(new KeyValuePair<string, string>("a", null));
            Assert.Fail();
        }
        catch (ArgumentException)
        {
            // An item with the same key has already been added. Key: a
        }

        Assert.That(dirtyFlagMap.Dirty, Is.False);
        Assert.That(dirtyFlagMap.ContainsKey("a"), Is.True);
        Assert.That(dirtyFlagMap["a"], Is.Null);
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
            Assert.That(((ICollection<KeyValuePair<string, string>>) dirtyFlagMap).Contains(new KeyValuePair<string, string>("a", "x")), Is.False);
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
            Assert.That(((ICollection<KeyValuePair<string, string>>) dirtyFlagMap).Contains(new KeyValuePair<string, string>("a", "y")), Is.False);
            Assert.That(dirtyFlagMap.Dirty, Is.False);

            Assert.That(((ICollection<KeyValuePair<string, string>>) dirtyFlagMap).Contains(new KeyValuePair<string, string>("a", null)), Is.False);
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
            Assert.That(((ICollection<KeyValuePair<string, string>>) dirtyFlagMap).Contains(new KeyValuePair<string, string>("a", "x")), Is.True);
            Assert.That(dirtyFlagMap.Dirty, Is.False);
        });

        dirtyFlagMap.Add("b", null);
        dirtyFlagMap.ClearDirtyFlag();

        Assert.Multiple(() =>
        {
            Assert.That(((ICollection<KeyValuePair<string, string>>) dirtyFlagMap).Contains(new KeyValuePair<string, string>("b", null)), Is.True);
            Assert.That(dirtyFlagMap.Dirty, Is.False);
        });
    }

    [Test]
    public void ContainsKey_KeyIsFound()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        dirtyFlagMap["a"] = "x";
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
        dirtyFlagMap["a"] = "x";
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
        dirtyFlagMap["a"] = "x";
        dirtyFlagMap["b"] = null;
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
        dirtyFlagMap["a"] = "x";
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
        dirtyFlagMap["X"] = "Y";
        dirtyFlagMap.ClearDirtyFlag();
        dirtyFlagMap.Clear();
        Assert.That(dirtyFlagMap.Dirty, Is.True);
    }

    [Test]
    public void TestRemove()
    {
        DirtyFlagMap<string, string> dirtyFlagMap = new DirtyFlagMap<string, string>();
        dirtyFlagMap["a"] = "Y";
        dirtyFlagMap.ClearDirtyFlag();

        dirtyFlagMap.Remove("b");
        Assert.That(dirtyFlagMap.Dirty, Is.False);

        dirtyFlagMap.Remove("a");
        Assert.That(dirtyFlagMap.Dirty, Is.True);
    }

    [Test]
    public void ICollectionKeyValuePairOfTKeyAndTValue_IsReadOnly()
    {
        var dirtyFlagMap = new DirtyFlagMap<string, string>();
        var collection = (ICollection<KeyValuePair<string, string>>) dirtyFlagMap;
        Assert.That(collection.IsReadOnly, Is.False);
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

        var keys = ((IReadOnlyDictionary<string, string>) dirtyFlagMap).Keys;

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
}
