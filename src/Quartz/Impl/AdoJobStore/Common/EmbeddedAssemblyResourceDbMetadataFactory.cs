using System.Collections.Specialized;

using Quartz.Configuration;
using Quartz.Util;

namespace Quartz.Impl.AdoJobStore.Common;

/// <summary>
/// The DbMetadata factory based on embedded assembly resource
/// </summary>
internal sealed class EmbeddedAssemblyResourceDbMetadataFactory : DbMetadataFactory
{
    /// <summary>
    /// The resource holding the driver descriptions Quartz ships.
    /// </summary>
    internal const string DefaultResourceName = "Quartz.Impl.AdoJobStore.Common.dbproviders.netstandard.properties";

    private readonly string resourceName;
    private readonly string propertyGroupName;

    /// <summary>
    /// Initializes a new instance reading the driver descriptions Quartz ships.
    /// </summary>
    public EmbeddedAssemblyResourceDbMetadataFactory()
        : this(DefaultResourceName, LegacyPropertyKeys.DbProvider)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EmbeddedAssemblyResourceDbMetadataFactory"/> class.
    /// </summary>
    /// <param name="resourceName">Name of the resource.</param>
    /// <param name="propertyGroupName">Name of the property group (The prefix of the provider name).</param>
    public EmbeddedAssemblyResourceDbMetadataFactory(string resourceName, string propertyGroupName)
    {
        this.resourceName = resourceName;
        this.propertyGroupName = propertyGroupName;
    }

    /// <summary>
    /// Gets the supported provider names.
    /// </summary>
    /// <returns>The enumeration of the supported provider names</returns>
    public override List<string> GetProviderNames()
    {
        PropertiesParser pp = PropertiesParser.ReadFromEmbeddedAssemblyResource(resourceName);
        IReadOnlyList<string> result = pp.GetPropertyGroups(propertyGroupName);
        return new List<string>(result);
    }

    /// <summary>
    /// Gets the database metadata associated to the specified provider name.
    /// </summary>
    /// <param name="providerName">Name of the provider.</param>
    /// <returns>The metadata instance for the specified name</returns>
    public override DbMetadata GetDbMetadata(string providerName)
    {
        List<string> deprecatedProviders =
        [
            "Npgsql-10",
            "SqlServer-11"
        ];

        if (deprecatedProviders.Contains(providerName))
        {
            Throw.InvalidConfigurationException(providerName + " provider is no longer supported.");
        }

        try
        {
            PropertiesParser pp = PropertiesParser.ReadFromEmbeddedAssemblyResource(resourceName);
            NameValueCollection props = pp.GetPropertyGroup(propertyGroupName + "." + providerName, true);
            DbMetadata metadata = new DbMetadata();

            ObjectUtils.SetObjectProperties(metadata, props);
            metadata.Validate();

            return metadata;
        }
        catch (Exception ex)
        {
            Throw.ArgumentException("Error while reading metadata information for provider '" + providerName + "'", nameof(providerName), ex);
            return default!;
        }
    }
}