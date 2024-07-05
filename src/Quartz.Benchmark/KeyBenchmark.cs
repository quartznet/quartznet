using BenchmarkDotNet.Attributes;
using Quartz.Util;

namespace Quartz.Benchmark;

[MemoryDiagnoser]
[DisassemblyDiagnoser]
public class KeyBenchmark
{
    private const string KeyNameA = "KeyNameA";
    private const string KeyNameB = "KeyNameB";
    private const string GroupNameA = "GroupNameA";
    private const string GroupNameB = "GroupNameB";

    private readonly Key<JobKey> _keyNameAGroupA;
    private readonly Key<JobKey> _keyNameAGroupASameReferenceForNameAndGroup;
    private readonly Key<JobKey> _keyNameAGroupANotSameReferenceForName;
    private readonly Key<JobKey> _keyNameAGroupANotSameReferenceForGroup;
    private readonly Key<JobKey> _keyNameAGroupB;
    private readonly Key<JobKey> _keyNameAGroupDefault;
    private readonly Key<JobKey> _keyNameAGroupDefaultSameReferenceForName;
    private readonly Key<JobKey> _keyNameBGroupA;
    private readonly Key<JobKey> _keyNameBGroupDefault;
    private readonly KeyLegacy<JobKey> _legacyKeyNameAGroupA;
    private readonly KeyLegacy<JobKey> _legacyKeyNameAGroupASameReferenceForNameAndGroup;
    private readonly KeyLegacy<JobKey> _legacyKeyNameAGroupANotSameReferenceForName;
    private readonly KeyLegacy<JobKey> _legacyKeyNameAGroupANotSameReferenceForGroup;
    private readonly KeyLegacy<JobKey> _legacyKeyNameAGroupB;
    private readonly KeyLegacy<JobKey> _legacyKeyNameAGroupDefault;
    private readonly KeyLegacy<JobKey> _legacyKeyNameAGroupDefaultSameReferenceForName;
    private readonly KeyLegacy<JobKey> _legacyKeyNameBGroupA;
    private readonly KeyLegacy<JobKey> _legacyKeyNameBGroupDefault;

    public KeyBenchmark()
    {
        _keyNameAGroupA = new Key<JobKey>(KeyNameA, GroupNameA);
        _keyNameAGroupASameReferenceForNameAndGroup = new Key<JobKey>(KeyNameA, GroupNameA);
        _keyNameAGroupANotSameReferenceForName = new Key<JobKey>(CreateUniqueReference(KeyNameA), GroupNameA);
        _keyNameAGroupANotSameReferenceForGroup = new Key<JobKey>(KeyNameA, CreateUniqueReference(GroupNameA));
        _keyNameAGroupB = new Key<JobKey>(KeyNameA, GroupNameB);
        _keyNameAGroupDefault = new Key<JobKey>(KeyNameA);
        _keyNameAGroupDefaultSameReferenceForName = new Key<JobKey>(KeyNameA);
        _keyNameBGroupA = new Key<JobKey>(KeyNameB, GroupNameA);
        _keyNameBGroupDefault = new Key<JobKey>(KeyNameB);

        _legacyKeyNameAGroupA = new KeyLegacy<JobKey>(KeyNameA, GroupNameA);
        _legacyKeyNameAGroupASameReferenceForNameAndGroup = new KeyLegacy<JobKey>(KeyNameA, GroupNameA);
        _legacyKeyNameAGroupANotSameReferenceForName = new KeyLegacy<JobKey>(CreateUniqueReference(KeyNameA), GroupNameA);
        _legacyKeyNameAGroupANotSameReferenceForGroup = new KeyLegacy<JobKey>(KeyNameA, CreateUniqueReference(GroupNameA));
        _legacyKeyNameAGroupB = new KeyLegacy<JobKey>(KeyNameA, GroupNameB);
        _legacyKeyNameAGroupDefault = new KeyLegacy<JobKey>(KeyNameA);
        _legacyKeyNameAGroupDefaultSameReferenceForName = new KeyLegacy<JobKey>(KeyNameA);
        _legacyKeyNameBGroupA = new KeyLegacy<JobKey>(KeyNameB, GroupNameA);
        _legacyKeyNameBGroupDefault = new KeyLegacy<JobKey>(KeyNameB);
    }

    [Benchmark]
    public int CompareTo_GroupIsDefault_ReferenceEquality_Old()
    {
        return _legacyKeyNameAGroupDefault.CompareTo(_legacyKeyNameAGroupDefault);
    }

    [Benchmark]
    public int CompareTo_GroupIsNotDefault_ReferenceEquality_Old()
    {
        return _legacyKeyNameAGroupA.CompareTo(_legacyKeyNameAGroupA);
    }

    [Benchmark]
    public int CompareTo_NameAndGroupEqual_GroupIsNotDefault_NameAndGroupSameReference_Old()
    {
        return _legacyKeyNameAGroupA.CompareTo(_legacyKeyNameAGroupASameReferenceForNameAndGroup);
    }

    [Benchmark]
    public int CompareTo_NameAndGroupEqual_GroupIsNotDefault_NameNotSameReference_Old()
    {
        return _legacyKeyNameAGroupA.CompareTo(_legacyKeyNameAGroupANotSameReferenceForName);
    }

    [Benchmark]
    public int CompareTo_NameAndGroupEqual_GroupIsNotDefault_GroupNotSameReference_Old()
    {
        return _legacyKeyNameAGroupA.CompareTo(_legacyKeyNameAGroupANotSameReferenceForGroup);
    }

    [Benchmark]
    public int CompareTo_NameAndGroupEqual_GroupIsDefault_Old()
    {
        return _legacyKeyNameAGroupDefault.CompareTo(_legacyKeyNameAGroupDefaultSameReferenceForName);
    }

    [Benchmark]
    public int CompareTo_NameEqualAndGroupNotEqual_GroupIsNotDefault_Old()
    {
        return _legacyKeyNameAGroupA.CompareTo(_legacyKeyNameAGroupB);
    }

    [Benchmark]
    public int CompareTo_NameEqualAndGroupNotEqual_GroupOfBaseIsDefault_Old()
    {
        return _legacyKeyNameAGroupDefault.CompareTo(_legacyKeyNameAGroupA);
    }

    [Benchmark]
    public int CompareTo_NameEqualAndGroupNotEqual_GroupOfOtherIsDefault_Old()
    {
        return _legacyKeyNameAGroupA.CompareTo(_legacyKeyNameAGroupDefault);
    }

    [Benchmark]
    public int CompareTo_NameNotEqualAndGroupEqual_GroupIsDefault_Old()
    {
        return _legacyKeyNameAGroupDefault.CompareTo(_legacyKeyNameBGroupDefault);
    }

    [Benchmark]
    public int CompareTo_NameNotEqualAndGroupEqual_GroupIsNotDefault_Old()
    {
        return _legacyKeyNameAGroupA.CompareTo(_legacyKeyNameBGroupA);
    }

    [Benchmark]
    public int CompareTo_NameNotEqualAndGroupNotEqual_GroupOfBaseIsDefault_Old()
    {
        return _legacyKeyNameAGroupDefault.CompareTo(_legacyKeyNameBGroupA);
    }

    [Benchmark]
    public int CompareTo_NameNotEqualAndGroupNotEqual_GroupOfOtherIsDefault_Old()
    {
        return _legacyKeyNameAGroupA.CompareTo(_legacyKeyNameBGroupDefault);
    }

    [Benchmark]
    public int CompareTo_GroupIsDefault_ReferenceEquality_New()
    {
        return _keyNameAGroupDefault.CompareTo(_keyNameAGroupDefault);
    }

    [Benchmark]
    public int CompareTo_GroupIsNotDefault_ReferenceEquality_New()
    {
        return _keyNameAGroupA.CompareTo(_keyNameAGroupA);
    }

    [Benchmark]
    public int CompareTo_NameAndGroupEqual_GroupIsNotDefault_NameAndGroupSameReference_New()
    {
        return _keyNameAGroupA.CompareTo(_keyNameAGroupASameReferenceForNameAndGroup);
    }

    [Benchmark]
    public int CompareTo_NameAndGroupEqual_GroupIsNotDefault_NameNotSameReference_New()
    {
        return _keyNameAGroupA.CompareTo(_keyNameAGroupANotSameReferenceForName);
    }

    [Benchmark]
    public int CompareTo_NameAndGroupEqual_GroupIsNotDefault_GroupNotSameReference_New()
    {
        return _keyNameAGroupA.CompareTo(_keyNameAGroupANotSameReferenceForGroup);
    }

    [Benchmark]
    public int CompareTo_NameAndGroupEqual_GroupIsDefault_New()
    {
        return _keyNameAGroupDefault.CompareTo(_keyNameAGroupDefaultSameReferenceForName);
    }

    [Benchmark]
    public int CompareTo_NameEqualAndGroupNotEqual_GroupIsNotDefault_New()
    {
        return _keyNameAGroupA.CompareTo(_keyNameAGroupB);
    }

    [Benchmark]
    public int CompareTo_NameEqualAndGroupNotEqual_GroupOfBaseIsDefault_New()
    {
        return _keyNameAGroupDefault.CompareTo(_keyNameAGroupA);
    }

    [Benchmark]
    public int CompareTo_NameEqualAndGroupNotEqual_GroupOfOtherIsDefault_New()
    {
        return _keyNameAGroupA.CompareTo(_keyNameAGroupDefault);
    }

    [Benchmark]
    public int CompareTo_NameNotEqualAndGroupEqual_GroupIsDefault_New()
    {
        return _keyNameAGroupDefault.CompareTo(_keyNameBGroupDefault);
    }

    [Benchmark]
    public int CompareTo_NameNotEqualAndGroupEqual_GroupIsNotDefault_New()
    {
        return _keyNameAGroupA.CompareTo(_keyNameBGroupA);
    }

    [Benchmark]
    public int CompareTo_NameNotEqualAndGroupNotEqual_GroupOfBaseIsDefault_New()
    {
        return _keyNameAGroupDefault.CompareTo(_keyNameBGroupA);
    }

    [Benchmark]
    public int CompareTo_NameNotEqualAndGroupNotEqual_GroupOfOtherIsDefault_New()
    {
        return _keyNameAGroupA.CompareTo(_keyNameBGroupDefault);
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

    /// <summary>
    /// Object representing a job or trigger key.
    /// </summary>
    /// <author>  <a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a></author>
    /// <author>Marko Lahma (.NET)</author>
    [Serializable]
    public class KeyLegacy<T> : IComparable<KeyLegacy<T>>
    {
        /// <summary>
        /// The default group for scheduling entities, with the value "DEFAULT".
        /// </summary>
        public const string DefaultGroup = "DEFAULT";

        private string name = null!;
        private string group = null!;

        protected KeyLegacy()
        {
        }

        /// <summary>
        /// Construct a new key with the given name and group.
        /// </summary>
        /// <param name="name">the name</param>
        public KeyLegacy(string name) : this(name, DefaultGroup)
        {
        }

        /// <summary>
        /// Construct a new key with the given name and group.
        /// </summary>
        /// <param name="name">the name</param>
        /// <param name="group">the group</param>
        public KeyLegacy(string name, string group)
        {
            this.name = name ?? throw new ArgumentNullException(nameof(name), "Name cannot be null.");
            this.group = group ?? DefaultGroup;
        }

        /// <summary>
        /// Get the name portion of the key.
        /// </summary>
        /// <returns> the name
        /// </returns>
        public virtual string Name
        {
            get => name;
            set => name = value;
        }

        /// <summary> <para>
        /// Get the group portion of the key.
        /// </para>
        ///
        /// </summary>
        /// <returns> the group
        /// </returns>
        public virtual string Group
        {
            get => group;
            set => group = value;
        }

        /// <summary> <para>
        /// Return the string representation of the key. The format will be:
        /// &lt;group&gt;.&lt;name&gt;.
        /// </para>
        ///
        /// </summary>
        /// <returns> the string representation of the key
        /// </returns>
        public override string ToString()
        {
            return Group + '.' + Name;
        }


        public override int GetHashCode()
        {
            const int Prime = 31;
            int result = 1;
            result = Prime * result + (@group is null ? 0 : group.GetHashCode());
            result = Prime * result + (name is null ? 0 : name.GetHashCode());
            return result;
        }

        public override bool Equals(object? obj)
        {
            if (this == obj)
            {
                return true;
            }
            if (obj is null)
            {
                return false;
            }
            if (GetType() != obj.GetType())
            {
                return false;
            }
            KeyLegacy<T> other = (KeyLegacy<T>) obj;
            if (group is null)
            {
                if (other.group is not null)
                {
                    return false;
                }
            }
            else if (!group.Equals(other.group))
            {
                return false;
            }
            if (name is null)
            {
                if (other.name is not null)
                {
                    return false;
                }
            }
            else if (!name.Equals(other.name))
            {
                return false;
            }
            return true;
        }

        public int CompareTo(KeyLegacy<T>? o)
        {
            if (o is null)
            {
                return 1;
            }

            if (group.Equals(DefaultGroup) && !o.group.Equals(DefaultGroup))
            {
                return -1;
            }
            if (!group.Equals(DefaultGroup) && o.group.Equals(DefaultGroup))
            {
                return 1;
            }

            int r = group.CompareTo(o.Group);
            if (r != 0)
            {
                return r;
            }

            return name.CompareTo(o.Name);
        }
    }
}