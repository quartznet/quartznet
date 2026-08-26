using System.Collections.Specialized;
using System.Runtime.Serialization;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Quartz.Impl;
using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;
using Quartz.Serialization.Newtonsoft;
using Quartz.Serialization.Newtonsoft.Calendars;
using Quartz.Serialization.Newtonsoft.Triggers;

// A namespace of its own: this page and the System.Text.Json page both name their custom types
// CustomCalendar and CustomTrigger, and each sample has to compile against its own flavour.
namespace Quartz.Documentation.Samples.Packages.NewtonsoftJson;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/packages/json-serialization.md.
/// </summary>
public static class NewtonsoftJsonSamples
{
    public static void Registration(IHostApplicationBuilder builder, string connectionString)
    {
        #region sample_newtonsoft_registration

        builder.Services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.UseSqlServer(connectionString);

            // it's generally recommended to stick with
            // string property keys and values when serializing
            store.ConfigureStore(options => options.StoreJobDataAsStrings = true);

            store.UseNewtonsoftJsonSerializer();
        }));

        #endregion
    }

    public static async ValueTask Standalone()
    {
        #region sample_newtonsoft_standalone

        await using StandaloneSchedulerFactory schedulerFactory = QuartzSchedulerBuilder.Create()
            .UsePersistentStore(store =>
            {
                store.UseGenericDatabase("MyProvider", "my connection string");
                store.ConfigureStore(options => options.StoreJobDataAsStrings = true);
                store.UseNewtonsoftJsonSerializer();
            })
            .Build();

        #endregion
    }

    public static async ValueTask FromProperties()
    {
        #region sample_newtonsoft_properties

        NameValueCollection properties = new()
        {
            ["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.LocalTransactionJobStore, Quartz",
            ["quartz.serializer.type"] = "newtonsoft"
        };

        await using StandaloneSchedulerFactory schedulerFactory = QuartzSchedulerBuilder.Create()
            .UseProperties(properties)
            .Build();

        #endregion
    }

    #region sample_newtonsoft_custom_serializer

    class CustomJsonSerializer : NewtonsoftJsonObjectSerializer
    {
        protected override JsonSerializerSettings CreateSerializerSettings()
        {
            var settings = base.CreateSerializerSettings();
            settings.Converters.Add(new MyCustomConverter());
            return settings;
        }
    }

    #endregion

    public static void UseCustomSerializer(IServiceCollection services)
    {
        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            #region sample_newtonsoft_use_custom_serializer

            store.UseSerializer<CustomJsonSerializer>();

            #endregion
        }));
    }

    public static void RegisterCalendarSerializer(IHostApplicationBuilder builder)
    {
        #region sample_newtonsoft_register_calendar_serializer

        builder.Services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.UseNewtonsoftJsonSerializer(json =>
            {
                json.AddCalendarSerializer<CustomCalendar>(new CustomCalendarSerializer());
            });
        }));

        #endregion
    }

    public static void BuildARegistryDirectly()
    {
        #region sample_newtonsoft_registry_directly

        NewtonsoftJsonSerializerRegistry registry = new NewtonsoftJsonSerializerRegistry()
            .AddCalendarSerializer<CustomCalendar>(new CustomCalendarSerializer())
            .AddTriggerSerializer<CustomTrigger>(new CustomTriggerSerializer());

        NewtonsoftJsonObjectSerializer serializer = new(registry);

        #endregion
    }
}

#region sample_newtonsoft_custom_calendar

[Serializable]
class CustomCalendar : BaseCalendar
{
    public CustomCalendar()
    {
    }

    // binary serialization support
    protected CustomCalendar(SerializationInfo info, StreamingContext context) : base(info, context)
    {
        SomeCustomProperty = info?.GetBoolean("SomeCustomProperty") ?? true;
    }

    public bool SomeCustomProperty { get; set; } = true;

    // binary serialization support
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);
        info?.AddValue("SomeCustomProperty", SomeCustomProperty);
    }
}

// JSON serialization support
class CustomCalendarSerializer : CalendarSerializer<CustomCalendar>
{
    protected override CustomCalendar Create(JObject source)
    {
        return new CustomCalendar();
    }

    protected override void SerializeFields(JsonWriter writer, CustomCalendar calendar)
    {
        writer.WritePropertyName("SomeCustomProperty");
        writer.WriteValue(calendar.SomeCustomProperty);
    }

    protected override void DeserializeFields(CustomCalendar calendar, JObject source)
    {
        calendar.SomeCustomProperty = source["SomeCustomProperty"]!.Value<bool>();
    }
}

#endregion

/// <summary>
/// The Newtonsoft flavour of the custom trigger the registry sample registers.
/// </summary>
class CustomTrigger : SimpleTriggerImpl;

class CustomTriggerSerializer : TriggerSerializer<CustomTrigger>
{
    public override string TriggerTypeName => "CustomTrigger";

    public override IScheduleBuilder CreateScheduleBuilder(JObject source) => SimpleScheduleBuilder.Create();

    protected override void SerializeFields(JsonWriter writer, CustomTrigger trigger)
    {
    }
}

/// <summary>
/// A converter of the reader's own, standing in for whatever the application needs.
/// </summary>
class MyCustomConverter : JsonConverter<Uri>
{
    public override Uri? ReadJson(JsonReader reader, Type objectType, Uri? existingValue, bool hasExistingValue, JsonSerializer serializer) =>
        reader.Value is string value ? new Uri(value) : existingValue;

    public override void WriteJson(JsonWriter writer, Uri? value, JsonSerializer serializer) =>
        writer.WriteValue(value?.ToString());
}
