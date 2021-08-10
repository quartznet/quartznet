//===============================================================================================================
// System  : Personal Data Interchange Classes
// File    : RecurFrequency.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 10/22/2014
// Note    : Copyright 2003-2014, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains the recurrence frequency enumerated type
//
// This code is published under the Microsoft Public License (Ms-PL).  A copy of the license should be
// distributed with the code and can be found at the project website: https://github.com/EWSoftware/PDI.
// This notice, the author's name, and all copyright notices must remain intact in all applications,
// documentation, and source files.
//
//    Date     Who  Comments
// ==============================================================================================================
// 08/12/2004  EFW  Created the code
//===============================================================================================================

using System;

#nullable disable
namespace EWSoftware.PDI
{
    /// <summary>
    /// This enumerated type defines the recurrence frequency
    /// </summary>
    [Serializable]
    public enum RecurFrequency
    {
        /// <summary>Recurrence pattern is undefined and returns no dates</summary>
        Undefined,
        /// <summary>Yearly recurrence</summary>
        Yearly,
        /// <summary>Monthly recurrence</summary>
        Monthly,
        /// <summary>Weekly recurrence</summary>
        Weekly,
        /// <summary>Daily recurrence</summary>
        Daily,
        /// <summary>Hourly recurrence</summary>
        Hourly,
        /// <summary>Minutely recurrence</summary>
        Minutely,
        /// <summary>Secondly recurrence</summary>
        Secondly
    }
}
