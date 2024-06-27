using System.Collections.Specialized;

using Quartz.Util;

namespace Quartz.Impl.AdoJobStore.Common;

/// <summary>
/// The DbMetadata factory based on embedded assembly resource
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="EmbeddedAssemblyResourceDbMetadataFactory"/> class.
/// </remarks>
/// <param name="resourceName">Name of the resource.</param>
/// <param name="propertyGroupName">Name of the property group (The prefix of the provider name).</param>
internal sealed class EmbeddedAssemblyResourceDbMetadataFactory(string resourceName, string propertyGroupName)
    : DbMetadataFactory
{
    private readonly string resourceName = resourceName;
    private readonly string propertyGroupName = propertyGroupName;

    /// <summary>
    /// Gets the supported provider names.
    /// </summary>
    /// <returns>The enumeration of the supported provider names</returns>
    public override IReadOnlyCollection<string> GetProviderNames()
    {
        PropertiesParser pp = PropertiesParser.ReadFromEmbeddedAssemblyResource(resourceName);
        var result = pp.GetPropertyGroups(propertyGroupName);

        return result;
    }

    /// <summary>
    /// Gets the database metadata associated to the specified provider name.
    /// </summary>
    /// <param name="providerName">Name of the provider.</param>
    /// <returns>The metadata instance for the specified name</returns>
    public override DbMetadata GetDbMetadata(string providerName)
    {
        List<string> deprecatedProviders = new List<string>
        {
            "Npgsql-10",
            "SqlServer-11"
        };

        if (deprecatedProviders.Contains(providerName))
        {
            ThrowHelper.ThrowInvalidConfigurationException(providerName + " provider is no longer supported.");
        }

        try
        {
            PropertiesParser pp = PropertiesParser.ReadFromEmbeddedAssemblyResource(resourceName);
            NameValueCollection props = pp.GetPropertyGroup(propertyGroupName + "." + providerName, true);
            DbMetadata metadata = new DbMetadata();

            ObjectUtils.SetObjectProperties(metadata, props);
            metadata.Init();

            return metadata;
        }
        catch (Exception ex)
        {
            ThrowHelper.ThrowArgumentException("Error while reading metadata information for provider '" + providerName + "'", nameof(providerName), ex);

            return default!;
        }
    }
}