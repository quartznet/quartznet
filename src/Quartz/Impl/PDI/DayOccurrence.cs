//===============================================================================================================
// System  : Personal Data Interchange Classes
// File    : DayOccurrence.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 10/22/2014
// Note    : Copyright 2003-2014, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains the day occurrence enumerated type
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
    /// This enumerated type defines occurrences for days of the week within a month
    /// </summary>
    [Serializable]
    public enum DayOccurrence
    {
        /// <summary>No day occurrence set</summary>
        [LRDescription("DONone")]
        None,
        /// <summary>The first occurrence in the month</summary>
        [LRDescription("DOFirst")]
        First,
        /// <summary>The second occurrence in the month</summary>
        [LRDescription("DOSecond")]
        Second,
        /// <summary>The third occurrence in the month</summary>
        [LRDescription("DOThird")]
        Third,
        /// <summary>The fourth occurrence in the month</summary>
        [LRDescription("DOFourth")]
        Fourth,
        /// <summary>The last occurrence in the month</summary>
        [LRDescription("DOLast")]
        Last
    }
}
