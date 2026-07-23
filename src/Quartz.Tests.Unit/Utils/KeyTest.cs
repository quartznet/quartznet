using Quartz.Util;

namespace Quartz.Tests.Unit.Utils;

/// <summary>
/// Unit tests for Key&lt;T&gt;
/// </summary>
/// <author>Gert Driesen</author>
public class KeyTest
{
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