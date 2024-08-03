using Quartz.Spi;

namespace Quartz.Examples.AspNetCore;

public class CustomTypeLoader : ITypeLoadHelper
{
    private readonly ILogger<CustomTypeLoader> logger;

    public CustomTypeLoader(ILogger<CustomTypeLoader> logger)
    {
        this.logger = logger;
    }

    public void Initialize()
    {
    }

    public Type? LoadType(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        logger.LogInformation("Requested to load type {TypeName}", name);
        return Type.GetType(name);
    }
}
