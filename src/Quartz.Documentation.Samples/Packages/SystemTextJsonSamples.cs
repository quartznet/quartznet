using System.Collections.Specialized;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Impl;
using Quartz.Serialization.SystemTextJson;

namespace Quartz.Documentation.Samples.Packages;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/packages/system-text-json.md.
/// </summary>
public static class SystemTextJsonSamples
{
    public static void Registration(IServiceCollection services)
    {
        #region sample_stj_registration

        services.AddQuartz(q =>
        {
            q.UsePersistentStore(store =>
            {
                store.UseSqlServer("my connection string");

                // it's generally recommended to stick with
                // string property keys and values when serializing
                store.Configure(options => options.StoreJobDataAsStrings = true);

                store.UseSystemTextJsonSerializer();
            });
        });

        #endregion
    }

    public static void FromProperties()
    {
        #region sample_stj_properties

        var properties = new NameValueCollection
        {
         ["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.LocalTransactionJobStore, Quartz",
         ["quartz.serializer.type"] = "stj"
        };
        ISchedulerFactory schedulerFactory = QuartzSchedulerBuilder.Create()
            .UseProperties(properties)
            .Build();

        #endregion
    }

    #region sample_stj_custom_serializer

    class CustomJsonSerializer : SystemTextJsonObjectSerializer
    {
        // Declaring this constructor is what lets the container hand the serializer the registered custom
        // trigger and calendar serializers; without it only the built-in types are known.
        public CustomJsonSerializer(SystemTextJsonSerializerRegistry registry) : base(registry)
        {
        }

        protected override JsonSerializerOptions CreateSerializerOptions()
        {
            var options = base.CreateSerializerOptions();
            options.Converters.Add(new MyCustomConverter());
            return options;
        }
    }

    #endregion

    public static void UseCustomSerializer(IServiceCollection services)
    {
        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            #region sample_stj_use_custom_serializer

            store.UseSerializer<CustomJsonSerializer>();

            #endregion
        }));
    }

    public static void RegisterCustomSerializers(IServiceCollection services)
    {
        #region sample_stj_register_custom_serializers

        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.UseSqlServer("my connection string");
            store.UseSystemTextJsonSerializer(json =>
            {
                json.AddCalendarSerializer<CustomCalendar>(new CustomCalendarSerializer());
                json.AddTriggerSerializer<CustomTrigger>(new CustomTriggerSerializer());
            });
        }));

        #endregion
    }

    public static void PerSchedulerSerializers(IServiceCollection services, string reportingDb, string ingestDb)
    {
        #region sample_stj_per_scheduler_serializers

        services.AddQuartz("reporting", q => q.UsePersistentStore(store =>
        {
            store.UseSqlServer(reportingDb);
            store.UseSystemTextJsonSerializer(json => json.AddTriggerSerializer<ReportTrigger>(new ReportTriggerSerializer()));
        }));

        services.AddQuartz("ingest", q => q.UsePersistentStore(store =>
        {
            store.UseSqlServer(ingestDb);
            store.UseSystemTextJsonSerializer(json => json.AddTriggerSerializer<IngestTrigger>(new IngestTriggerSerializer()));
        }));

        #endregion
    }

    public static void ContainerWideRegistry(IServiceCollection services)
    {
        #region sample_stj_container_registry

        services.AddSingleton(new SystemTextJsonSerializerRegistry()
            .AddTriggerSerializer<CustomTrigger>(new CustomTriggerSerializer())
            .AddCalendarSerializer<CustomCalendar>(new CustomCalendarSerializer()));

        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.UseSqlServer("my connection string");
            // no callback: the store's serializer reads the container's registry, so the same custom
            // serializers apply to the job store, the HTTP API and the dashboard
            store.UseSystemTextJsonSerializer();
        }));

        #endregion
    }

    public static void KeyedRegistry(IServiceCollection services)
    {
        #region sample_stj_keyed_registry

        services.AddKeyedSingleton("reporting", new SystemTextJsonSerializerRegistry()
            .AddTriggerSerializer<ReportTrigger>(new ReportTriggerSerializer()));

        #endregion
    }

    public static void TypeInfoResolver(IServiceCollection services)
    {
        #region sample_stj_type_info_resolver

        // The metadata for this application's own job-data value types. Only a trimmed or native AOT
        // publish needs it: with reflection on, the resolver chain still ends in reflection.
        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.UseSqlServer("my connection string");
            store.UseSystemTextJsonSerializer(json => json.AddTypeInfoResolver(JobDataContext.Default));
        }));

        #endregion
    }
}

/// <summary>A job-data value type of the application's own, which no contract of Quartz's can name.</summary>
public enum Severity
{
    Routine,
    Urgent
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(Severity))]
internal sealed partial class JobDataContext : JsonSerializerContext;
