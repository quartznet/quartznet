using System.Globalization;
using System.Text.Json.Serialization;

using HealthChecks.UI.Client;

using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

using NJsonSchema.Generation;

using NSwag;
using NSwag.Generation.AspNetCore;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Security;

using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Quartz.AspNetCore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Impl.Calendar;
using Quartz.Impl.Matchers;
using Quartz.Plugin.Interrupt;

using Serilog;

namespace Quartz.Examples.AspNetCore;

public class Startup
{
    public Startup(IConfiguration configuration)
    {
        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateLogger();

        Configuration = configuration;
    }

    public IConfiguration Configuration { get; }

    // This method gets called by the runtime. Use this method to add services to the container.
    public void ConfigureServices(IServiceCollection services)
    {
        // make sure you configure logging and open telemetry before quartz services

        services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.ClearProviders();
            loggingBuilder.AddSerilog(dispose: true);
            loggingBuilder.AddOpenTelemetry(options =>
            {
                options.IncludeFormattedMessage = true;
                options.IncludeScopes = true;
            });
        });

        services.AddOpenTelemetry()
            .ConfigureResource(builder => builder.AddService("Quartz ASP.NET Example"))
            .WithMetrics(metrics =>
            {
                metrics.AddRuntimeInstrumentation()
                    .AddMeter("Quartz", "Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.Server.Kestrel", "System.Net.Http");
            })
            .WithTracing(x => x
                .AddSource("Quartz")
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddConsoleExporter()
            );

        var useOtlpExporter = !string.IsNullOrWhiteSpace(Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        if (useOtlpExporter)
        {
            services.AddOpenTelemetry().UseOtlpExporter();
        }

        services.AddRazorPages();

        // base configuration for DI, read from appSettings.json
        services.Configure<QuartzOptions>(Configuration.GetSection("Quartz"));

        // if you are using persistent job store, you might want to alter some options
        services.Configure<QuartzOptions>(options =>
        {
            options.Scheduling.IgnoreDuplicates = true; // default: false
            options.Scheduling.OverWriteExistingData = true; // default: true
        });

        // custom connection provider
        services.AddSingleton<IDbProvider, CustomSqlServerConnectionProvider>();

        // a custom time provider will be pulled from DI
        services.AddSingleton<TimeProvider, CustomTimeProvider>();

        // async disposable
        services.AddScoped<AsyncDisposableDependency>();

        services.AddQuartz(q =>
        {
            // handy when part of cluster or you want to otherwise identify multiple schedulers
            q.SchedulerId = "Scheduler-Core";

            // you can control whether job interruption happens for running jobs when scheduler is shutting down
            q.InterruptJobsOnShutdown = true;

            // when QuartzHostedServiceOptions.WaitForJobsToComplete = true or scheduler.Shutdown(waitForJobsToComplete: true)
            q.InterruptJobsOnShutdownWithWait = true;

            // we can change from the default of 1
            q.MaxBatchSize = 5;

            // we take this from appsettings.json, just show it's possible
            // q.SchedulerName = "Quartz ASP.NET Core Sample Scheduler";

            // these are the defaults
            q.UseSimpleTypeLoader();
            q.UseInMemoryStore();
            q.UseDefaultThreadPool(maxConcurrency: 10);

            // you could use custom too
            q.UseTypeLoader<CustomTypeLoader>();

            // quickest way to create a job with single trigger is to use ScheduleJob
            q.ScheduleJob<ExampleJob>(trigger => trigger
                .WithIdentity("Combined Configuration Trigger")
                .StartAt(DateBuilder.EvenSecondDate(DateTimeOffset.UtcNow.AddSeconds(7)))
                .WithDailyTimeIntervalSchedule(interval: 10, intervalUnit: IntervalUnit.Second)
                .WithDescription("my awesome trigger configured for a job with single call")
            );

            // you can also configure individual jobs and triggers with code
            // this allows you to associated multiple triggers with same job
            // (if you want to have different job data map per trigger for example)
            q.AddJob<ExampleJob>(j => j
                .StoreDurably() // we need to store durably if no trigger is associated
                .WithDescription("my awesome job")
            );

            // here's a known job for triggers
            var jobKey = new JobKey("awesome job", "awesome group");
            q.AddJob<ExampleJob>(jobKey, j => j
                .WithDescription("my awesome job")
                .UsingJobData(nameof(ExampleJob.InjectedString), "Hello")
                .UsingJobData(nameof(ExampleJob.InjectedBool), true)
            );

            q.AddTrigger(t => t
                .WithIdentity("Simple Trigger")
                .ForJob(jobKey)
                .StartNow()
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(10)).RepeatForever())
                .WithDescription("my awesome simple trigger")
                .UsingJobData("ExampleKey", "ExampleValue")
            );

            q.AddTrigger(t => t
                .WithIdentity("Cron Trigger")
                .ForJob(jobKey)
                .StartAt(DateBuilder.EvenSecondDate(DateTimeOffset.UtcNow.AddSeconds(3)))
                .WithCronSchedule("0/3 * * * * ?")
                .WithDescription("my awesome cron trigger")
            );

            // auto-interrupt long-running job
            q.UseJobAutoInterrupt(options =>
            {
                // this is the default
                options.DefaultMaxRunTime = TimeSpan.FromMinutes(5);
            });
            q.ScheduleJob<SlowJob>(
                triggerConfigurator => triggerConfigurator
                    .WithIdentity("slowJobTrigger")
                    .StartNow()
                    .WithSimpleSchedule(x => x.WithIntervalInSeconds(5).RepeatForever()),
                jobConfigurator => jobConfigurator
                    .WithIdentity("slowJob")
                    .UsingJobData(JobInterruptMonitorPlugin.JobDataMapKeyAutoInterruptable, "true")
                    // allow only five seconds for this job, overriding default configuration
                    .UsingJobData(JobInterruptMonitorPlugin.JobDataMapKeyMaxRunTime, TimeSpan.FromSeconds(5).TotalMilliseconds.ToString(CultureInfo.InvariantCulture))
            );

            // async disposable dependencies
            q.ScheduleJob<AsyncDisposableJob>(
                triggerConfigurator => triggerConfigurator
                    .StartNow()
                    .WithSimpleSchedule(x => x.WithIntervalInSeconds(5).WithRepeatCount(2))
            );

            const string calendarName = "myHolidayCalendar";
            q.AddCalendar<HolidayCalendar>(
                name: calendarName,
                replace: true,
                updateTriggers: true,
                x => x.AddExcludedDate(new DateTime(2020, 5, 15))
            );

            q.AddTrigger(t => t
                .WithIdentity("Daily Trigger")
                .ForJob(jobKey)
                .StartAt(DateBuilder.EvenSecondDate(DateTimeOffset.UtcNow.AddSeconds(5)))
                .WithDailyTimeIntervalSchedule(interval: 10, intervalUnit: IntervalUnit.Second)
                .WithDescription("my awesome daily time interval trigger")
                .ModifiedByCalendar(calendarName)
            );

            // also add XML configuration and poll it for changes
            q.UseXmlSchedulingConfiguration(x =>
            {
                x.Files = ["~/quartz_jobs.config"];
                x.ScanInterval = TimeSpan.FromMinutes(1);
                x.FailOnFileNotFound = true;
                x.FailOnSchedulingError = true;
            });

            // convert time zones using converter that can handle Windows/Linux differences
            q.UseTimeZoneConverter();

            // add some listeners
            q.AddSchedulerListener<SampleSchedulerListener>();
            q.AddJobListener<SampleJobListener>(GroupMatcher<JobKey>.GroupEquals(jobKey.Group));
            q.AddTriggerListener<SampleTriggerListener>();

            // Add Quartz.NET HTTP API
            q.AddHttpApi(options =>
            {
                // "/quartz-api" is also default value
                options.ApiPath = "/quartz-api";
                options.IncludeStackTraceInProblemDetails = true;
            });

            q.UsePersistentStore<CustomJobStore>(options =>
            {
                options.UseSystemTextJsonSerializer();
            });

            // example of persistent job store using JSON serializer as an example
            /*
            q.UsePersistentStore(s =>
            {
                s.PerformSchemaValidation = true; // default
                s.UseProperties = true; // preferred, but not default
                s.RetryInterval = TimeSpan.FromSeconds(15);
                s.UseSqlServer("sql-server-01", sqlServer =>
                {
                    // if needed, could create a custom strategy for handling connections
                    //sqlServer.UseConnectionProvider<CustomSqlServerConnectionProvider>();

                    sqlServer.ConnectionString = "some connection string";

                    // or from appsettings.json
                    // sqlServer.ConnectionStringName = "Quartz";

                    // this is the default
                    sqlServer.TablePrefix = "QRTZ_";
                });
                s.UseSystemTextJsonSerializer();
                s.UseClustering(c =>
                {
                    c.CheckinMisfireThreshold = TimeSpan.FromSeconds(20);
                    c.CheckinInterval = TimeSpan.FromSeconds(10);
                });
            });
            */
        });

        // we can use options pattern to support hooking your own configuration with Quartz's
        // because we don't use service registration api, we need to manually ensure the job is present in DI
        services.AddTransient<ExampleJob>();

        // if there is no need to use key matchers, job and trigger listeners can be added to services and Quartz will automatically use these
        services.AddSingleton<IJobListener, SecondSampleJobListener>();
        services.AddSingleton<ITriggerListener>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<SecondSampleTriggerListener>>();
            return new SecondSampleTriggerListener(logger, "Example value");
        });

        services.Configure<SampleOptions>(Configuration.GetSection("Sample"));
        services.AddOptions<QuartzOptions>()
            .Configure<IOptions<SampleOptions>>((options, dep) =>
            {
                if (!string.IsNullOrWhiteSpace(dep.Value.CronSchedule))
                {
                    var jobKey = new JobKey("options-custom-job", "custom");
                    options.AddJob<ExampleJob>(j => j.WithIdentity(jobKey));
                    options.AddTrigger(trigger => trigger
                        .WithIdentity("options-custom-trigger", "custom")
                        .ForJob(jobKey)
                        .WithCronSchedule(dep.Value.CronSchedule));
                }
            });

        // Add health checks
        services.AddQuartzHealthChecks();

        // Quartz.Extensions.Hosting hosting
        services.AddQuartzHostedService(options =>
        {
            // when shutting down we want jobs to complete gracefully
            options.WaitForJobsToComplete = true;
        });

        services
            .AddHealthChecksUI()
            .AddInMemoryStorage();

        services.AddAuthentication(options =>
            {
                options.DefaultScheme = ApiKeyAuthenticationOptions.Scheme;
            })
            .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationOptions.Scheme, options =>
            {
                options.AllowedApiKey = Configuration.GetValue<string>("QuartzHttpApiKey");
            });

        AddSwaggerDocument(services);
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
            app.UseOpenApi();
            app.UseSwaggerUi();
        }
        else
        {
            app.UseExceptionHandler("/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        app.UseStaticFiles();
        app.UseRouting();

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapRazorPages();
            endpoints.MapHealthChecks("healthz", new HealthCheckOptions
            {
                Predicate = _ => true,
                ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
            });
            endpoints.MapHealthChecksUI();

            // Map HTTP API endpoints
            endpoints.MapQuartzApi()
                .RequireAuthorization();
        });
    }

    private static void AddSwaggerDocument(IServiceCollection services)
    {
        const string securityScope = "SwaggerApiKey";

        services.AddEndpointsApiExplorer();
        services.AddSwaggerDocument(settings =>
        {
            settings.AddSecurity(securityScope, new OpenApiSecurityScheme
            {
                Type = OpenApiSecuritySchemeType.ApiKey,
                Name = ApiKeyAuthenticationHandler.ApiKeyHeaderName,
                In = OpenApiSecurityApiKeyLocation.Header,
                Description = "Quartz API key for HTTP API"
            });

            settings.Title = "Quartz.NET HTTP API";
            settings.Version = "v1";
            ((SystemTextJsonSchemaGeneratorSettings) settings.SchemaSettings).SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            settings.OperationProcessors.Add(new OperationProcessor(context =>
            {
                var apiDescription = ((AspNetCoreOperationProcessorContext) context).ApiDescription;
                context.OperationDescription.Operation.Summary = apiDescription.ActionDescriptor.DisplayName;

                foreach (var parameter in context.OperationDescription.Operation.Parameters)
                {
                    if (parameter.Name == "schedulerName")
                    {
                        parameter.Default = "Quartz ASP.NET Core Sample Scheduler";
                        break;
                    }
                }

                return true;
            }));

            settings.OperationProcessors.Add(new AspNetCoreOperationSecurityScopeProcessor(securityScope));
        });
    }

    private sealed class CustomTimeProvider : TimeProvider;
}

