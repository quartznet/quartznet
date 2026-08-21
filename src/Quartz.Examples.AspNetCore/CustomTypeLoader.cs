using Quartz.Extensibility;

namespace Quartz.Examples.AspNetCore;

public class CustomTypeLoader : ITypeLoader
{
    private readonly ILogger<CustomTypeLoader> logger;

    public CustomTypeLoader(ILogger<CustomTypeLoader> logger)
    {
        this.logger = logger;
    }

    public Type? LoadType(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        logger.LogInformation("Requested to load type {TypeName}", name);

        // Throwing rather than returning null is the contract: Quartz only asks for types it needs.
        return Type.GetType(name, throwOnError: true);
    }
}
