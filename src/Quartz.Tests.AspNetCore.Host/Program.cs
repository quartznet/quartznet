using Microsoft.AspNetCore.Builder;


namespace Quartz.Tests.AspNetCore;

// Simple web server used to run endpoints during testing
public class Program
{
    public static void Main()
    {
        var builder = WebApplication.CreateBuilder();

        builder.Services.AddQuartz();
        builder.Services.AddQuartzHttpApi(options => options.ApiPath = "/");

        var app = builder.Build();

        // Anonymous on purpose: this host exists to exercise the wire contract, and the endpoints have
        // to answer without a user for that. Authorization has its own tests, which build their own
        // hosts. Saying nothing here would be refused at startup, which is the point of saying it.
        app.MapQuartzHttpApi().AllowAnonymous();
        app.Run();
    }
}