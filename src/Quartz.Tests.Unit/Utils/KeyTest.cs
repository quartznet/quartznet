using System.Runtime.Serialization.Formatters;
using System.Runtime.Serialization.Formatters.Binary;

using Quartz.Util;

namespace Quartz.Tests.Unit.Utils;

/// <summary>
/// Unit tests for Key&lt;T&gt;
/// </summary>
/// <author>Gert Driesen</author>
[TestFixture]
public class KeyTest
{
    private static readonly byte[] _serializedKeyStringWithNameAndGroup = new byte[]
    {
        0x00, 0x01, 0x00, 0x00, 0x00, 0xff, 0xff, 0xff, 0xff, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x0c, 0x02, 0x00, 0x00, 0x00, 0x49, 0x51, 0x75, 0x61, 0x72, 0x74, 0x7a, 0x2c,
        0x20, 0x56, 0x65, 0x72, 0x73, 0x69, 0x6f, 0x6e, 0x3d, 0x33, 0x2e, 0x33, 0x2e, 0x33, 0x2e,
        0x30, 0x2c, 0x20, 0x43, 0x75, 0x6c, 0x74, 0x75, 0x72, 0x65, 0x3d, 0x6e, 0x65, 0x75, 0x74,
        0x72, 0x61, 0x6c, 0x2c, 0x20, 0x50, 0x75, 0x62, 0x6c, 0x69, 0x63, 0x4b, 0x65, 0x79, 0x54,
        0x6f, 0x6b, 0x65, 0x6e, 0x3d, 0x66, 0x36, 0x62, 0x38, 0x63, 0x39, 0x38, 0x61, 0x34, 0x30,
        0x32, 0x63, 0x63, 0x38, 0x61, 0x34, 0x05, 0x01, 0x00, 0x00, 0x00, 0x6f, 0x51, 0x75, 0x61,
        0x72, 0x74, 0x7a, 0x2e, 0x55, 0x74, 0x69, 0x6c, 0x2e, 0x4b, 0x65, 0x79, 0x60, 0x31, 0x5b,
        0x5b, 0x53, 0x79, 0x73, 0x74, 0x65, 0x6d, 0x2e, 0x53, 0x74, 0x72, 0x69, 0x6e, 0x67, 0x2c,
        0x20, 0x6d, 0x73, 0x63, 0x6f, 0x72, 0x6c, 0x69, 0x62, 0x2c, 0x20, 0x56, 0x65, 0x72, 0x73,
        0x69, 0x6f, 0x6e, 0x3d, 0x34, 0x2e, 0x30, 0x2e, 0x30, 0x2e, 0x30, 0x2c, 0x20, 0x43, 0x75,
        0x6c, 0x74, 0x75, 0x72, 0x65, 0x3d, 0x6e, 0x65, 0x75, 0x74, 0x72, 0x61, 0x6c, 0x2c, 0x20,
        0x50, 0x75, 0x62, 0x6c, 0x69, 0x63, 0x4b, 0x65, 0x79, 0x54, 0x6f, 0x6b, 0x65, 0x6e, 0x3d,
        0x62, 0x37, 0x37, 0x61, 0x35, 0x63, 0x35, 0x36, 0x31, 0x39, 0x33, 0x34, 0x65, 0x30, 0x38,
        0x39, 0x5d, 0x5d, 0x02, 0x00, 0x00, 0x00, 0x04, 0x6e, 0x61, 0x6d, 0x65, 0x05, 0x67, 0x72,
        0x6f, 0x75, 0x70, 0x01, 0x01, 0x02, 0x00, 0x00, 0x00, 0x06, 0x03, 0x00, 0x00, 0x00, 0x01,
        0x41, 0x06, 0x04, 0x00, 0x00, 0x00, 0x01, 0x42, 0x0b
    };

    [Test]
    public void Ctor_Name_IsNotNull()
    {
        const string name = "X";

        var key = new Key<string>(name);

        Assert.Multiple(() =>
        {
            Assert.That(key.Name, Is.SameAs(name));
            Assert.That(key.Group, Is.SameAs(Key<string>.DefaultGroup));
        });
    }

    [Test]
    public void Ctor_Name_NameIsNull()
    {
        const string name = null;

        var actualException = Assert.Throws<ArgumentNullException>(() => new Key<string>(name));
        Assert.That(actualException.ParamName, Is.EqualTo("name"));
    }

    [Test]
    public void Ctor_NameAndGroup()
    {
        const string name = "Name";
        const string group = "Group";

        var key = new Key<string>(name, group);

        Assert.Multiple(() =>
        {
            Assert.That(key.Name, Is.SameAs(name));
            Assert.That(key.Group, Is.SameAs(group));
        });
    }

    [Test]
    public void Ctor_NameAndGroup_NameIsNull()
    {
        const string name = null;
        const string group = "Group";

        var actualException = Assert.Throws<ArgumentNullException>(() => new Key<string>(name, group));
        Assert.That(actualException.ParamName, Is.EqualTo("name"));
    }

    [Test]
    public void Ctor_NameAndGroup_GroupIsNull()
    {
        const string name = "Name";
        const string group = null;

        var actualException = Assert.Throws<ArgumentNullException>(() => new Key<string>(name, group));
        Assert.That(actualException.ParamName, Is.EqualTo("group"));
    }

    [Test]
    public void DefaultGroupIsDefault()
    {
        Assert.That(Key<string>.DefaultGroup, Is.EqualTo("DEFAULT"));
    }

    [Test]
    public void CompareTo_SameReference_GroupIsDefault_Implicit()
    {
        var key = new Key<string>("A");

        Assert.That(key.CompareTo(key), Is.EqualTo(0));
    }

    [Test]
    public void CompareTo_SameReference_GroupIsDefault_Explicit_Literal()
    {
        var key = new Key<string>("A", CreateUniqueReference(Key<string>.DefaultGroup));

        Assert.That(key.CompareTo(key), Is.EqualTo(0));
    }

    [Test]
    public void CompareTo_SameReference_GroupIsDefault_Explicit_SameReference()
    {
        var key = new Key<string>("A", Key<string>.DefaultGroup);

        Assert.That(key.CompareTo(key), Is.EqualTo(0));
    }

    [Test]
    public void CompareTo_SameReference_GroupIsNotDefault()
    {
        var key = new Key<string>("A", "G");

        Assert.That(key.CompareTo(key), Is.EqualTo(0));
    }

    [Test]
    public void CompareTo_GroupEqualAndNameEqual_NameSameReference_GroupIsDefault_Implicit()
    {
        const string keyName = "A";

        var key1 = new Key<string>(keyName);
        var key2 = new Key<string>(keyName);

        Assert.That(key1.CompareTo(key2), Is.EqualTo(0));
    }

    [Test]
    public void CompareTo_GroupEqualAndNameEqual_NameSameReference_GroupIsDefault_Explicit_BothGroupsAreDefaultGroupConstant()
    {
        const string keyName = "A";

        var key1 = new Key<string>(keyName, Key<string>.DefaultGroup);
        var key2 = new Key<string>(keyName, Key<string>.DefaultGroup);

        Assert.That(key1.CompareTo(key2), Is.EqualTo(0));
    }

    [Test]
    public void CompareTo_GroupEqualAndNameEqual_NameSameReference_GroupIsDefault_Explicit_GroupOfBaseIsDefaultGroupConstant()
    {
        const string keyName = "A";

        var key1 = new Key<string>(keyName, Key<string>.DefaultGroup);
        var key2 = new Key<string>(keyName, CreateUniqueReference(Key<string>.DefaultGroup));

        Assert.That(key1.CompareTo(key2), Is.EqualTo(0));
    }

    [Test]
    public void CompareTo_GroupEqualAndNameEqual_NameSameReference_GroupIsDefault_Explicit_GroupOfOtherIsDefaultGroupConstant()
    {
        const string keyName = "A";

        var key1 = new Key<string>(keyName, CreateUniqueReference(Key<string>.DefaultGroup));
        var key2 = new Key<string>(keyName, Key<string>.DefaultGroup);

        Assert.That(key1.CompareTo(key2), Is.EqualTo(0));
    }

    [Test]
    public void CompareTo_GroupEqualAndNameEqual_NameSameReference_GroupIsDefault_Explicit_NeitherGroupsAreDefaultGroupConstant()
    {
        const string keyName = "A";

        var key1 = new Key<string>(keyName, CreateUniqueReference(Key<string>.DefaultGroup));
        var key2 = new Key<string>(keyName, CreateUniqueReference(Key<string>.DefaultGroup));

        Assert.That(key1.CompareTo(key2), Is.EqualTo(0));
    }

    [Test]
    public void CompareTo_GroupEqualAndNameEqual_NameSameReference_GroupIsNotDefault_SameReference()
    {
        const string keyName = "A";
        const string groupName = "GROUP_B";

        var key1 = new Key<string>(keyName, groupName);
        var key2 = new Key<string>(keyName, groupName);

        Assert.That(key1.CompareTo(key2), Is.EqualTo(0));
    }

    [Test]
    public void CompareTo_GroupEqualAndNameEqual_NameSameReference_GroupIsNotDefault_NotSameReference()
    {
        const string keyName = "A";
        const string groupName = "Group_A";

        var key1 = new Key<string>(keyName, groupName);
        var key2 = new Key<string>(keyName, CreateUniqueReference(groupName));

        Assert.That(key1.CompareTo(key2), Is.EqualTo(0));
    }

    [Test]
    public void CompareTo_GroupEqualAndNameEqual_NameNotSameReference_GroupIsDefault_Implicit()
    {
        const string keyName = "Key_A";

        var key1 = new Key<string>(keyName);
        var key2 = new Key<string>(CreateUniqueReference(keyName));

        Assert.That(key1.CompareTo(key2), Is.EqualTo(0));
    }

    [Test]
    public void CompareTo_GroupEqualAndNameEqual_NameNotSameReference_GroupIsDefault_Explicit_BothGroupsAreDefaultGroupConstant()
    {
        const string keyName = "Key_A";

        var key1 = new Key<string>(keyName, Key<string>.DefaultGroup);
        var key2 = new Key<string>(CreateUniqueReference(keyName), Key<string>.DefaultGroup);

        Assert.That(key1.CompareTo(key2), Is.EqualTo(0));
    }

    [Test]
    public void CompareTo_GroupEqualAndNameEqual_NameNotSameReference_GroupIsDefault_Explicit_GroupOfBaseIsDefaultGroupConstant()
    {
        const string keyName = "Key_A";

        var key1 = new Key<string>(keyName, Key<string>.DefaultGroup);
        var key2 = new Key<string>(CreateUniqueReference(keyName), CreateUniqueReference(Key<string>.DefaultGroup));

        Assert.That(key1.CompareTo(key2), Is.EqualTo(0));
    }

    [Test]
    public void CompareTo_GroupEqualAndNameEqual_NameNotSameReference_GroupIsDefault_Explicit_GroupOfOtherIsDefaultGroupConstant()
    {
        const string keyName = "Key_A";

        var key1 = new Key<string>(keyName, CreateUniqueReference(Key<string>.DefaultGroup));
        var key2 = new Key<string>(CreateUniqueReference(keyName), Key<string>.DefaultGroup);

        Assert.That(key1.CompareTo(key2), Is.EqualTo(0));
    }

    [Test]
    public void CompareTo_GroupEqualAndNameEqual_NameNotSameReference_GroupIsDefault_Explicit_NeitherGroupsAreDefaultGroupConstant()
    {
        const string keyName = "Key_A";

        var key1 = new Key<string>(keyName, CreateUniqueReference(Key<string>.DefaultGroup));
        var key2 = new Key<string>(CreateUniqueReference(keyName), CreateUniqueReference(Key<string>.DefaultGroup));

        Assert.That(key1.CompareTo(key2), Is.EqualTo(0));
    }

    [Test]
    public void CompareTo_GroupEqualAndNameEqual_NameNotSameReference_GroupIsNotDefault_SameReference()
    {
        const string keyName = "Key_A";
        const string groupName = "GROUP_B";

        var key1 = new Key<string>(keyName, groupName);
        var key2 = new Key<string>(CreateUniqueReference(keyName), groupName);

        Assert.That(key1.CompareTo(key2), Is.EqualTo(0));
    }

    [Test]
    public void CompareTo_GroupEqualAndNameEqual_NameNotSameReference_GroupIsNotDefault_NotSameReference()
    {
        const string keyName = "Key_A";
        const string groupName = "Group_A";

        var key1 = new Key<string>(keyName, groupName);
        var key2 = new Key<string>(CreateUniqueReference(keyName), CreateUniqueReference(groupName));

        Assert.That(key1.CompareTo(key2), Is.EqualTo(0));
    }

    [Test]
    public void CompareTo_GroupEqualAndNameNotEqual_NameOfOtherLessThanNameOfBase()
    {
        var key1 = new Key<string>("A");
        var key2 = new Key<string>("B");

        Assert.That(key1.CompareTo(key2), Is.EqualTo(-1));
    }

    [Test]
    public void CompareTo_GroupEqualAndNameNotEqual_NameOfOtherGreatherThanNameOfBase()
    {
        var key1 = new Key<string>("B");
        var key2 = new Key<string>("A");

        Assert.That(key1.CompareTo(key2), Is.EqualTo(1));
    }

    [Test]
    public void CompareTo_GroupNotEqualAndNameEqual_GroupOfBaseIsDefault_Implicit()
    {
        const string keyName = "Key_A";

        var key1 = new Key<string>(keyName);
        var key2 = new Key<string>(CreateUniqueReference(keyName), "G");

        Assert.That(key1.CompareTo(key2), Is.EqualTo(-1));
    }

    [Test]
    public void CompareTo_GroupNotEqualAndNameEqual_GroupOfBaseIsDefault_Explicit_DefaultGroupConstant()
    {
        const string keyName = "Key_A";

        var key1 = new Key<string>(keyName, Key<string>.DefaultGroup);
        var key2 = new Key<string>(CreateUniqueReference(keyName), "G");

        Assert.That(key1.CompareTo(key2), Is.EqualTo(-1));
    }

    [Test]
    public void CompareTo_GroupNotEqualAndNameEqual_GroupOfBaseIsDefault_Explicit_Literal()
    {
        const string keyName = "Key_A";

        var key1 = new Key<string>(keyName, "DEFAULT");
        var key2 = new Key<string>(CreateUniqueReference(keyName), "G");

        Assert.That(key1.CompareTo(key2), Is.EqualTo(-1));
    }

    [Test]
    public void CompareTo_GroupNotEqualAndNameEqual_GroupOfOtherIsDefault_Implicit()
    {
        const string keyName = "Key_A";

        var key1 = new Key<string>(keyName, "G");
        var key2 = new Key<string>(CreateUniqueReference(keyName));

        Assert.That(key1.CompareTo(key2), Is.EqualTo(1));
    }

    [Test]
    public void CompareTo_GroupNotEqualAndNameEqual_GroupOfOtherIsDefault_Explicit_DefaultGroupConstant()
    {
        const string keyName = "Key_A";

        var key1 = new Key<string>(keyName, "G");
        var key2 = new Key<string>(CreateUniqueReference(keyName), Key<string>.DefaultGroup);

        Assert.That(key1.CompareTo(key2), Is.EqualTo(1));
    }

    [Test]
    public void CompareTo_GroupNotEqualAndNameEqual_GroupOfOtherIsDefault_Explicit_Literal()
    {
        const string keyName = "Key_A";

        var key1 = new Key<string>(keyName, "G");
        var key2 = new Key<string>(CreateUniqueReference(keyName), "DEFAULT");

        Assert.That(key1.CompareTo(key2), Is.EqualTo(1));
    }

    [Test]
    public void CompareTo_GroupNotEqualAndNameNotEqual_GroupOfOtherIsGreaterThanGroupOfBase_NameOfOtherIsGreaterThanNameOfBase()
    {
        var key1 = new Key<string>("A", "G");
        var key2 = new Key<string>("B", "H");

        Assert.That(key1.CompareTo(key2), Is.EqualTo(-1));
    }

    [Test]
    public void CompareTo_GroupNotEqualAndNameNotEqual_GroupOfOtherIsGreaterThanGroupOfBase_NameOfOtherIsLessThanNameOfBase()
    {
        var key1 = new Key<string>("B", "G");
        var key2 = new Key<string>("A", "H");

        Assert.That(key1.CompareTo(key2), Is.EqualTo(-1));
    }

    [Test]
    public void CompareTo_GroupNotEqualAndNameNotEqual_GroupOfOtherIsLessThanGroupOfBase_NameOfOtherIsGreaterThanNameOfBase()
    {
        var key1 = new Key<string>("A", "H");
        var key2 = new Key<string>("B", "G");

        Assert.That(key1.CompareTo(key2), Is.EqualTo(1));
    }

    [Test]
    public void CompareTo_GroupNotEqualAndNameNotEqual_GroupOfOtherIsLessThanGroupOfBase_NameOfOtherIsLessThanNameOfBase()
    {
        var key1 = new Key<string>("B", "H");
        var key2 = new Key<string>("A", "G");

        Assert.That(key1.CompareTo(key2), Is.EqualTo(1));
    }

    [Test]
    public void Serialization_CanBeDeserialized()
    {
#pragma warning disable SYSLIB0050
        var key = new Key<string>("A", "B");

        using var ms = new MemoryStream();
        ms.Write(_serializedKeyStringWithNameAndGroup, 0, _serializedKeyStringWithNameAndGroup.Length);
        ms.Position = 0;

        BinaryFormatter formatter = new BinaryFormatter();
        formatter.AssemblyFormat = FormatterAssemblyStyle.Simple;

        var deserialized = formatter.Deserialize(ms) as Key<string>;
        Assert.Multiple(() =>
        {
            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized.Group, Is.EqualTo(key.Group));
            Assert.That(deserialized.Name, Is.EqualTo(key.Name));
        });
#pragma warning restore SYSLIB0050
    }

    [Test]
    public void Serialization_CanBeSerializedAndDeserialized()
    {
        var key = new Key<string>("A", "B");

        using (var ms = new MemoryStream())
        {
            BinaryFormatter formatter = new BinaryFormatter();
            formatter.Serialize(ms, key);

            ms.Position = 0;

            var deserialized = formatter.Deserialize(ms) as Key<string>;
            Assert.Multiple(() =>
            {
                Assert.That(deserialized, Is.Not.Null);
                Assert.That(deserialized.Group, Is.EqualTo(key.Group));
                Assert.That(deserialized.Name, Is.EqualTo(key.Name));
            });
        }
    }

    /// <summary>
    /// Creates a unique reference of the specified <see cref="string"/>.
    /// </summary>
    /// <returns>
    /// A unique reference of the specified <see cref="string"/>.
    /// </returns>
    private static string CreateUniqueReference(string value)
    {
        return new string(value.ToCharArray());
    }
}