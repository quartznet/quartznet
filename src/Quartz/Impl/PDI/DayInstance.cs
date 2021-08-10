//===============================================================================================================
// System  : Personal Data Interchange Classes
// File    : DayInstance.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 11/22/2018
// Note    : Copyright 2003-2018, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains a class used to specify day instances for the BYDAY rule of a recurrence object.  The class
// is serializable.
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
using System.Globalization;
using System.Runtime.Serialization;
using System.Security;

#nullable disable
namespace EWSoftware.PDI
{
    /// <summary>
    /// A day instance item.  The properties of this class define a day of the week instance on which a
    /// recurrence date may fall.
    /// </summary>
    /// <remarks>The day instance can be generic specifying that all instances are included (i.e. all Mondays) or
    /// it can refer to a specific instance (i.e. the second Monday, the third from last Tuesday, etc).</remarks>
    [Serializable]
    public class DayInstance : ISerializable
    {
        #region Private data members
        //=====================================================================

        // This is used to convert the instance to its string form.  This is convenient for generating its
        // iCalendar representation.
        private static readonly string[] abbrevDays = { "SU", "MO", "TU", "WE", "TH", "FR", "SA" };

        private int instance;
        private DayOfWeek dow;

        #endregion

        #region Properties
        //=====================================================================

        /// <summary>
        /// This property is used to get or set the instance value for the day
        /// </summary>
        /// <value>A value of zero indicates all instances of the day of the week should be included.  A negative
        /// value indicates that the instance is derived by counting from the end of the month or year.  A
        /// positive value indicates that the instance is derived by counting from the start of the month or
        /// year.</value>
        /// <exception cref="ArgumentOutOfRangeException">This is thrown if the instance value is less than -53
        /// or greater than 53.</exception>
        public int Instance
        {
            get => instance;
            set
            {
                // The spec doesn't say it, but you can imply that you can't have a value higher than 53 as these
                // do roughly correspond to week numbers.
                if(value < -53 || value > 53)
                    throw new ArgumentOutOfRangeException(nameof(value), value, LR.GetString("ExDIBadInstanceValue"));

                instance = value;
            }
        }

        /// <summary>
        /// This property is used to get or set the week day used for the instance
        /// </summary>
        public DayOfWeek DayOfWeek
        {
            get => dow;
            set
            {
                if(!Enum.IsDefined(typeof(DayOfWeek), value))
                    throw new ArgumentOutOfRangeException(nameof(value), value, LR.GetString("ExDIInvalidDayOfWeek"));

                dow = value;
            }
        }
        #endregion

        #region Constructors
        //=====================================================================

        /// <summary>
        /// Constructor.  This version constructs a day instance that specifies a specific occurrence of the day
        /// of the week.
        /// </summary>
        /// <param name="instance">The instance of the day of the week</param>
        /// <param name="dow">The day of the week</param>
        /// <overloads>There are four constructors for this class.</overloads>
        public DayInstance(int instance, DayOfWeek dow)
        {
            this.Instance = instance;
            this.DayOfWeek = dow;
        }

        /// <summary>
        /// Constructor.  This version constructs a day instance that specifies only the day of the week.  This
        /// indicates that all instances of the day of the week are to be included.
        /// </summary>
        /// <param name="dow">The day of the week</param>
        public DayInstance(DayOfWeek dow) : this(0, dow)
        {
        }

        /// <summary>
        /// Copy constructor
        /// </summary>
        /// <param name="di">The day instance to copy</param>
        public DayInstance(DayInstance di)
        {
            if(di != null)
            {
                this.Instance = di.Instance;
                this.DayOfWeek = di.DayOfWeek;
            }
        }

        /// <summary>
        /// Deserialization constructor for use with <see cref="System.Runtime.Serialization.ISerializable"/>
        /// </summary>
        /// <param name="info">The serialization info object</param>
        /// <param name="context">The streaming context object</param>
        protected DayInstance(SerializationInfo info, StreamingContext context)
        {
            if(info != null)
            {
                this.Instance = (int)info.GetValue("Instance", typeof(int));
                this.DayOfWeek = (DayOfWeek)info.GetValue("DayOfWeek", typeof(DayOfWeek));
            }
        }
        #endregion

        #region Methods
        //=====================================================================

        /// <summary>
        /// This is overridden to allow proper comparison of DayInstance objects
        /// </summary>
        /// <param name="obj">The object to which this instance is compared</param>
        /// <returns>Returns true if the object equals this instance, false if it does not</returns>
        public override bool Equals(object obj)
        {
            if(!(obj is DayInstance d))
                return false;

            return (this == d || (this.Instance == d.Instance && this.DayOfWeek == d.DayOfWeek));
        }

        /// <summary>
        /// Get a hash code for the day instance object
        /// </summary>
        /// <returns>Returns the hash code for the day instance object</returns>
        public override int GetHashCode()
        {
            return this.ToString().GetHashCode();
        }

        /// <summary>
        /// Convert the day instance to its string description
        /// </summary>
        /// <returns>Returns the string version of the day instance suitable for saving to a PDI data stream
        /// (i.e. MO, -1TU, 5WE).</returns>
        public override string ToString()
        {
            if(this.Instance == 0)
                return abbrevDays[(int)this.DayOfWeek];

            return $"{this.Instance}{abbrevDays[(int)this.DayOfWeek]}";
        }

        /// <summary>
        /// Get a description of the day instance
        /// </summary>
        /// <returns>A string describing the day instance (i.e. 1st Monday from end).</returns>
        public string ToDescription()
        {
            string suffix = DayInstance.NumericSuffix(instance);

            if(instance == 0)
                return $"{LR.GetString("DIAny")} {CultureInfo.CurrentCulture.DateTimeFormat.DayNames[(int)dow]}";

            if(instance < 0)
                return $"{instance * -1}{suffix} {CultureInfo.CurrentCulture.DateTimeFormat.DayNames[(int)dow]} {LR.GetString("DIFromEnd")}";

            return $"{instance}{suffix} {CultureInfo.CurrentCulture.DateTimeFormat.DayNames[(int)dow]}";
        }

        /// <summary>
        /// This is used to get the descriptive suffix for a number (i.e. "st" for 1st, "nd" for 2nd, etc).
        /// </summary>
        /// <param name="number">The number for which to get the suffix</param>
        /// <returns>The number as a string with the appropriate suffix</returns>
        /// <remarks>It is static so that it can be shared with other classes that need it</remarks>
        public static string NumericSuffix(int number)
        {
            int digits, idx;
            string suffix;

            digits = (number % 100) * ((number < 0) ? -1 : 1);

            if((digits >= 10 && digits <= 19) || digits % 10 == 0)
                idx = 4;
            else
                idx = digits % 10;

            switch(idx)
            {
                case 1:
                    suffix = LR.GetString("DIFirst");
                    break;

                case 2:
                    suffix = LR.GetString("DISecond");
                    break;

                case 3:
                    suffix = LR.GetString("DIThird");
                    break;

                default:
                    suffix = LR.GetString("DINth");
                    break;
            }

            return suffix;
        }
        #endregion

        #region ISerializable implementation
        //=====================================================================

        /// <summary>
        /// This implements the <see cref="System.Runtime.Serialization.ISerializable"/> interface and adds data
        /// to the serialization info.
        /// </summary>
        /// <param name="info">The serialization info object</param>
        /// <param name="context">The streaming context</param>
        [SecurityCritical]
        public virtual void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if(info != null)
            {
                info.AddValue("Instance", this.Instance);
                info.AddValue("DayOfWeek", this.DayOfWeek);
            }
        }
        #endregion
    }
}
