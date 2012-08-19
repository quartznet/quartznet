#region License
/* 
 * Copyright 2009- Terracotta, Inc. 
 * 
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not 
 * use this file except in compliance with the License. You may obtain a copy 
 * of the License at 
 * 
 *   http://www.apache.org/licenses/LICENSE-2.0 
 *   
 * Unless required by applicable law or agreed to in writing, software 
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT 
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the 
 * License for the specific language governing permissions and limitations 
 * under the License.
 * 
 */
#endregion

using System;
using System.IO;
using System.Security;
#if !ClientProfile
using System.Web;
#endif

using Common.Logging;

namespace Quartz.Util
{
    /// <summary>
    /// Utility class for file handling related things.
    /// </summary>
    /// <author>Marko Lahma</author>
    public class FileUtil
    {
        private static readonly ILog logger = LogManager.GetLogger<FileUtil>();

        /// <summary>
        /// Resolves file to actual file if for example relative '~' used.
        /// </summary>
        /// <param name="fName">File name to check</param>
        /// <returns>Expanded file name or actual no resolving was done.</returns>
        public static string ResolveFile(string fName)
        {
            if (fName != null && fName.StartsWith("~"))
            {
                // relative to run directory

#if !ClientProfile
                // if HttpContext available, use it
                if (HttpContext.Current != null)
                {
                    return HttpContext.Current.Server.MapPath(fName);
                }
#endif

                fName = fName.Substring(1);
                if (fName.StartsWith("/") || fName.StartsWith("\\"))
                {
                    fName = fName.Substring(1);
                }
                try
                {
                    fName = Path.Combine(AppDomain.CurrentDomain.SetupInformation.ApplicationBase, fName);
                }
                catch (SecurityException)
                {
                    logger.WarnFormat("Cannot determine path for relative file '{0}' because of security exception");
                    throw;
                }
            }

            return fName;
        }
    }
}