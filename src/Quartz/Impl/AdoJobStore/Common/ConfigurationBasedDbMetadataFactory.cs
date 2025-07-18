using System.Collections.Specialized;

using Quartz.Util;

namespace Quartz.Impl.AdoJobStore.Common;

/// <summary>
/// The DbMetadata factory based on application configuration
/// </summary>
internal sealed class ConfigurationBasedDbMetadataFactory : DbMetadataFactory
{
    private readonly string propertyGroupName;
    private readonly NameValueCollection properties;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationBasedDbMetadataFactory" /> class.
    /// </summary>
    /// <param name="properties">Name of the configuration section.</param>
    /// <param name="propertyGroupName">The provider name prefix.</param>
    public ConfigurationBasedDbMetadataFactory(NameValueCollection properties, string propertyGroupName)
    {
        if (string.IsNullOrEmpty(propertyGroupName))
        {
            Throw.ArgumentNullException(nameof(propertyGroupName));
        }

        if (properties is null)
        {
            Throw.ArgumentNullException(nameof(properties));
        }

        this.properties = properties;
        this.propertyGroupName = propertyGroupName;
    }

    /// <summary>
    /// Gets the properties parser.
    /// </summary>
    /// <returns>The properties parser</returns>
    private PropertiesParser GetPropertiesParser()
    {
        var result = new PropertiesParser(properties);
        return result;
    }

    /// <summary>
    /// Gets the supported provider names.
    /// </summary>
    /// <returns>The enumeration of the supported provider names</returns>
    public override IReadOnlyCollection<string> GetProviderNames()
    {
        PropertiesParser pp = GetPropertiesParser();
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
        try
        {
            PropertiesParser pp = GetPropertiesParser();
            NameValueCollection props = pp.GetPropertyGroup(propertyGroupName + "." + providerName, true);
            DbMetadata metadata = new DbMetadata();

            ObjectUtils.SetObjectProperties(metadata, props);
            metadata.Init();

            return metadata;
        }
        catch (Exception ex)
        {
            Throw.ArgumentException("Error while reading metadata information for provider '" + providerName + "'", nameof(providerName), ex);
            return default!;
        }
    }
}