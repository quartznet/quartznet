using System;
using System.Data;
using System.Globalization;

namespace Quartz.Util
{
    public static class DataReaderExtensions
    {
        public static string GetString(this IDataReader reader, string columnName)
        {
            object columnValue = reader[columnName];
            if (columnValue == DBNull.Value)
            {
                return null;
            }
            return (string)columnValue;
        }

        public static int GetInt32(this IDataReader reader, string columnName)
        {
            object columnValue = reader[columnName];
            return Convert.ToInt32(columnValue, CultureInfo.InvariantCulture);
        }

        public static long GetInt64(this IDataReader reader, string columnName)
        {
            object columnValue = reader[columnName];
            return Convert.ToInt64(columnValue, CultureInfo.InvariantCulture);
        }

        public static long? GetNullableInt64(this IDataReader reader, string columnName)
        {
            object columnValue = reader[columnName];
            if (columnValue == DBNull.Value)
            {
                return null;
            }
            return Convert.ToInt64(columnValue, CultureInfo.InvariantCulture);
        }

        public static decimal GetDecimal(this IDataReader reader, string columnName)
        {
            object columnValue = reader[columnName];
            return Convert.ToDecimal(columnValue,CultureInfo.InvariantCulture);
        }
    }
}