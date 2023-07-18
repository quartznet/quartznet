//===============================================================================================================
// System  : Personal Data Interchange Classes
// File    : LocalizedResources.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 11/22/2018
// Note    : Copyright 2004-2018, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains some internal classes used to manage the localized resources for the assembly
//
// This code is published under the Microsoft Public License (Ms-PL).  A copy of the license should be
// distributed with the code and can be found at the project website: https://github.com/EWSoftware/PDI.
// This notice, the author's name, and all copyright notices must remain intact in all applications,
// documentation, and source files.
//
//    Date     Who  Comments
// ==============================================================================================================
// 01/24/2004  EFW  Created the code
//===============================================================================================================

using System;
using System.Reflection;
using System.Globalization;
using System.Resources;

#nullable disable
namespace EWSoftware.PDI
{
    /// <summary>
    /// This class is used to load resources for the assembly
    /// </summary>
    internal static class LR
    {
        #region Private data members
        //=====================================================================

        // This constant defines the key for the resources.  This is combined with the assembly name to get the
        // resources key name within it.
        private const string ResourcesKey = "PDI";

        // The resource manager
        private static ResourceManager rm;

        // This is a helper object used to quickly lock the class when creating the resource manager
        private static readonly object syncRoot = new Object();

        #endregion

        #region Properties
        //=====================================================================

        /// <summary>
        /// This private, read-only property is used to instantiate the resource manager object on first use
        /// </summary>
        private static ResourceManager Resources
        {
            get
            {
                if(rm == null)
                {
                    lock(syncRoot)
                    {
                        if(rm == null)
                        {
                            Assembly asm = Assembly.GetExecutingAssembly();

                            rm = new ResourceManager($"{asm.GetName().Name}.{ResourcesKey}", asm);
                        }
                    }
                }

                return rm;
            }
        }
        #endregion

        #region Methods
        //=====================================================================

        /// <summary>
        /// This method is used to retrieve the value of a string resource
        /// </summary>
        /// <param name="name">The name of the string resource to get</param>
        /// <returns>Returns the value of the string resource if found or the key name enclosed in
        /// "[?:&lt;key&gt;]" if not found.</returns>
        internal static string GetString(string name)
        {
            string s = Resources.GetString(name, null);

            if(s == null)
                s = $"[?:{name}]";

            return s;
        }

        /// <summary>
        /// This method is used to retrieve the value of a string resource that contains formatting placeholders
        /// </summary>
        /// <param name="name">The name of the string resource to get</param>
        /// <param name="args">The arguments to be formatted into the retrieved string</param>
        /// <returns>Returns the value of the string resource formatted with the passed arguments if found or the
        /// key name enclosed in "[?:&lt;key&gt;]" if not found.</returns>
        internal static string GetString(string name, params object[] args)
        {
            return String.Format(CultureInfo.CurrentCulture, GetString(name), args);
        }

        /// <summary>
        /// This can be called to get the localized description for an enumerated type value
        /// </summary>
        /// <param name="value">The value for which to retrieve a description</param>
        /// <returns>The description retrieved from the resources if found or the ToString() representation of
        /// the value if not found.</returns>
        internal static string EnumDesc(Enum value)
        {
            FieldInfo fi = value.GetType().GetField(value.ToString());

            LRDescriptionAttribute[] attributes = (LRDescriptionAttribute[])fi.GetCustomAttributes(
                typeof(LRDescriptionAttribute), false);

            return (attributes.Length > 0) ? attributes[0].Description : value.ToString();
        }
        #endregion
    }
}
