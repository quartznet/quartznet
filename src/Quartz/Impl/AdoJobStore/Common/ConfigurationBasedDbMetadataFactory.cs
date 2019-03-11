using System;
using System.Collections.Generic;
using System.Collections.Specialized;

using Quartz.Util;

namespace Quartz.Impl.AdoJobStore.Common
{
    /// <summary>
    /// The DbMetadata factory based on application configuration
    /// </summary>
    public class ConfigurationBasedDbMetadataFactory : DbMetadataFactory
    {
        private readonly string sectionName;
        private readonly string providerNamePrefix;

        /// <summary>
        /// Initializes a new instance of the <see cref="EmbeddedAssemblyResourceDbMetadataFactory" /> class.
        /// </summary>
        /// <param name="sectionName">Name of the configuration section.</param>
        /// <param name="providerNamePrefix">The provider name prefix.</param>
        /// <exception cref="System.ArgumentNullException">The providerNamePrefix cannot be null or empty.</exception>
        public ConfigurationBasedDbMetadataFactory(string sectionName, string providerNamePrefix)
        {
            if (string.IsNullOrEmpty(providerNamePrefix))
            {
                throw new ArgumentNullException(nameof(providerNamePrefix));
            }

            this.sectionName = sectionName;
            this.providerNamePrefix = providerNamePrefix;
        }

        /// <summary>
        /// Gets the properties parser.
        /// </summary>
        /// <returns>The properties parser</returns>
        protected virtual PropertiesParser GetPropertiesParser()
        {
            var settings = Util.Configuration.GetSection(sectionName) ?? new NameValueCollection();
            var result = new PropertiesParser(settings);
            return result;
        }

        /// <summary>
        /// Gets the supported provider names.
        /// </summary>
        /// <returns>The enumeration of the supported provider names</returns>
        public override IReadOnlyCollection<string> GetProviderNames()
        {
            PropertiesParser pp = GetPropertiesParser();
            var result = pp.GetPropertyGroups(providerNamePrefix);
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
                NameValueCollection props = pp.GetPropertyGroup(providerNamePrefix + "." + providerName, true);
                DbMetadata metadata = new DbMetadata();

                ObjectUtils.SetObjectProperties(metadata, props);
                metadata.Init();

                return metadata;
            }
            catch (Exception ex)
            {
                throw new ArgumentException("Error while reading metadata information for provider '" + providerName + "'", nameof(providerName), ex);
            }
        }
    }
}