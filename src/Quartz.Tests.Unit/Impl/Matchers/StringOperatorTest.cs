using NUnit.Framework;
using Quartz.Impl.Matchers;

using System.Runtime.Serialization.Formatters;
using System.Runtime.Serialization.Formatters.Binary;

namespace Quartz.Tests.Unit.Impl.Matchers;

[TestFixture]
public class StringOperatorTest
{
    [Test]
    public void Anything_CanBeDeserialized()
    {
        var op = Deserialize<StringOperator>("StringOperator_Anything");

        Assert.IsNotNull(op);
        Anything_Evaluate(op);
    }

    [Test]
    public void Anything_CanBeSerializedAndDeserialized()
    {
        var op = SerializeAndDeserialize(StringOperator.Anything);
        Anything_Evaluate(op);
    }

    [Test]
    public void Anything_ShouldBeSingleton()
    {
        Assert.AreSame(StringOperator.Anything, StringOperator.Anything);
    }

    [Test]
    public void Anything_Evaluate()
    {
        Anything_Evaluate(StringOperator.Anything);
    }

    private static void Anything_Evaluate(StringOperator op)
    {
        Assert.IsTrue(op.Evaluate(null, null));
        Assert.IsTrue(op.Evaluate(null, "Quartz"));
        Assert.IsTrue(op.Evaluate("Quartz", null));
        Assert.IsTrue(op.Evaluate("Quartz", "Quartz"));
        Assert.IsTrue(op.Evaluate("aa", new string('a', 2)));
        Assert.IsTrue(op.Evaluate("Quartz", "QuartZ"));
        Assert.IsTrue(op.Evaluate("Quartz", "tz"));
        Assert.IsTrue(op.Evaluate("Quartz", "tZ"));
        Assert.IsTrue(op.Evaluate("Quartz", "ua"));
        Assert.IsTrue(op.Evaluate("Quartz", "Qu"));
        Assert.IsTrue(op.Evaluate("Quartz", "QU"));
    }

    [Test]
    public void Anything_Equals_Object()
    {
        var op = StringOperator.Anything;

        Assert.IsTrue(op.Equals((object) op));
        Assert.IsTrue(op.Equals((object) SerializeAndDeserialize(op)));
        Assert.IsFalse(op.Equals((object) StringOperator.Equality));
        Assert.IsFalse(op.Equals((object) null));
        Assert.IsFalse(op.Equals("xxx"));
    }

    [Test]
    public void Anything_Equals_StringOperator()
    {
        var op = StringOperator.Anything;

        Assert.IsTrue(op.Equals(op));
        Assert.IsTrue(op.Equals(SerializeAndDeserialize(op)));
        Assert.IsFalse(op.Equals(StringOperator.Equality));
        Assert.IsFalse(op.Equals((StringOperator) null));
    }

    [Test]
    public void Contains_CanBeDeserialized()
    {
        var op = Deserialize<StringOperator>("StringOperator_Contains");

        Assert.IsNotNull(op);
        Contains_Evaluate(op);
    }

    [Test]
    public void Contains_CanBeSerializedAndDeserialized()
    {
        var op = SerializeAndDeserialize(StringOperator.Contains);
        Contains_Evaluate(op);
    }

    [Test]
    public void Contains_ShouldBeSingleton()
    {
        Assert.AreSame(StringOperator.Contains, StringOperator.Contains);
    }

    [Test]
    public void Contains_Evaluate()
    {
        Contains_Evaluate(StringOperator.Contains);
    }

    private static void Contains_Evaluate(StringOperator op)
    {
        Assert.IsFalse(op.Evaluate(null, null));
        Assert.IsFalse(op.Evaluate(null, "Quartz"));
        Assert.IsTrue(op.Evaluate("Quartz", "Quartz"));
        Assert.IsTrue(op.Evaluate("aa", new string('a', 2)));
        Assert.IsFalse(op.Evaluate(null, string.Empty));
        Assert.IsFalse(op.Evaluate("Quartz", "QuartZ"));
        Assert.IsTrue(op.Evaluate("Quartz", "tz"));
        Assert.IsFalse(op.Evaluate("Quartz", "tZ"));
        Assert.IsTrue(op.Evaluate("Quartz", "ua"));
        Assert.IsTrue(op.Evaluate("Quartz", "Qu"));
        Assert.IsFalse(op.Evaluate("Quartz", "QU"));
    }

    [Test]
    public void Contains_Evaluate_ValueIsNotNullAndCompareToIsNull()
    {
        var op = StringOperator.Contains;

        try
        {
            op.Evaluate("Quartz", null);
            Assert.Fail();
        }
        catch (ArgumentNullException ex)
        {
            Assert.AreEqual("value", ex.ParamName);
        }
    }

    [Test]
    public void Contains_Equals_Object()
    {
        var op = StringOperator.Contains;

        Assert.IsTrue(op.Equals((object) op));
        Assert.IsTrue(op.Equals((object) SerializeAndDeserialize(op)));
        Assert.IsFalse(op.Equals((object) StringOperator.Anything));
        Assert.IsFalse(op.Equals((object) null));
        Assert.IsFalse(op.Equals("xxx"));
    }

    [Test]
    public void Contains_Equals_StringOperator()
    {
        var op = StringOperator.Contains;

        Assert.IsTrue(op.Equals(op));
        Assert.IsTrue(op.Equals(SerializeAndDeserialize(op)));
        Assert.IsFalse(op.Equals(StringOperator.Anything));
        Assert.IsFalse(op.Equals((StringOperator) null));
    }

    [Test]
    public void Custom_Equals_Object()
    {
        var op = new NothingOperator();

        Assert.IsTrue(op.Equals((object) op));
        Assert.IsTrue(op.Equals((object) SerializeAndDeserialize(op)));
        Assert.IsFalse(op.Equals((object) StringOperator.Anything));
        Assert.IsFalse(op.Equals((object) null));
        Assert.IsFalse(op.Equals("xxx"));
    }

    [Test]
    public void Custom_Equals_StringOperator()
    {
        var op = new NothingOperator();

        Assert.IsTrue(op.Equals(op));
        Assert.IsTrue(op.Equals(SerializeAndDeserialize(op)));
        Assert.IsFalse(op.Equals(StringOperator.Anything));
        Assert.IsFalse(op.Equals((StringOperator) null));
    }

    [Test]
    public void EndsWith_CanBeDeserialized()
    {
        var op = Deserialize<StringOperator>("StringOperator_EndsWith");

        Assert.IsNotNull(op);
        EndsWith_Evaluate(op);
    }

    [Test]
    public void EndsWith_CanBeSerializedAndDeserialized()
    {
        var op = SerializeAndDeserialize(StringOperator.EndsWith);
        EndsWith_Evaluate(op);
    }

    [Test]
    public void EndsWith_ShouldBeSingleton()
    {
        Assert.AreSame(StringOperator.EndsWith, StringOperator.EndsWith);
    }

    [Test]
    public void EndsWith_Evaluate()
    {
        var op = StringOperator.EndsWith;

        Assert.IsFalse(op.Evaluate(null, null));
        Assert.IsFalse(op.Evaluate(null, "Quartz"));
        Assert.IsTrue(op.Evaluate("Quartz", "Quartz"));
        Assert.IsTrue(op.Evaluate("aa", new string('a', 2)));
        Assert.IsFalse(op.Evaluate(null, string.Empty));
        Assert.IsFalse(op.Evaluate("Quartz", "QuartZ"));
        Assert.IsTrue(op.Evaluate("Quartz", "tz"));
        Assert.IsFalse(op.Evaluate("Quartz", "tZ"));
        Assert.IsFalse(op.Evaluate("Quartz", "ua"));
        Assert.IsFalse(op.Evaluate("Quartz", "Qu"));
        Assert.IsFalse(op.Evaluate("Quartz", "QU"));
    }

    private static void EndsWith_Evaluate(StringOperator op)
    {
        Assert.IsFalse(op.Evaluate(null, null));
        Assert.IsFalse(op.Evaluate(null, "Quartz"));
        Assert.IsTrue(op.Evaluate("Quartz", "Quartz"));
        Assert.IsTrue(op.Evaluate("aa", new string('a', 2)));
        Assert.IsFalse(op.Evaluate(null, string.Empty));
        Assert.IsFalse(op.Evaluate("Quartz", "QuartZ"));
        Assert.IsTrue(op.Evaluate("Quartz", "tz"));
        Assert.IsFalse(op.Evaluate("Quartz", "tZ"));
        Assert.IsFalse(op.Evaluate("Quartz", "ua"));
        Assert.IsFalse(op.Evaluate("Quartz", "Qu"));
        Assert.IsFalse(op.Evaluate("Quartz", "QU"));
    }

    [Test]
    public void EndsWith_Evaluate_ValueIsNotNullAndCompareToIsNull()
    {
        var op = StringOperator.EndsWith;

        try
        {
            op.Evaluate("Quartz", null);
            Assert.Fail();
        }
        catch (ArgumentNullException ex)
        {
            Assert.AreEqual("value", ex.ParamName);
        }
    }

    [Test]
    public void EndsWith_Equals_Object()
    {
        var op = StringOperator.EndsWith;

        Assert.IsTrue(op.Equals((object) op));
        Assert.IsTrue(op.Equals((object) SerializeAndDeserialize(op)));
        Assert.IsFalse(op.Equals((object) StringOperator.Anything));
        Assert.IsFalse(op.Equals((object) null));
        Assert.IsFalse(op.Equals("xxx"));
    }

    [Test]
    public void EndsWith_Equals_StringOperator()
    {
        var op = StringOperator.EndsWith;

        Assert.IsTrue(op.Equals(op));
        Assert.IsTrue(op.Equals(SerializeAndDeserialize(op)));
        Assert.IsFalse(op.Equals(StringOperator.Anything));
        Assert.IsFalse(op.Equals((StringOperator) null));
    }

    [Test]
    public void Equality_CanBeDeserialized()
    {
        var op = Deserialize<StringOperator>("StringOperator_Equality");
            
        Assert.IsNotNull(op);
        Equality_Evaluate(op);
    }

    [Test]
    public void Equality_CanBeSerializedAndDeserialized()
    {
        var op = SerializeAndDeserialize(StringOperator.Equality);
        Equality_Evaluate(op);
    }

    [Test]
    public void Equality_ShouldBeSingleton()
    {
        Assert.AreSame(StringOperator.Equality, StringOperator.Equality);
    }

    [Test]
    public void Equality_Evaluate()
    {
        Equality_Evaluate(StringOperator.Equality);
    }

    private static void Equality_Evaluate(StringOperator op)
    {
        Assert.IsTrue(op.Evaluate(null, null));
        Assert.IsFalse(op.Evaluate(null, "Quartz"));
        Assert.IsFalse(op.Evaluate("Quartz", null));
        Assert.IsTrue(op.Evaluate("Quartz", "Quartz"));
        Assert.IsTrue(op.Evaluate("aa", new string('a', 2)));
        Assert.IsFalse(op.Evaluate(null, string.Empty));
        Assert.IsFalse(op.Evaluate("Quartz", "QuartZ"));
        Assert.IsFalse(op.Evaluate("Quartz", "tz"));
        Assert.IsFalse(op.Evaluate("Quartz", "tZ"));
        Assert.IsFalse(op.Evaluate("Quartz", "ua"));
        Assert.IsFalse(op.Evaluate("Quartz", "Qu"));
        Assert.IsFalse(op.Evaluate("Quartz", "QU"));
    }

    [Test]
    public void Equality_Equals_Object()
    {
        var op = StringOperator.Equality;

        Assert.IsTrue(op.Equals((object) op));
        Assert.IsTrue(op.Equals((object) SerializeAndDeserialize(op)));
        Assert.IsFalse(op.Equals((object) StringOperator.Anything));
        Assert.IsFalse(op.Equals((object) null));
        Assert.IsFalse(op.Equals("xxx"));
    }

    [Test]
    public void Equality_Equals_StringOperator()
    {
        var op = StringOperator.Equality;

        Assert.IsTrue(op.Equals(op));
        Assert.IsTrue(op.Equals(SerializeAndDeserialize(op)));
        Assert.IsFalse(op.Equals(StringOperator.Anything));
        Assert.IsFalse(op.Equals((StringOperator) null));
    }

    [Test]
    public void StartsWith_CanBeDeserialized()
    {
        var op = Deserialize<StringOperator>("StringOperator_StartsWith");

        Assert.IsNotNull(op);
        StartsWith_Evaluate(op);
    }

    [Test]
    public void StartsWith_CanBeSerializedAndDeserialized()
    {
        var op = SerializeAndDeserialize(StringOperator.StartsWith);
        StartsWith_Evaluate(op);
    }

    [Test]
    public void StartsWith_ShouldBeSingleton()
    {
        Assert.AreSame(StringOperator.StartsWith, StringOperator.StartsWith);
    }

    [Test]
    public void StartsWith_Evaluate()
    {
        var op = StringOperator.StartsWith;

        Assert.IsFalse(op.Evaluate(null, null));
        Assert.IsFalse(op.Evaluate(null, "Quartz"));
        Assert.IsTrue(op.Evaluate("Quartz", "Quartz"));
        Assert.IsTrue(op.Evaluate("aa", new string('a', 2)));
        Assert.IsFalse(op.Evaluate(null, string.Empty));
        Assert.IsFalse(op.Evaluate("Quartz", "QuartZ"));
        Assert.IsFalse(op.Evaluate("Quartz", "tz"));
        Assert.IsFalse(op.Evaluate("Quartz", "tZ"));
        Assert.IsFalse(op.Evaluate("Quartz", "ua"));
        Assert.IsTrue(op.Evaluate("Quartz", "Qu"));
        Assert.IsFalse(op.Evaluate("Quartz", "QU"));
    }

    private static void StartsWith_Evaluate(StringOperator op)
    {
        Assert.IsFalse(op.Evaluate(null, null));
        Assert.IsFalse(op.Evaluate(null, "Quartz"));
        Assert.IsTrue(op.Evaluate("Quartz", "Quartz"));
        Assert.IsTrue(op.Evaluate("aa", new string('a', 2)));
        Assert.IsFalse(op.Evaluate(null, string.Empty));
        Assert.IsFalse(op.Evaluate("Quartz", "QuartZ"));
        Assert.IsFalse(op.Evaluate("Quartz", "tz"));
        Assert.IsFalse(op.Evaluate("Quartz", "tZ"));
        Assert.IsFalse(op.Evaluate("Quartz", "ua"));
        Assert.IsTrue(op.Evaluate("Quartz", "Qu"));
        Assert.IsFalse(op.Evaluate("Quartz", "QU"));
    }

    [Test]
    public void StartsWith_Evaluate_ValueIsNotNullAndCompareToIsNull()
    {
        var op = StringOperator.StartsWith;

        try
        {
            op.Evaluate("Quartz", null);
            Assert.Fail();
        }
        catch (ArgumentNullException ex)
        {
            Assert.AreEqual("value", ex.ParamName);
        }
    }

    [Test]
    public void StartsWith_Equals_Object()
    {
        var op = StringOperator.StartsWith;

        Assert.IsTrue(op.Equals((object) op));
        Assert.IsTrue(op.Equals((object) SerializeAndDeserialize(op)));
        Assert.IsFalse(op.Equals((object) StringOperator.Anything));
        Assert.IsFalse(op.Equals((object) null));
        Assert.IsFalse(op.Equals("xxx"));
    }

    [Test]
    public void StartsWith_Equals_StringOperator()
    {
        var op = StringOperator.StartsWith;

        Assert.IsTrue(op.Equals(op));
        Assert.IsTrue(op.Equals(SerializeAndDeserialize(op)));
        Assert.IsFalse(op.Equals(StringOperator.Anything));
        Assert.IsFalse(op.Equals((StringOperator) null));
    }

    [Serializable]
    private class NothingOperator : StringOperator
    {
        public override bool Evaluate(string value, string compareTo)
        {
            return false;
        }
    }

    private static T SerializeAndDeserialize<T>(T stringOperator)
    {
        var formatter = new BinaryFormatter();

        using (var ms = new MemoryStream())
        {
            formatter.Serialize(ms, stringOperator);

            ms.Position = 0;

            return (T) formatter.Deserialize(ms);
        }
    }

    private static T Deserialize<T>(string name)
    {
        using (var fs = File.OpenRead(Path.Combine("Serialized", name + ".ser")))
        {
            BinaryFormatter binaryFormatter = new BinaryFormatter();
            binaryFormatter.AssemblyFormat = FormatterAssemblyStyle.Simple;
            return (T) binaryFormatter.Deserialize(fs);
        }
    }
}