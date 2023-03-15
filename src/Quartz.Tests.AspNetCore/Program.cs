using Microsoft.AspNetCore.Builder;

using Quartz.AspNetCore;

namespace Quartz.Tests.AspNetCore;

// Simple web server used to run endpoints during testing
public class Program
{
    public static void Main()
    {
        var builder = WebApplication.CreateBuilder();

        builder.Services.AddQuartz(configurator =>
        {
            configurator.AddHttpApi(options => options.ApiPath = "/");
        });

        var app = builder.Build();

        app.MapQuartzApi();
        app.Run();
    }
}