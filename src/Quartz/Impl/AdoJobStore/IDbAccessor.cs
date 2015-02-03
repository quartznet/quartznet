using System;
using System.Data;

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// Service interface or modifying <see cref="IDbCommand" /> parameters 
    /// and resultset values.
    /// </summary>
    public interface IDbAccessor
    {
        /// <summary>
        /// Prepares a <see cref="IDbCommand" /> to be used to access database.
        /// </summary>
        /// <param name="cth">Connection and transaction pair</param>
        /// <param name="commandText">SQL to run</param>
        /// <returns></returns>
        IDbCommand PrepareCommand(ConnectionAndTransactionHolder cth, string commandText);

        /// <summary>
        /// Adds a parameter to <see cref="IDbCommand" />.
        /// </summary>
        /// <param name="cmd">Command to add parameter to</param>
        /// <param name="paramName">Parameter's name</param>
        /// <param name="paramValue">Parameter's value</param>
        void AddCommandParameter(IDbCommand cmd, string paramName, object paramValue);

        /// <summary>
        /// Adds a parameter to <see cref="IDbCommand" />.
        /// </summary>
        /// <param name="cmd">Command to add parameter to</param>
        /// <param name="paramName">Parameter's name</param>
        /// <param name="paramValue">Parameter's value</param>
        /// <param name="dataType">Parameter's data type</param>
        void AddCommandParameter(IDbCommand cmd, string paramName, object paramValue, Enum dataType);

        /// <summary>
        /// Gets the db presentation for boolean value. Subclasses can overwrite this behaviour.
        /// </summary>
        /// <param name="booleanValue">Value to map to database.</param>
        /// <returns></returns>
        object GetDbBooleanValue(bool booleanValue);

        /// <summary>
        /// Gets the boolean value from db presentation. Subclasses can overwrite this behaviour.
        /// </summary>
        /// <param name="columnValue">Value to map from database.</param>
        /// <returns></returns>
        bool GetBooleanFromDbValue(object columnValue);

        /// <summary>
        /// Gets the db presentation for date/time value. Subclasses can overwrite this behaviour.
        /// </summary>
        /// <param name="dateTimeValue">Value to map to database.</param>
        /// <returns></returns>
        object GetDbDateTimeValue(DateTimeOffset? dateTimeValue);

        /// <summary>
        /// Gets the date/time value from db presentation. Subclasses can overwrite this behaviour.
        /// </summary>
        /// <param name="columnValue">Value to map from database.</param>
        /// <returns></returns>
        DateTimeOffset? GetDateTimeFromDbValue(object columnValue);

        /// <summary>
        /// Gets the db presentation for time span value. Subclasses can overwrite this behaviour.
        /// </summary>
        /// <param name="timeSpanValue">Value to map to database.</param>
        /// <returns></returns>
        object GetDbTimeSpanValue(TimeSpan? timeSpanValue);

        /// <summary>
        /// Gets the time span value from db presentation. Subclasses can overwrite this behaviour.
        /// </summary>
        /// <param name="columnValue">Value to map from database.</param>
        /// <returns></returns>
        TimeSpan? GetTimeSpanFromDbValue(object columnValue);
    }
}