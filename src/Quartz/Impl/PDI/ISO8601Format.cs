//===============================================================================================================
// System  : Personal Data Interchange Classes
// File    : ISO8601Format.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 10/22/2014
// Note    : Copyright 2003-2014, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains a string "enumerated" type for formatting date/time values in the various ISO 8601 formats
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

// Ignore Spelling: utc

#nullable disable
namespace EWSoftware.PDI
{
    /// <summary>
    /// A string "enumerated" type for formatting date/time values in the various ISO 8601 formats
    /// </summary>
    /// <remarks>These are just format strings.  It is up to you to make sure that date/time values are in local
    /// time or universal time.</remarks>
    /// <example>
    /// <code language="cs">
    /// string utcText, localTimeText;
    /// DateTime utc = DateTime.Now.ToUniversalTime();
    /// DateTime localTime = DateTime.Now;
    ///
    /// utcText = utc.ToString(ISO8601Format.BasicDateTimeUniversal);
    /// localTimeText= localTime.ToString(ISO8601Format.ExtendedDateTimeLocal);
    /// </code>
    /// <code language="vbnet">
    /// Dim utcText, localTimeText As String
    /// Dim utc As DateTime = DateTime.Now.ToUniversalTime()
    /// Dim localTime As DateTime = DateTime.Now
    ///
    /// utcText = utc.ToString(ISO8601Format.BasicDateTimeUniversal)
    /// localTimeText = localTime.ToString(ISO8601Format.ExtendedDateTimeLocal)
    /// </code>
    /// </example>
    public static class ISO8601Format
    {
        /// <summary>Basic date format (yyyyMMdd)</summary>
        public const string BasicDate = "yyyyMMdd";
        /// <summary>Basic local date/time format (yyyyMMddTHHmmss)</summary>
        public const string BasicDateTimeLocal = "yyyyMMddTHHmmss";
        /// <summary>Basic universal date/time format (yyyyMMddTHHmmssZ)</summary>
        public const string BasicDateTimeUniversal = "yyyyMMddTHHmmssZ";
        /// <summary>Extended date format (yyyy-MM-dd)</summary>
        public const string ExtendedDate = "yyyy-MM-dd";
        /// <summary>Extended local date/time format (yyyy-MM-ddTHH:mm:ss)</summary>
        public const string ExtendedDateTimeLocal = "yyyy-MM-ddTHH:mm:ss";
        /// <summary>Extended universal date/time format (yyyy-MM-ddTHH:mm:ssZ)</summary>
        public const string ExtendedDateTimeUniversal = "yyyy-MM-ddTHH:mm:ssZ";
    }
}
