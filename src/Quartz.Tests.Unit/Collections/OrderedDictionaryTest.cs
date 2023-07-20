using NUnit.Framework;
using Quartz.Collections;

namespace Quartz.Tests.Unit.Collections;

[TestFixture]
internal class OrderedDictionaryTest
{
    [Test]
    public void Values_Empty_ToArray()
    {
        var dictionary = new OrderedDictionary<string, int>();
        var valuesCollection = dictionary.Values;

        var valuesArray = valuesCollection.ToArray();
        Assert.AreSame(Array.Empty<int>(), valuesArray);
    }

    [Test]
    public void Values_NotEmpty_ToArray()
    {
        var dictionary = new OrderedDictionary<string, int>();
        dictionary.Add("A", 5);
        dictionary.Add("Z", 2);

        var valuesCollection = dictionary.Values;

        var valuesArray = valuesCollection.ToArray();
        Assert.IsNotNull(valuesArray);
        Assert.AreEqual(2, valuesArray.Length);
        Assert.AreEqual(5, valuesArray[0]);
        Assert.AreEqual(2, valuesArray[1]);
    }
}