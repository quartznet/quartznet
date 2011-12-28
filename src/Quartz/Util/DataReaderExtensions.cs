using System;
using System.Data;
using System.Globalization;

namespace Quartz.Util
{
    /// <summary>
    /// Extension methods for simplified <see cref="IDataReader" /> access.
    /// </summary>
    public static class DataReaderExtensions
    {
        /// <summary>
        /// Returns string from given column name, or null if DbNull.
        /// </summary>
        public static string GetString(this IDataReader reader, string columnName)
        {
            object columnValue = reader[columnName];
            if (columnValue == DBNull.Value)
            {
                return null;
            }
            return (string)columnValue;
        }

        /// <summary>
        /// Returns int from given column name.
        /// </summary>
        public static int GetInt32(this IDataReader reader, string columnName)
        {
            object columnValue = reader[columnName];
            return Convert.ToInt32(columnValue, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Returns long from given column name.
        /// </summary>
        public static long GetInt64(this IDataReader reader, string columnName)
        {
            object columnValue = reader[columnName];
            return Convert.ToInt64(columnValue, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Returns long from given column name, or null if DbNull.
        /// </summary>
        public static long? GetNullableInt64(this IDataReader reader, string columnName)
        {
            object columnValue = reader[columnName];
            if (columnValue == DBNull.Value)
            {
                return null;
            }
            return Convert.ToInt64(columnValue, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Returns decimal from given column name.
        /// </summary>
        public static decimal GetDecimal(this IDataReader reader, string columnName)
        {
            object columnValue = reader[columnName];
            return Convert.ToDecimal(columnValue,CultureInfo.InvariantCulture);
        }
    }
}