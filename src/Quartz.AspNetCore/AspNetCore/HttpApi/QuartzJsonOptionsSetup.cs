using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

using Quartz.HttpApiContract;
using Quartz.Serialization.SystemTextJson;

namespace Quartz.AspNetCore.HttpApi;

/// <summary>
/// Teaches the application's HTTP JSON options about Quartz's triggers, calendars, keys and wire enums,
/// and about the problem details its errors are written as.
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
        options.SerializerOptions.ConfigureWireFormat(serializerRegistry);

        // Last, behind reflection: HttpApiProblemDetailsJsonContext says why. Added without looking for it
        // first, because nothing runs this twice on one instance: AddQuartzHttpApi registers the setup with
        // TryAddEnumerable, and the options factory configures each instance once.
        options.SerializerOptions.TypeInfoResolverChain.Add(HttpApiProblemDetailsJsonContext.Default);
    }
}
