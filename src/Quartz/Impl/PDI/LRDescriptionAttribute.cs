//===============================================================================================================
// System  : Personal Data Interchange Classes
// File    : LRDescriptionAttribute.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 10/22/2014
// Note    : Copyright 2004-2014, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains a custom attribute used on the enumerated types within the assembly to return more
// user-friendly descriptions for the values.
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

#nullable disable
namespace EWSoftware.PDI
{
    /// <summary>
    /// This custom attribute is used on the enumerated types within the assembly to return more user-friendly
    /// descriptions for the values rather than their default ToString() representations.  It also allows the
    /// descriptions to be localized for different languages.
    /// </summary>
    [AttributeUsage(AttributeTargets.All)]
    internal sealed class LRDescriptionAttribute : System.ComponentModel.DescriptionAttribute
    {
        #region Private data members
        //=====================================================================

        // This is used to indicate whether or not the localized description needs to be retrieved
        private bool replaced;

        #endregion

        #region Properties
        //=====================================================================

        /// <summary>
        /// The description property is overridden to retrieve the localized description string on first use
        /// </summary>
        public override string Description
        {
            get
            {
                if(!replaced)
                {
                    this.DescriptionValue = LR.GetString(base.Description);
                    replaced = true;
                }

                return base.Description;
            }
        }
        #endregion

        #region Constructor
        //=====================================================================

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="descriptionKey">The string resource key name</param>
        public LRDescriptionAttribute(string descriptionKey) : base(descriptionKey)
        {
        }
        #endregion
    }
}
