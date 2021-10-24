using NUnit.Framework;
using Quartz.Util;

namespace Quartz.Tests.Unit.Utils
{
    /// <summary>
    /// Unit tests for Key<T>.
    /// </summary>
    /// <author>Gert Driesen</author>
    [TestFixture]
    public class KeyTest
    {
        [Test]
        public void DefaultGroupIsDefault()
        {
            Assert.AreEqual("DEFAULT", Key<string>.DefaultGroup);
        }

        [Test]
        public void CompareTo_SameReference_GroupIsDefault_Implicit()
        {
            var key = new Key<string>("A");

            Assert.AreEqual(0, key.CompareTo(key));
        }

        [Test]
        public void CompareTo_SameReference_GroupIsDefault_Explicit_Literal()
        {
            var key = new Key<string>("A", CreateUniqueReference(Key<string>.DefaultGroup));

            Assert.AreEqual(0, key.CompareTo(key));
        }

        [Test]
        public void CompareTo_SameReference_GroupIsDefault_Explicit_SameReference()
        {
            var key = new Key<string>("A", Key<string>.DefaultGroup);

            Assert.AreEqual(0, key.CompareTo(key));
        }

        [Test]
        public void CompareTo_SameReference_GroupIsNotDefault()
        {
            var key = new Key<string>("A", "G");

            Assert.AreEqual(0, key.CompareTo(key));
        }

        [Test]
        public void CompareTo_GroupEqualAndNameEqual_NameSameReference_GroupIsDefault_Implicit()
        {
            const string keyName = "A";

            var key1 = new Key<string>(keyName);
            var key2 = new Key<string>(keyName);

            Assert.AreEqual(0, key1.CompareTo(key2));
        }

        [Test]
        public void CompareTo_GroupEqualAndNameEqual_NameSameReference_GroupIsDefault_Explicit_BothGroupsAreDefaultGroupConstant()
        {
            const string keyName = "A";

            var key1 = new Key<string>(keyName, Key<string>.DefaultGroup);
            var key2 = new Key<string>(keyName, Key<string>.DefaultGroup);

            Assert.AreEqual(0, key1.CompareTo(key2));
        }

        [Test]
        public void CompareTo_GroupEqualAndNameEqual_NameSameReference_GroupIsDefault_Explicit_GroupOfBaseIsDefaultGroupConstant()
        {
            const string keyName = "A";

            var key1 = new Key<string>(keyName, Key<string>.DefaultGroup);
            var key2 = new Key<string>(keyName, CreateUniqueReference(Key<string>.DefaultGroup));

            Assert.AreEqual(0, key1.CompareTo(key2));
        }

        [Test]
        public void CompareTo_GroupEqualAndNameEqual_NameSameReference_GroupIsDefault_Explicit_GroupOfOtherIsDefaultGroupConstant()
        {
            const string keyName = "A";

            var key1 = new Key<string>(keyName, CreateUniqueReference(Key<string>.DefaultGroup));
            var key2 = new Key<string>(keyName, Key<string>.DefaultGroup);

            Assert.AreEqual(0, key1.CompareTo(key2));
        }

        [Test]
        public void CompareTo_GroupEqualAndNameEqual_NameSameReference_GroupIsDefault_Explicit_NeitherGroupsAreDefaultGroupConstant()
        {
            const string keyName = "A";

            var key1 = new Key<string>(keyName, CreateUniqueReference(Key<string>.DefaultGroup));
            var key2 = new Key<string>(keyName, CreateUniqueReference(Key<string>.DefaultGroup));

            Assert.AreEqual(0, key1.CompareTo(key2));
        }

        [Test]
        public void CompareTo_GroupEqualAndNameEqual_NameSameReference_GroupIsNotDefault_SameReference()
        {
            const string keyName = "A";
            const string groupName = "GROUP_B";

            var key1 = new Key<string>(keyName, groupName);
            var key2 = new Key<string>(keyName, groupName);

            Assert.AreEqual(0, key1.CompareTo(key2));
        }

        [Test]
        public void CompareTo_GroupEqualAndNameEqual_NameSameReference_GroupIsNotDefault_NotSameReference()
        {
            const string keyName = "A";
            const string groupName = "Group_A";

            var key1 = new Key<string>(keyName, groupName);
            var key2 = new Key<string>(keyName, CreateUniqueReference(groupName));

            Assert.AreEqual(0, key1.CompareTo(key2));
        }

        [Test]
        public void CompareTo_GroupEqualAndNameEqual_NameNotSameReference_GroupIsDefault_Implicit()
        {
            const string keyName = "Key_A";

            var key1 = new Key<string>(keyName);
            var key2 = new Key<string>(CreateUniqueReference(keyName));

            Assert.AreEqual(0, key1.CompareTo(key2));
        }

        [Test]
        public void CompareTo_GroupEqualAndNameEqual_NameNotSameReference_GroupIsDefault_Explicit_BothGroupsAreDefaultGroupConstant()
        {
            const string keyName = "Key_A";

            var key1 = new Key<string>(keyName, Key<string>.DefaultGroup);
            var key2 = new Key<string>(CreateUniqueReference(keyName), Key<string>.DefaultGroup);

            Assert.AreEqual(0, key1.CompareTo(key2));
        }

        [Test]
        public void CompareTo_GroupEqualAndNameEqual_NameNotSameReference_GroupIsDefault_Explicit_GroupOfBaseIsDefaultGroupConstant()
        {
            const string keyName = "Key_A";

            var key1 = new Key<string>(keyName, Key<string>.DefaultGroup);
            var key2 = new Key<string>(CreateUniqueReference(keyName), CreateUniqueReference(Key<string>.DefaultGroup));

            Assert.AreEqual(0, key1.CompareTo(key2));
        }

        [Test]
        public void CompareTo_GroupEqualAndNameEqual_NameNotSameReference_GroupIsDefault_Explicit_GroupOfOtherIsDefaultGroupConstant()
        {
            const string keyName = "Key_A";

            var key1 = new Key<string>(keyName, CreateUniqueReference(Key<string>.DefaultGroup));
            var key2 = new Key<string>(CreateUniqueReference(keyName), Key<string>.DefaultGroup);

            Assert.AreEqual(0, key1.CompareTo(key2));
        }

        [Test]
        public void CompareTo_GroupEqualAndNameEqual_NameNotSameReference_GroupIsDefault_Explicit_NeitherGroupsAreDefaultGroupConstant()
        {
            const string keyName = "Key_A";

            var key1 = new Key<string>(keyName, CreateUniqueReference(Key<string>.DefaultGroup));
            var key2 = new Key<string>(CreateUniqueReference(keyName), CreateUniqueReference(Key<string>.DefaultGroup));

            Assert.AreEqual(0, key1.CompareTo(key2));
        }

        [Test]
        public void CompareTo_GroupEqualAndNameEqual_NameNotSameReference_GroupIsNotDefault_SameReference()
        {
            const string keyName = "Key_A";
            const string groupName = "GROUP_B";

            var key1 = new Key<string>(keyName, groupName);
            var key2 = new Key<string>(CreateUniqueReference(keyName), groupName);

            Assert.AreEqual(0, key1.CompareTo(key2));
        }

        [Test]
        public void CompareTo_GroupEqualAndNameEqual_NameNotSameReference_GroupIsNotDefault_NotSameReference()
        {
            const string keyName = "Key_A";
            const string groupName = "Group_A";

            var key1 = new Key<string>(keyName, groupName);
            var key2 = new Key<string>(CreateUniqueReference(keyName), CreateUniqueReference(groupName));

            Assert.AreEqual(0, key1.CompareTo(key2));
        }

        [Test]
        public void CompareTo_GroupEqualAndNameNotEqual_NameOfOtherLessThanNameOfBase()
        {
            var key1 = new Key<string>("A");
            var key2 = new Key<string>("B");

            Assert.AreEqual(-1, key1.CompareTo(key2));
        }

        [Test]
        public void CompareTo_GroupEqualAndNameNotEqual_NameOfOtherGreatherThanNameOfBase()
        {
            var key1 = new Key<string>("B");
            var key2 = new Key<string>("A");

            Assert.AreEqual(1, key1.CompareTo(key2));
        }

        [Test]
        public void CompareTo_GroupNotEqualAndNameEqual_GroupOfBaseIsDefault_Implicit()
        {
            const string keyName = "Key_A";

            var key1 = new Key<string>(keyName);
            var key2 = new Key<string>(CreateUniqueReference(keyName), "G");

            Assert.AreEqual(-1, key1.CompareTo(key2));
        }

        [Test]
        public void CompareTo_GroupNotEqualAndNameEqual_GroupOfBaseIsDefault_Explicit_DefaultGroupConstant()
        {
            const string keyName = "Key_A";

            var key1 = new Key<string>(keyName, Key<string>.DefaultGroup);
            var key2 = new Key<string>(CreateUniqueReference(keyName), "G");

            Assert.AreEqual(-1, key1.CompareTo(key2));
        }

        [Test]
        public void CompareTo_GroupNotEqualAndNameEqual_GroupOfBaseIsDefault_Explicit_Literal()
        {
            const string keyName = "Key_A";

            var key1 = new Key<string>(keyName, "DEFAULT");
            var key2 = new Key<string>(CreateUniqueReference(keyName), "G");

            Assert.AreEqual(-1, key1.CompareTo(key2));
        }

        [Test]
        public void CompareTo_GroupNotEqualAndNameEqual_GroupOfOtherIsDefault_Implicit()
        {
            const string keyName = "Key_A";

            var key1 = new Key<string>(keyName, "G");
            var key2 = new Key<string>(CreateUniqueReference(keyName));

            Assert.AreEqual(1, key1.CompareTo(key2));
        }

        [Test]
        public void CompareTo_GroupNotEqualAndNameEqual_GroupOfOtherIsDefault_Explicit_DefaultGroupConstant()
        {
            const string keyName = "Key_A";

            var key1 = new Key<string>(keyName, "G");
            var key2 = new Key<string>(CreateUniqueReference(keyName), Key<string>.DefaultGroup);

            Assert.AreEqual(1, key1.CompareTo(key2));
        }

        [Test]
        public void CompareTo_GroupNotEqualAndNameEqual_GroupOfOtherIsDefault_Explicit_Literal()
        {
            const string keyName = "Key_A";

            var key1 = new Key<string>(keyName, "G");
            var key2 = new Key<string>(CreateUniqueReference(keyName), "DEFAULT");

            Assert.AreEqual(1, key1.CompareTo(key2));
        }

        [Test]
        public void CompareTo_GroupNotEqualAndNameNotEqual_GroupOfOtherIsGreaterThanGroupOfBase_NameOfOtherIsGreaterThanNameOfBase()
        {
            var key1 = new Key<string>("A", "G");
            var key2 = new Key<string>("B", "H");

            Assert.AreEqual(-1, key1.CompareTo(key2));
        }

        [Test]
        public void CompareTo_GroupNotEqualAndNameNotEqual_GroupOfOtherIsGreaterThanGroupOfBase_NameOfOtherIsLessThanNameOfBase()
        {
            var key1 = new Key<string>("B", "G");
            var key2 = new Key<string>("A", "H");

            Assert.AreEqual(-1, key1.CompareTo(key2));
        }

        [Test]
        public void CompareTo_GroupNotEqualAndNameNotEqual_GroupOfOtherIsLessThanGroupOfBase_NameOfOtherIsGreaterThanNameOfBase()
        {
            var key1 = new Key<string>("A", "H");
            var key2 = new Key<string>("B", "G");

            Assert.AreEqual(1, key1.CompareTo(key2));
        }

        [Test]
        public void CompareTo_GroupNotEqualAndNameNotEqual_GroupOfOtherIsLessThanGroupOfBase_NameOfOtherIsLessThanNameOfBase()
        {
            var key1 = new Key<string>("B", "H");
            var key2 = new Key<string>("A", "G");

            Assert.AreEqual(1, key1.CompareTo(key2));
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
}
