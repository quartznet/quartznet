using Microsoft.AspNetCore.Builder;


namespace Quartz.Tests.AspNetCore;

// Simple web server used to run endpoints during testing
public class Program
{
    public static void Main()
    {
        var builder = WebApplication.CreateBuilder();

        builder.Services.AddQuartz(configurator =>
        {
            configurator.AddQuartzHttpApi(options => options.ApiPath = "/");
        });

        var app = builder.Build();

        app.MapQuartzHttpApi();
        app.Run();
    }
}