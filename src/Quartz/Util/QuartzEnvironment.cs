using System;
using System.Collections;
using System.Collections.Generic;
using System.Security;

using Common.Logging;

namespace Quartz.Util
{
    /// <summary>
    /// Environment access helpers that fail gracefully if under medium trust.
    /// </summary>
    public static class QuartzEnvironment
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(QuartzEnvironment));
        private static readonly bool isRunningOnMono = Type.GetType("Mono.Runtime") != null;

        /// <summary>
        /// Return whether we are currently running under Mono runtime.
        /// </summary>
        public static bool IsRunningOnMono
        {
            get { return isRunningOnMono; }
        }

        /// <summary>
        /// Retrieves the value of an environment variable from the current process.
        /// </summary>
        public static string GetEnvironmentVariable(string key)
        {
            try
            {
                return Environment.GetEnvironmentVariable(key);
            }
            catch (SecurityException)
            {
                log.WarnFormat("Unable to read environment variable '{0}' due to security exception, probably running under medium trust", key);
            }
            return null;
        }

        /// <summary>
        /// Retrieves all environment variable names and their values from the current process.
        /// </summary>
        public static IDictionary<string, string> GetEnvironmentVariables()
        {
            var retValue = new Dictionary<string, string>();
            try
            {
                IDictionary variables = Environment.GetEnvironmentVariables();
                foreach (string key in variables.Keys)
                {
                    retValue[key] = variables[key] as string;
                }
            }
            catch (SecurityException)
            {
                log.WarnFormat("Unable to read environment variables due to security exception, probably running under medium trust");
            }
            return retValue;
        }
    }
}