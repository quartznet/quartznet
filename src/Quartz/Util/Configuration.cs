using System;
using System.Collections.Specialized;
using System.Configuration;

using Quartz.Logging;

namespace Quartz.Util
{
    internal static class Configuration
    {
        private static readonly ILog log = LogProvider.GetLogger(typeof(Configuration));
        
        internal static NameValueCollection? GetSection(string sectionName)
        {
            try
            {
                return (NameValueCollection) ConfigurationManager.GetSection(sectionName);
            }
            catch (Exception e)
            {
                log.Warn("could not read configuration using ConfigurationManager.GetSection: " + e.Message);
                return null;
            }
        }
    }
}