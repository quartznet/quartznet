using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Documentation.Samples.Configuration;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/configuration/json.md.
/// </summary>
/// <remarks>
/// The page writes <c>Configuration.GetSection(…)</c>, which is how a class holding the application's
/// configuration reads. The property below is what makes that name resolve here.
/// </remarks>
public static class JsonSamples
{
    private static IConfiguration Configuration => null!;

    public static void UnderDependencyInjection(IServiceCollection services)
    {
        #region sample_configuration_json_with_di

        services.AddQuartz(Configuration.GetSection("Quartz"), q =>
        {
            // Additional code-based configuration still works alongside JSON
            q.AddJob<MyJob>(j => j.WithIdentity("codeJob").StoreDurably());
        });

        #endregion
    }

    public static void WithoutDependencyInjection()
    {
        #region sample_configuration_json_without_di

        ISchedulerFactory factory = QuartzSchedulerBuilder.Create()
            .UseConfiguration(Configuration.GetSection("Quartz"))
            .Build();

        #endregion
    }

    public static void SeveralNamedSchedulers(IServiceCollection services)
    {
        #region sample_configuration_json_named_schedulers

        // Registers "Primary" and "Secondary" named schedulers automatically
        services.AddQuartz(Configuration.GetSection("Quartz"));
        services.AddQuartzHostedService();

        #endregion
    }

    public static void OneNamedScheduler(IServiceCollection services)
    {
        #region sample_configuration_json_one_named_scheduler

        // Both lines are equivalent
        services.AddQuartz("Primary", Configuration.GetSection("Quartz"));
        services.AddQuartz("Primary", Configuration.GetSection("Quartz:Schedulers:Primary"));

        #endregion
    }
}
