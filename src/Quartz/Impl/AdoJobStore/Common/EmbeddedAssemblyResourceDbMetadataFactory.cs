using System;
using System.Collections.Generic;
using System.Collections.Specialized;

using Quartz.Util;

namespace Quartz.Impl.AdoJobStore.Common
{
    /// <summary>
    /// The DbMetadata factory based on embedded assembly resource
    /// </summary>
    public class EmbeddedAssemblyResourceDbMetadataFactory : DbMetadataFactory
    {
        private readonly string resourceName;
        private readonly string propertyGroupName;

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
        public override IEnumerable<string> GetProviderNames()
        {
            PropertiesParser pp = PropertiesParser.ReadFromEmbeddedAssemblyResource(resourceName);
            IEnumerable<string> result = pp.GetPropertyGroups(propertyGroupName);
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
                throw new InvalidConfigurationException(providerName + " provider is no longer supported.");
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
                throw new ArgumentException("Error while reading metadata information for provider '" + providerName + "'", "providerName", ex);
            }
        }
    }
}
