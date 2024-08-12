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
        Assert.That(valuesArray, Is.SameAs(Array.Empty<int>()));
    }

    [Test]
    public void Values_NotEmpty_ToArray()
    {
        var dictionary = new OrderedDictionary<string, int>();
        dictionary.Add("A", 5);
        dictionary.Add("Z", 2);

        var valuesCollection = dictionary.Values;

        var valuesArray = valuesCollection.ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(valuesArray, Is.Not.Null);
            Assert.That(valuesArray, Has.Length.EqualTo(2));
            Assert.That(valuesArray[0], Is.EqualTo(5));
            Assert.That(valuesArray[1], Is.EqualTo(2));
        });
    }
}