using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;

namespace Quartz.Server;

/// <summary>
/// Factory class to create Quartz server implementations from.
/// </summary>
public class QuartzServerFactory
{
    private static readonly ILogger<QuartzServerFactory> logger = LogProvider.CreateLogger<QuartzServerFactory>();

    /// <summary>
    /// Creates a new instance of an Quartz.NET server core.
    /// </summary>
    /// <returns></returns>
    public static QuartzServer CreateServer()
    {
        string typeName = Configuration.ServerImplementationType;

        Type t = Type.GetType(typeName, true)!;

        logger.LogDebug("Creating new instance of server type '{Type}'", typeName);
        QuartzServer retValue = (QuartzServer) Activator.CreateInstance(t)!;
        logger.LogDebug("Instance successfully created");
        return retValue;
    }
}