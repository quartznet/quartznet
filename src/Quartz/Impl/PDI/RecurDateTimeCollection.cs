//===============================================================================================================
// System  : Personal Data Interchange Classes
// File    : RecurDateTime.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 10/22/2014
// Note    : Copyright 2004-2014, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains a collection class for RecurDateTime objects
//
// This code is published under the Microsoft Public License (Ms-PL).  A copy of the license should be
// distributed with the code and can be found at the project website: https://github.com/EWSoftware/PDI.
// This notice, the author's name, and all copyright notices must remain intact in all applications,
// documentation, and source files.
//
//    Date     Who  Comments
// ==============================================================================================================
// 08/17/2004  EFW  Created the code
// 03/05/2007  EFW  Converted to use a generic base class
//===============================================================================================================

using System.Collections.Generic;

#nullable disable
namespace EWSoftware.PDI
{
    /// <summary>
	/// A type-safe collection of <see cref="RecurDateTime"/> objects
	/// </summary>
	internal class RecurDateTimeCollection : List<RecurDateTime>
	{
        #region Constructors
        //=====================================================================

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <overloads>There are two overloads for the constructor</overloads>
        public RecurDateTimeCollection()
        {
        }

        /// <summary>
        /// Construct a collection from an enumerable list of <see cref="RecurDateTime"/> objects
        /// </summary>
        /// <param name="recurDateTimes">The enumerable list of items to add</param>
        public RecurDateTimeCollection(IEnumerable<RecurDateTime> recurDateTimes)
        {
            if(recurDateTimes != null)
                this.AddRange(recurDateTimes);
        }
        #endregion
    }
}
