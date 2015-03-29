using System.IO;
using System.Net.Http.Formatting;
using System.Web.Http;

using Microsoft.Owin.Cors;
using Microsoft.Owin.FileSystems;
using Microsoft.Owin.StaticFiles;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

using Owin;

using Quartz.Logging;

using Swashbuckle.Application;

namespace Quartz.Web
{
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
            config.Formatters.JsonFormatter.SerializerSettings =
                serializerSettings;
        }
    }
}