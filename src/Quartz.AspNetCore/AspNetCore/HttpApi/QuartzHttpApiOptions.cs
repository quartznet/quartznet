namespace Quartz.AspNetCore.HttpApi;

public class QuartzHttpApiOptions
{
    public string ApiPath { get; set; } = "/quartz-api";
    public bool IncludeStackTraceInProblemDetails { get; set; }

    internal string TrimmedApiPath => ApiPath.TrimEnd('/');
}