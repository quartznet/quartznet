using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

using Quartz.Serialization.SystemTextJson;

namespace Quartz.AspNetCore.HttpApi;

/// <summary>
/// Teaches the application's HTTP JSON options about Quartz's triggers, calendars and keys.
/// </summary>
/// <remarks>
/// A type rather than a lambda so that registering it is idempotent: the options are the whole
/// container's, and every <c>AddQuartzHttpApi</c> call wants the same converters on them.
/// </remarks>
internal sealed class QuartzJsonOptionsSetup : IConfigureOptions<JsonOptions>
{
    private readonly SystemTextJsonSerializerRegistry serializerRegistry;

    public QuartzJsonOptionsSetup(SystemTextJsonSerializerRegistry serializerRegistry)
    {
        this.serializerRegistry = serializerRegistry;
    }

    public void Configure(JsonOptions options)
    {
        options.SerializerOptions?.AddQuartzConverters(serializerRegistry, newtonsoftCompatibilityMode: false);
    }
}
