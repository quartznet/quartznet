//===============================================================================================================
// System  : Personal Data Interchange Classes
// File    : EasterMethod.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 10/22/2014
// Note    : Copyright 2003-2014, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains the easter method enumerated type
//
// This code is published under the Microsoft Public License (Ms-PL).  A copy of the license should be
// distributed with the code and can be found at the project website: https://github.com/EWSoftware/PDI.
// This notice, the author's name, and all copyright notices must remain intact in all applications,
// documentation, and source files.
//
//    Date     Who  Comments
// ==============================================================================================================
// 08/18/2003  EFW  Created the code
//===============================================================================================================

using System;

#nullable disable
namespace EWSoftware.PDI
{
    /// <summary>
    /// This enumerated type defines the various ways to calculate Easter
    /// </summary>
    [Serializable]
    public enum EasterMethod
    {
        /// <summary>Calculate Easter as the Sunday following the Paschal Full Moon (PFM) date for the year based
        /// on the Julian Calendar.  This method is valid for all years from 326 onward.</summary>
        Julian,
        /// <summary>This method is the same as the Julian method but converts the Julian calendar date to the
        /// equivalent Gregorian calendar date. This method is valid for all years from 1583 to 4099.</summary>
        Orthodox,
        /// <summary>Calculate Easter as the Sunday following the Paschal Full Moon (PFM) date for the year based
        /// on the Gregorian Calendar.  This method is valid for all years from 1583 to 4099.</summary>
        Gregorian
    }
}
