using NUnit.Framework;

using Quartz.Tests.Unit.Utils;

namespace Quartz.Tests.Unit;

public class TriggerKeyTest
{
    [Test]
    public void TriggerKeyShouldBeSerializable()
    {
        TriggerKey original = new TriggerKey("name", "group");

        TriggerKey cloned = original.DeepClone();

        Assert.That(cloned.Name, Is.EqualTo(original.Name));
        Assert.That(cloned.Group, Is.EqualTo(original.Group));
    }
}