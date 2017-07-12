using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.PlatformAbstractions;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

using Quartz.Impl;
using Quartz.Logging;
using Quartz.Impl.Calendar;
//using Quartz.Web.LiveLog;

using Swashbuckle.AspNetCore;
using Swashbuckle.AspNetCore.Swagger;

namespace Quartz.Web
{

    public class Startup
    {
        //public IConfigurationRoot Configuration { get; }

        public Startup(IHostingEnvironment env)
        {   
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc();
            //call this in case you need aspnet-user-authtype/aspnet-user-identity
            //services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            ConfigureQuartz(services);
            services.AddSwaggerGen(c =>
                {
                    c.SwaggerDoc("v1", new Info {
                         Title = "Quartz.Web API",
                         Version = "alpha",
                         Description = "",
                         TermsOfService = "",
                         Contact = new Contact
                         {
                            Name = "",
                            Email = ""
                         },
                        });
                    //var filePath = Path.Combine(PlatformServices.Default.Application.ApplicationBasePath, "APIdocumentation.xml");
                    //c.IncludeXmlComments(filePath);
                    c.DescribeAllEnumsAsStrings();
                });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            
            app.UseMvc();
            
            app.UseSwagger();
            app.UseSwaggerUI(c =>
                {
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", "API V1");
                });
            
            app.UseDefaultFiles();
            app.UseStaticFiles();
        }


        void ConfigureQuartz(IServiceCollection services)
        {
            
            // First we must get a reference to a scheduler
            ISchedulerFactory sf = new StdSchedulerFactory();
            IScheduler scheduler = sf.GetScheduler().Result;

            // var liveLogPlugin = new LiveLogPlugin();
            // scheduler.ListenerManager.AddJobListener(liveLogPlugin);
            // scheduler.ListenerManager.AddTriggerListener(liveLogPlugin);
            // scheduler.ListenerManager.AddSchedulerListener(liveLogPlugin);

            scheduler.AddCalendar(typeof (AnnualCalendar).Name, new AnnualCalendar(), false, false);
            scheduler.AddCalendar(typeof (CronCalendar).Name, new CronCalendar("0 0/5 * * * ?"), false, false);
            scheduler.AddCalendar(typeof (DailyCalendar).Name, new DailyCalendar("12:01", "13:04"), false, false);
            scheduler.AddCalendar(typeof (HolidayCalendar).Name, new HolidayCalendar(), false, false);
            scheduler.AddCalendar(typeof (MonthlyCalendar).Name, new MonthlyCalendar(), false, false);
            //scheduler.AddCalendar(typeof (WeeklyCalendar).Name, new WeeklyCalendar(), false, false);

            services.AddSingleton<IScheduler>(scheduler);
        }
    }

    /*
    /// <summary>
    /// Initializes the Web API infrastructure.
    /// </summary>
    public class Startup
    {
        private static readonly ILog log = LogProvider.GetLogger(typeof (WebConsolePlugin));

        /// <summary>
        /// Con
        /// </summary>
        /// <param name="app"></param>
        public void Configuration(IAppBuilder app)
        {
            // Configure Web API for self-host. 
            HttpConfiguration config = new HttpConfiguration();

            config
                .EnableSwagger(c => c.SingleApiVersion("v1", "Quartz.NET Web API"))
                .EnableSwaggerUi();

            config.MapHttpAttributeRoutes();
            ConfigureJson(config);
            ConfigureFileServing(app);
            app.UseCors(CorsOptions.AllowAll);
            app.MapSignalR();

            app.UseWebApi(config);

            config.EnsureInitialized();
        }

        private void ConfigureFileServing(IAppBuilder appBuilder)
        {
            var appRoot = new DirectoryInfo(@"..\..\..\..\src\Quartz.Web\App");
            log.InfoFormat("Binding web console app root to {0}", appRoot.FullName);

            var physicalFileSystem = new PhysicalFileSystem(appRoot.FullName);
            var options = new FileServerOptions
            {
                EnableDefaultFiles = true,
                FileSystem = physicalFileSystem
            };
            options.StaticFileOptions.FileSystem = physicalFileSystem;
            options.StaticFileOptions.ServeUnknownFileTypes = true;
            options.DefaultFilesOptions.DefaultFileNames = new[] {"index.html"};
            appBuilder.UseFileServer(options);
        }

        private static void ConfigureJson(HttpConfiguration config)
        {
            // Web API
            config.Formatters.Clear();
            config.Formatters.Add(new JsonMediaTypeFormatter());
            var serializerSettings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            };
            serializerSettings.Converters.Add(new StringEnumConverter
            {
                CamelCaseText = true
            });
            config.Formatters.JsonFormatter.SerializerSettings = serializerSettings;
        }
    }
    */
}