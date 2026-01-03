using System;
using System.Text.Json;
using System.Text.Json.Serialization;

using Quartz.Simpl;
using Quartz.Util;

namespace Quartz.Tests.Unit.Extensions.Serialization;

public class TestJobDataConverter : JsonConverter<TestJobData>, ICustomJobDataConverter
{
    public override TestJobData Read(ref Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
    {
        return JsonSerializer.Deserialize(ref reader, TestJobDataSerializationOptions.Default.TestJobData);
    }

    public override void Write(Utf8JsonWriter writer, TestJobData value, System.Text.Json.JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, TestJobDataSerializationOptions.Default.TestJobData);
    }

    public bool HandlesProperty(string propertyName)
    {
        return propertyName == "Test";
    }
}

public class TestJobData
{
    public string Content { get; set; }
}

[JsonSerializable(typeof(TestJobData))]
public partial class TestJobDataSerializationOptions : JsonSerializerContext;

public class TestJobDataObjectSerializer : SystemTextJsonObjectSerializer
{
    protected override System.Text.Json.JsonSerializerOptions CreateSerializerOptions()
    {
        var options = base.CreateSerializerOptions();

        options.Converters.Insert(0, new TestJobDataConverter());

        return options;
    }
}
