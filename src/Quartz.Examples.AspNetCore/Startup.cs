using System.Text.Json.Serialization;

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
using Quartz.Diagnostics;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Impl.Calendar;
using Quartz.Plugins.History;

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
                    .AddMeter(QuartzInstrumentation.MeterName, "Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.Server.Kestrel", "System.Net.Http");
            })
            .WithTracing(x => x
                .AddSource(QuartzInstrumentation.ActivitySourceName)
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

        // if you are using persistent job store, you might want to alter some options
        services.Configure<QuartzOptions>(options =>
        {
            options.Scheduling.IgnoreDuplicates = true; // default: false
            options.Scheduling.OverwriteExistingData = true; // default: true
        });

        // custom connection provider
        services.AddSingleton<IDbProvider, CustomSqlServerConnectionProvider>();

        // a custom time provider will be pulled from DI
        services.AddSingleton<TimeProvider, CustomTimeProvider>();

        // async disposable
        services.AddScoped<AsyncDisposableDependency>();

        // base configuration for DI, read from appSettings.json using hierarchical JSON
        services.AddQuartz(Configuration.GetSection("Quartz"), q =>
        {
            // handy when part of cluster or you want to otherwise identify multiple schedulers
            q.ConfigureScheduler(options => options.InstanceId = "Scheduler-Core");

            // you can control whether running jobs are interrupted when the scheduler shuts down, and
            // whether that also covers a shutdown that waits for them to finish
            // (QuartzHostedServiceOptions.WaitForJobsToComplete = true, or
            // scheduler.Shutdown(waitForJobsToComplete: true))
            q.ConfigureScheduler(options => options.ShutdownJobInterruption = ShutdownJobInterruption.Always);

            // we can change from the default of 1
            q.ConfigureScheduler(options => options.MaxBatchSize = 5);

            // we take this from appsettings.json, just show it's possible
            // q.ConfigureScheduler(options => options.InstanceName = "Quartz ASP.NET Core Sample Scheduler");

            // these are the defaults
            q.UseSimpleTypeLoader();
            q.UseInMemoryStore();
            q.UseDefaultThreadPool(maxConcurrency: 10);

            // you could use custom too
            q.UseTypeLoader<CustomTypeLoader>();

            // log what jobs and triggers do
            q.AddPlugin<LoggingJobHistoryPlugin>();
            q.AddPlugin<LoggingTriggerHistoryPlugin>();

            // quickest way to create a job with single trigger is to use ScheduleJob
            q.ScheduleJob<ExampleJob>(trigger => trigger
                .WithIdentity("Combined Configuration Trigger")
                .StartAt(DateTimeOffset.UtcNow.AddSeconds(7))
                .WithDailyTimeIntervalSchedule(x => x.WithInterval(10, IntervalUnit.Second))
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
            q.AddJob<ExampleJob>(j => j
                .WithIdentity(jobKey)
                .WithDescription("my awesome job")
                // naming the property binds the value to it: the key cannot be mistyped and the value
                // cannot be of the wrong type
                .UsingJobData(j2 => j2.InjectedString, "Hello")
                .UsingJobData(j2 => j2.InjectedBool, true)
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
                .StartAt(DateTimeOffset.UtcNow.AddSeconds(3))
                .WithCronSchedule("0/3 * * * * ?")
                .WithDescription("my awesome cron trigger")
            );

            // interrupt a job that runs longer than it is allowed to. SlowJob says
            // [JobTimeout("00:00:05")] for itself, so five seconds is what it gets rather than this
            q.AddJobTimeout(TimeSpan.FromMinutes(5));

            q.ScheduleJob<SlowJob>(
                triggerConfigurator => triggerConfigurator
                    .WithIdentity("slowJobTrigger")
                    .StartNow()
                    .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(5)).RepeatForever()),
                jobConfigurator => jobConfigurator.WithIdentity("slowJob")
            );

            // async disposable dependencies
            q.ScheduleJob<AsyncDisposableJob>(
                triggerConfigurator => triggerConfigurator
                    .StartNow()
                    .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(5)).WithRepeatCount(2))
            );

            const string calendarName = "myHolidayCalendar";
            q.AddCalendar<HolidayCalendar>(
                name: calendarName,
                options: new AddCalendarOptions { Replace = true, UpdateTriggers = true },
                configure: x => x.AddExcludedDay(new DateOnly(2020, 5, 15))
            );

            q.AddTrigger(t => t
                .WithIdentity("Daily Trigger")
                .ForJob(jobKey)
                .StartAt(DateTimeOffset.UtcNow.AddSeconds(5))
                .WithDailyTimeIntervalSchedule(x => x.WithInterval(10, IntervalUnit.Second))
                .WithDescription("my awesome daily time interval trigger")
                .WithCalendarName(calendarName)
            );

            // your own configuration can decide what a scheduler runs: whether there is a schedule at all
            // is decided here, where jobs and triggers are registered, and the schedule itself is read out
            // of the container when the trigger is built
            if (!string.IsNullOrWhiteSpace(Configuration.GetSection("Sample")[nameof(SampleOptions.CronSchedule)]))
            {
                var customJobKey = new JobKey("options-custom-job", "custom");
                q.AddJob<ExampleJob>(j => j.WithIdentity(customJobKey));
                q.AddTrigger((serviceProvider, trigger) => trigger
                    .WithIdentity("options-custom-trigger", "custom")
                    .ForJob(customJobKey)
                    .WithCronSchedule(serviceProvider.GetRequiredService<IOptions<SampleOptions>>().Value.CronSchedule)
                );
            }

            // also add XML configuration and poll it for changes
            q.UseXmlSchedulingConfiguration(x =>
            {
                x.Files.Add("~/quartz_jobs.config");
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

            q.UsePersistentStore<CustomJobStore>(options =>
            {
                options.UseSystemTextJsonSerializer();
            });

            // example of persistent job store using JSON serializer as an example
            /*
            q.UsePersistentStore(s =>
            {
                s.UseSqlServer(sqlServer =>
                {
                    sqlServer.ConnectionString = "some connection string";

                    // or from appsettings.json
                    // sqlServer.ConnectionStringName = "Quartz";

                    // or a DbDataSource the application registered in the container
                    // sqlServer.UseRegisteredDataSource = true;

                    // if needed, a custom strategy for handling connections is registered as
                    // IDbProvider in the container, the way CustomSqlServerConnectionProvider is above
                });
                s.ConfigureStore(options =>
                {
                    options.SchemaProvisioning = SchemaProvisioning.Validate; // default
                    options.UseProperties = true; // preferred, but not default
                    options.DbRetryInterval = TimeSpan.FromSeconds(15);

                    // this is the default
                    options.TablePrefix = "QRTZ_";
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

        // Add Quartz.NET HTTP API, which serves every scheduler in the container rather than one of them
        services.AddQuartzHttpApi(options =>
        {
            // "/quartz-api" is also default value
            options.ApiPath = "/quartz-api";
            options.IncludeStackTraceInProblemDetails = true;
        });

        // adding a job to the scheduler does not register its type, so we do that ourselves
        services.AddTransient<ExampleJob>();

        // if there is no need to use key matchers, job and trigger listeners can be added to services and Quartz will automatically use these
        services.AddSingleton<IJobListener, SecondSampleJobListener>();
        services.AddSingleton<ITriggerListener>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<SecondSampleTriggerListener>>();
            return new SecondSampleTriggerListener(logger, "Example value");
        });

        // your own options, read by the trigger registered above
        services.Configure<SampleOptions>(Configuration.GetSection("Sample"));

        // Add health checks
        services.AddHealthChecks().AddQuartz();

        // Add Quartz.NET Dashboard
        services.AddQuartzDashboard();

        // run the scheduler as an IHostedService
        services.AddQuartzHostedService(options =>
        {
            // when shutting down we want jobs to complete gracefully
            options.WaitForJobsToComplete = true;
        });

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
        app.UseAntiforgery();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapRazorPages();
            endpoints.MapHealthChecks("healthz", new HealthCheckOptions
            {
                Predicate = _ => true,
            });

            // Map HTTP API endpoints. The path can be named here, beside the other routes, or set
            // through QuartzHttpApiOptions.ApiPath; a pattern given here wins over that.
            endpoints.MapQuartzHttpApi("/quartz-api")
                .RequireAuthorization();

            // Map Quartz.NET Dashboard UI at /quartz. Anonymous on purpose and said out loud: this
            // sample authenticates with an API key header, which a browser cannot send, and a mapping
            // that stated nothing would refuse to start. A deployment authorizes it instead - with
            // RequireAuthorization() here, or QuartzDashboardOptions.AuthorizationPolicy.
            endpoints.MapQuartzDashboard("/quartz").AllowAnonymous();
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

