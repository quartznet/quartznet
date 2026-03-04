using NUnit.Framework;

namespace Quartz.Tests.Unit.Extensions.Serialization;

public class SerializationExtensionsTests
{
    [Test]
    public void GetJobDataMap_WithCustomJobDataConverter()
    {
        var source = """{"Test":{"Content":"Hello, World!"}}"""u8;

        TestJobDataObjectSerializer os = new();
        os.Initialize();

        var map = os.DeSerialize<JobDataMap>(source.ToArray());
        Assert.That(map, Is.Not.Null);
        Assert.That(map.ContainsKey("Test"), Is.True);
        var test = map["Test"] as TestJobData;
        Assert.That(test, Is.Not.Null);
        Assert.That(test.Content, Is.EqualTo("Hello, World!"));
    }
}
