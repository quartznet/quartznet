using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

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
        JsonSerializerOptions? serializerOptions = options.SerializerOptions;
        if (serializerOptions is null)
        {
            return;
        }

        serializerOptions.ConfigureWireFormat(serializerRegistry);

        // Last, behind reflection: HttpApiProblemDetailsJsonContext says why.
        IList<IJsonTypeInfoResolver> chain = serializerOptions.TypeInfoResolverChain;
        if (!chain.Contains(HttpApiProblemDetailsJsonContext.Default))
        {
            chain.Add(HttpApiProblemDetailsJsonContext.Default);
        }
    }
}
