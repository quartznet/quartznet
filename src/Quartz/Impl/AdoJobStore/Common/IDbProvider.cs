using System;
using System.Data;

namespace Quartz.Impl.AdoJobStore
{
	/// <summary>
	/// 
	/// </summary>
	public interface IDbProvider
	{
        
		/// <summary>
		/// Returns a new command object for executing SQL statments/Stored Procedures
		/// against the database.
		/// </summary>
		/// <returns>An new <see cref="IDbCommand"/></returns>
		IDbCommand CreateCommand();

		/// <summary>
		/// Returns a new instance of the providers CommandBuilder class.
		/// </summary>
		/// <remarks>In .NET 1.1 there was no common base class or interface
		/// for command builders, hence the return signature is object to
		/// be portable (but more loosely typed) across .NET 1.1/2.0</remarks>
		/// <returns>A new Command Builder</returns>
		object CreateCommandBuilder();        
        
		/// <summary>
		/// Returns a new connection object to communicate with the database.
		/// </summary>
		/// <returns>A new <see cref="IDbConnection"/></returns>
		IDbConnection CreateConnection();


		/// <summary>
		/// Returns a new adapter objects for use with offline DataSets.
		/// </summary>
		/// <returns>A new <see cref="IDbDataAdapter"/></returns>
		IDbDataAdapter CreateDataAdapter();
        
		/// <summary>
		/// Returns a new parameter object for binding values to parameter
		/// placeholders in SQL statements or Stored Procedure variables.
		/// </summary> 
		/// <returns>A new <see cref="IDbDataParameter"/></returns>
		IDbDataParameter CreateParameter();
        
		/// <summary>
		/// Connection string used to create connections.
		/// </summary>
		string ConnectionString
		{
			set;
			get;
		}
        
        
	}
}
