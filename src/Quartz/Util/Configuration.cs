using System.Collections.Specialized;
using System.Configuration;

using Microsoft.Extensions.Logging;

using Quartz.Logging;

namespace Quartz.Util;

internal static class Configuration
{
    internal static NameValueCollection? GetSection(string sectionName)
    {
#if NETFRAMEWORK
        try
        {
            return (NameValueCollection) ConfigurationManager.GetSection(sectionName);
        }
        catch (Exception e)
        {
            var logger = LogProvider.CreateLogger(nameof(Configuration));
            logger.LogWarning(e,"could not read configuration using ConfigurationManager.GetSection: {ExceptionMessage}",e.Message);
            return null;
        }
#else
        return null;
#endif
    }
}