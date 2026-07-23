// ReSharper disable SuspiciousTypeConversion.Global

using Quartz.Impl.Matchers;

namespace Quartz.Tests.Unit.Impl.Matchers;

[TestFixture]
public class StringOperatorTest
{
    [Test]
    public void Anything_ShouldBeSingleton()
    {
#pragma warning disable NUnit2009
        Assert.That(StringOperator.Anything, Is.SameAs(StringOperator.Anything));
#pragma warning restore NUnit2009
    }

    [Test]
    public void Anything_Evaluate()
    {
        Anything_Evaluate(StringOperator.Anything);
    }

    private static void Anything_Evaluate(StringOperator op)
    {
        Assert.Multiple(() =>
        {
            Assert.That(op.Evaluate(null, null), Is.True);
            Assert.That(op.Evaluate(null, "Quartz"), Is.True);
            Assert.That(op.Evaluate("Quartz", null), Is.True);
            Assert.That(op.Evaluate("Quartz", "Quartz"), Is.True);
            Assert.That(op.Evaluate("aa", new string('a', 2)), Is.True);
            Assert.That(op.Evaluate("Quartz", "QuartZ"), Is.True);
            Assert.That(op.Evaluate("Quartz", "tz"), Is.True);
            Assert.That(op.Evaluate("Quartz", "tZ"), Is.True);
            Assert.That(op.Evaluate("Quartz", "ua"), Is.True);
            Assert.That(op.Evaluate("Quartz", "Qu"), Is.True);
            Assert.That(op.Evaluate("Quartz", "QU"), Is.True);
        });
    }

    [Test]
    public void Anything_Equals_Object()
    {
        var op = StringOperator.Anything;

        Assert.Multiple(() =>
        {
            Assert.That(op.Equals((object)op), Is.True);
            Assert.That(op, Is.Not.EqualTo(StringOperator.Equality));
            Assert.That(op, Is.Not.EqualTo(null));
            Assert.That(op.Equals("xxx"), Is.False);
        });
    }

    [Test]
    public void Anything_Equals_StringOperator()
    {
        var op = StringOperator.Anything;

        Assert.Multiple(() =>
        {
            Assert.That(op.Equals(op), Is.True);
            Assert.That(op, Is.Not.EqualTo(StringOperator.Equality));
            Assert.That(op, Is.Not.EqualTo(null));
        });
    }

    [Test]
    public void Contains_ShouldBeSingleton()
    {
#pragma warning disable NUnit2009
        Assert.That(StringOperator.Contains, Is.SameAs(StringOperator.Contains));
#pragma warning restore NUnit2009
    }

    [Test]
    public void Contains_Evaluate()
    {
        Contains_Evaluate(StringOperator.Contains);
    }

    private static void Contains_Evaluate(StringOperator op)
    {
        Assert.Multiple(() =>
        {
            Assert.That(op.Evaluate(null, null), Is.False);
            Assert.That(op.Evaluate(null, "Quartz"), Is.False);
            Assert.That(op.Evaluate("Quartz", "Quartz"), Is.True);
            Assert.That(op.Evaluate("aa", new string('a', 2)), Is.True);
            Assert.That(op.Evaluate(null, string.Empty), Is.False);
            Assert.That(op.Evaluate("Quartz", "QuartZ"), Is.False);
            Assert.That(op.Evaluate("Quartz", "tz"), Is.True);
            Assert.That(op.Evaluate("Quartz", "tZ"), Is.False);
            Assert.That(op.Evaluate("Quartz", "ua"), Is.True);
            Assert.That(op.Evaluate("Quartz", "Qu"), Is.True);
            Assert.That(op.Evaluate("Quartz", "QU"), Is.False);
        });
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
            Assert.That(ex.ParamName, Is.EqualTo("value"));
        }
    }

    [Test]
    public void Contains_Equals_Object()
    {
        var op = StringOperator.Contains;

        Assert.Multiple(() =>
        {
            Assert.That(op.Equals((object)op), Is.True);
            Assert.That(op, Is.Not.EqualTo(StringOperator.Anything));
            Assert.That(op, Is.Not.EqualTo(null));
            Assert.That(op.Equals("xxx"), Is.False);
        });
    }

    [Test]
    public void Contains_Equals_StringOperator()
    {
        var op = StringOperator.Contains;

        Assert.Multiple(() =>
        {
            Assert.That(op.Equals(op), Is.True);
            Assert.That(op, Is.Not.EqualTo(StringOperator.Anything));
            Assert.That(op.Equals(null), Is.False);
        });
    }

    [Test]
    public void Custom_Equals_Object()
    {
        var op = new NothingOperator();

        Assert.Multiple(() =>
        {
            Assert.That(op.Equals((object)op), Is.True);
            Assert.That(op, Is.Not.EqualTo(StringOperator.Anything));
            Assert.That(op, Is.Not.EqualTo(null));
            Assert.That(op.Equals("xxx"), Is.False);
        });
    }

    [Test]
    public void Custom_Equals_StringOperator()
    {
        var op = new NothingOperator();

        Assert.Multiple(() =>
        {
            Assert.That(op.Equals(op), Is.True);
            Assert.That(op, Is.Not.EqualTo(StringOperator.Anything));
            Assert.That(op.Equals(null), Is.False);
        });
    }

    [Test]
    public void EndsWith_ShouldBeSingleton()
    {
#pragma warning disable NUnit2009
        Assert.That(StringOperator.EndsWith, Is.SameAs(StringOperator.EndsWith));
#pragma warning restore NUnit2009
    }

    [Test]
    public void EndsWith_Evaluate()
    {
        var op = StringOperator.EndsWith;

        Assert.Multiple(() =>
        {
            Assert.That(op.Evaluate(null, null), Is.False);
            Assert.That(op.Evaluate(null, "Quartz"), Is.False);
            Assert.That(op.Evaluate("Quartz", "Quartz"), Is.True);
            Assert.That(op.Evaluate("aa", new string('a', 2)), Is.True);
            Assert.That(op.Evaluate(null, string.Empty), Is.False);
            Assert.That(op.Evaluate("Quartz", "QuartZ"), Is.False);
            Assert.That(op.Evaluate("Quartz", "tz"), Is.True);
            Assert.That(op.Evaluate("Quartz", "tZ"), Is.False);
            Assert.That(op.Evaluate("Quartz", "ua"), Is.False);
            Assert.That(op.Evaluate("Quartz", "Qu"), Is.False);
            Assert.That(op.Evaluate("Quartz", "QU"), Is.False);
        });
    }

    private static void EndsWith_Evaluate(StringOperator op)
    {
        Assert.Multiple(() =>
        {
            Assert.That(op.Evaluate(null, null), Is.False);
            Assert.That(op.Evaluate(null, "Quartz"), Is.False);
            Assert.That(op.Evaluate("Quartz", "Quartz"), Is.True);
            Assert.That(op.Evaluate("aa", new string('a', 2)), Is.True);
            Assert.That(op.Evaluate(null, string.Empty), Is.False);
            Assert.That(op.Evaluate("Quartz", "QuartZ"), Is.False);
            Assert.That(op.Evaluate("Quartz", "tz"), Is.True);
            Assert.That(op.Evaluate("Quartz", "tZ"), Is.False);
            Assert.That(op.Evaluate("Quartz", "ua"), Is.False);
            Assert.That(op.Evaluate("Quartz", "Qu"), Is.False);
            Assert.That(op.Evaluate("Quartz", "QU"), Is.False);
        });
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
            Assert.That(ex.ParamName, Is.EqualTo("value"));
        }
    }

    [Test]
    public void EndsWith_Equals_Object()
    {
        var op = StringOperator.EndsWith;

        Assert.Multiple(() =>
        {
            Assert.That(op.Equals((object)op), Is.True);
            Assert.That(op, Is.Not.EqualTo(StringOperator.Anything));
            Assert.That(op, Is.Not.EqualTo(null));
            Assert.That(op.Equals("xxx"), Is.False);
        });
    }

    [Test]
    public void EndsWith_Equals_StringOperator()
    {
        var op = StringOperator.EndsWith;

        Assert.Multiple(() =>
        {
            Assert.That(op.Equals(op), Is.True);
            Assert.That(op.Equals(StringOperator.Anything), Is.False);
            Assert.That(op.Equals(null), Is.False);
        });
    }

    [Test]
    public void Equality_ShouldBeSingleton()
    {
#pragma warning disable NUnit2009
        Assert.That(StringOperator.Equality, Is.SameAs(StringOperator.Equality));
#pragma warning restore NUnit2009
    }

    [Test]
    public void Equality_Evaluate()
    {
        Equality_Evaluate(StringOperator.Equality);
    }

    private static void Equality_Evaluate(StringOperator op)
    {
        Assert.Multiple(() =>
        {
            Assert.That(op.Evaluate(null, null), Is.True);
            Assert.That(op.Evaluate(null, "Quartz"), Is.False);
            Assert.That(op.Evaluate("Quartz", null), Is.False);
            Assert.That(op.Evaluate("Quartz", "Quartz"), Is.True);
            Assert.That(op.Evaluate("aa", new string('a', 2)), Is.True);
            Assert.That(op.Evaluate(null, string.Empty), Is.False);
            Assert.That(op.Evaluate("Quartz", "QuartZ"), Is.False);
            Assert.That(op.Evaluate("Quartz", "tz"), Is.False);
            Assert.That(op.Evaluate("Quartz", "tZ"), Is.False);
            Assert.That(op.Evaluate("Quartz", "ua"), Is.False);
            Assert.That(op.Evaluate("Quartz", "Qu"), Is.False);
            Assert.That(op.Evaluate("Quartz", "QU"), Is.False);
        });
    }

    [Test]
    public void Equality_Equals_Object()
    {
        var op = StringOperator.Equality;

        Assert.Multiple(() =>
        {
            Assert.That(op.Equals((object)op), Is.True);
            Assert.That(op.Equals((object)StringOperator.Anything), Is.False);
            Assert.That(op.Equals((object)null), Is.False);
            Assert.That(op.Equals("xxx"), Is.False);
        });
    }

    [Test]
    public void Equality_Equals_StringOperator()
    {
        var op = StringOperator.Equality;

        Assert.Multiple(() =>
        {
            Assert.That(op.Equals(op), Is.True);
            Assert.That(op, Is.Not.EqualTo(StringOperator.Anything));
            Assert.That(op.Equals(null), Is.False);
        });
    }

    [Test]
    public void StartsWith_ShouldBeSingleton()
    {
#pragma warning disable NUnit2009
        Assert.That(StringOperator.StartsWith, Is.SameAs(StringOperator.StartsWith));
#pragma warning restore NUnit2009
    }

    [Test]
    public void StartsWith_Evaluate()
    {
        var op = StringOperator.StartsWith;

        Assert.Multiple(() =>
        {
            Assert.That(op.Evaluate(null, null), Is.False);
            Assert.That(op.Evaluate(null, "Quartz"), Is.False);
            Assert.That(op.Evaluate("Quartz", "Quartz"));
            Assert.That(op.Evaluate("aa", new string('a', 2)), Is.True);
            Assert.That(op.Evaluate(null, string.Empty), Is.False);
            Assert.That(op.Evaluate("Quartz", "QuartZ"), Is.False);
            Assert.That(op.Evaluate("Quartz", "tz"), Is.False);
            Assert.That(op.Evaluate("Quartz", "tZ"), Is.False);
            Assert.That(op.Evaluate("Quartz", "ua"), Is.False);
            Assert.That(op.Evaluate("Quartz", "Qu"), Is.True);
            Assert.That(op.Evaluate("Quartz", "QU"), Is.False);
        });
    }

    private static void StartsWith_Evaluate(StringOperator op)
    {
        Assert.Multiple(() =>
        {
            Assert.That(op.Evaluate(null, null), Is.False);
            Assert.That(op.Evaluate(null, "Quartz"), Is.False);
            Assert.That(op.Evaluate("Quartz", "Quartz"), Is.True);
            Assert.That(op.Evaluate("aa", new string('a', 2)), Is.True);
            Assert.That(op.Evaluate(null, string.Empty), Is.False);
            Assert.That(op.Evaluate("Quartz", "QuartZ"), Is.False);
            Assert.That(op.Evaluate("Quartz", "tz"), Is.False);
            Assert.That(op.Evaluate("Quartz", "tZ"), Is.False);
            Assert.That(op.Evaluate("Quartz", "ua"), Is.False);
            Assert.That(op.Evaluate("Quartz", "Qu"), Is.True);
            Assert.That(op.Evaluate("Quartz", "QU"), Is.False);
        });
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
            Assert.That(ex.ParamName, Is.EqualTo("value"));
        }
    }

    [Test]
    public void StartsWith_Equals_Object()
    {
        var op = StringOperator.StartsWith;

        Assert.Multiple(() =>
        {
            Assert.That(op.Equals((object)op), Is.True);
            Assert.That(op.Equals((object)StringOperator.Anything), Is.False);
            Assert.That(op.Equals((object)null), Is.False);
            Assert.That(op.Equals("xxx"), Is.False);
        });
    }

    [Test]
    public void StartsWith_Equals_StringOperator()
    {
        var op = StringOperator.StartsWith;
        Assert.Multiple(() =>
        {

            Assert.That(op.Equals(op), Is.True);
            Assert.That(op, Is.Not.EqualTo(StringOperator.Anything));
            Assert.That(op.Equals(null), Is.False);
        });
    }

    private class NothingOperator : StringOperator
    {
        public override bool Evaluate(string value, string compareTo)
        {
            return false;
        }
    }
}
