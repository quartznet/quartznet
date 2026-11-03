using System.Data;
using System.Globalization;

namespace Quartz.Util;

/// <summary>
/// Extension methods for simplified <see cref="IDataReader" /> access.
/// </summary>
internal static class DataReaderExtensions
{
    /// <summary>
    /// Returns string from given column name, or null if DbNull.
    /// </summary>
    public static string? GetString(this IDataReader reader, string columnName)
    {
        object columnValue = reader[columnName];
        if (columnValue == DBNull.Value)
        {
            return null;
        }
        return (string) columnValue;
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
    /// <remarks>
    /// A driver that hands a <c>NUMERIC</c> column back as a double, as SQLite's does, has the double
    /// rounded the way it was before .NET 11 (<see cref="FloatingPointDecimals" />).
    /// </remarks>
    public static decimal GetDecimal(this IDataReader reader, string columnName)
    {
        object columnValue = reader[columnName];
        return FloatingPointDecimals.ToDecimal(columnValue);
    }
}