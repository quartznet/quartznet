using System.Text.Json.Serialization;

using Microsoft.Data.Sqlite;

using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Documentation.Samples.HowTos;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/how-tos/trimming-and-native-aot.md.
/// </summary>
/// <remarks>
/// SQLite is the driver these show because it is the one this repository already references — the
/// canary runs on it. Every other driver's factory is named the same way.
/// </remarks>
public static class TrimmingSamples
{
    public static void RegisterTheStoreWithTheDriversFactory(IServiceCollection services, string connectionString)
    {
        #region sample_trimming_provider_factory

        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            // The driver's own factory, rather than its name. Nothing is resolved from a string, so
            // there is nothing for the trimmer to have removed.
            store.UseSqlite(SqliteFactory.Instance, connectionString);
        }));

        #endregion
    }

    public static void NameJobTypesAsTypes(IServiceCollection services)
    {
        #region sample_trimming_job_types

        services.AddQuartz(q =>
        {
            // AddJob<T> declares what Quartz reflects over on a job — its public constructors, its
            // public properties and the interfaces it implements — so the trimmer keeps exactly those,
            // and the store finds the type when it reads JOB_CLASS_NAME back as a string.
            q.AddJob<ReportingJob>(job => job.WithIdentity("reporting").StoreDurably());

            q.AddTrigger(trigger => trigger
                .ForJob("reporting")
                .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(1)).RepeatForever()));
        });

        #endregion
    }

    public static void JobDataMetadataForATrimmedPublish(IServiceCollection services, string connectionString)
    {
        #region sample_trimming_job_data_resolver

        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.UseSqlite(SqliteFactory.Instance, connectionString);
            store.UseSystemTextJsonSerializer(registry => registry.AddTypeInfoResolver(ReportJobDataContext.Default));
        }));

        #endregion
    }
}

#region sample_trimming_job_data_context

// A job data value type of this application's own, which no contract of Quartz's can name.
public enum ReportFormat
{
    Csv,
    Pdf
}

// The metadata the registry is handed. Only a trimmed or native AOT publish needs it: with reflection
// on, the resolver chain still ends in reflection and this changes nothing.
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ReportFormat))]
internal sealed partial class ReportJobDataContext : JsonSerializerContext;

#endregion
