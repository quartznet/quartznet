/* 
* Copyright 2004-2005 OpenSymphony 
* 
* Licensed under the Apache License, Version 2.0 (the "License"); you may not 
* use this file except in compliance with the License. You may obtain a copy 
* of the License at 
* 
*   http://www.apache.org/licenses/LICENSE-2.0 
*   
* Unless required by applicable law or agreed to in writing, software 
* distributed under the License is distributed on an "AS IS" BASIS, WITHOUT 
* WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the 
* License for the specific language governing permissions and limitations 
* under the License.
* 
*/

/*
* Previously Copyright (c) 2001-2004 James House
*/
//UPGRADE_TODO: The type 'org.apache.commons.logging.Log' could not be found. If it was not included in the conversion, there may be compiler issues. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1262_3"'
using System;
using System.Data.OleDb;
using System.IO;
using log4net;

namespace Quartz.impl.jdbcjobstore
{
	/// <summary> <p>
	/// This is a driver delegate for the PostgreSQL JDBC driver.
	/// </p>
	/// 
	/// </summary>
	/// <author>  <a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a>
	/// </author>
	public class PostgreSQLDelegate : StdJDBCDelegate
	{
		/// <summary> <p>
		/// Create new PostgreSQLDelegate instance.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">logger
		/// the logger to use during execution
		/// </param>
		/// <param name="">tablePrefix
		/// the prefix of all table names
		/// </param>
		public PostgreSQLDelegate(ILog log, String tablePrefix, String instanceId) : base(log, tablePrefix, instanceId)
		{
		}

		/// <summary> <p>
		/// Create new PostgreSQLDelegate instance.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">logger
		/// the logger to use during execution
		/// </param>
		/// <param name="">tablePrefix
		/// the prefix of all table names
		/// </param>
		/// <param name="">useProperties
		/// use java.util.Properties for storage
		/// </param>
		//UPGRADE_NOTE: ref keyword was added to struct-type parameters. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1303_3"'
		public PostgreSQLDelegate(ILog log, String tablePrefix, String instanceId, ref Boolean useProperties) : base(log, tablePrefix, instanceId, useProperties)
		{
		}

		//---------------------------------------------------------------------------
		// protected methods that can be overridden by subclasses
		//---------------------------------------------------------------------------

		/// <summary> <p>
		/// This method should be overridden by any delegate subclasses that need
		/// special handling for BLOBs. The default implementation uses standard
		/// JDBC <code>java.sql.Blob</code> operations.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">rs
		/// the result set, already queued to the correct row
		/// </param>
		/// <param name="">colName
		/// the column name for the BLOB
		/// </param>
		/// <returns> the deserialized Object from the ResultSet BLOB
		/// </returns>
		/// <throws>  ClassNotFoundException </throws>
		/// <summary>           if a class found during deserialization cannot be found
		/// </summary>
		/// <throws>  IOException </throws>
		/// <summary>           if deserialization causes an error
		/// </summary>
		//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
		protected internal override Object getObjectFromBlob(OleDbDataReader rs, String colName)
		{
			Stream binaryInput = null;
			//UPGRADE_ISSUE: Method 'java.sql.ResultSet.getBytes' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlResultSetgetBytes_javalangString_3"'
			sbyte[] bytes = rs.getBytes(colName);

			Object obj = null;

			if (bytes != null)
			{
				binaryInput = new MemoryStream(SupportClass.ToByteArray(bytes));

				//UPGRADE_TODO: Class 'java.io.ObjectInputStream' was converted to 'System.IO.BinaryReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javaioObjectInputStream_3"'
				BinaryReader in_Renamed = new BinaryReader(binaryInput);
				//UPGRADE_WARNING: Method 'java.io.ObjectInputStream.readObject' was converted to 'SupportClass.Deserialize' which may throw an exception. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1101_3"'
				obj = SupportClass.Deserialize(in_Renamed);
				in_Renamed.Close();
			}

			return obj;
		}

		//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
		protected internal override Object getJobDetailFromBlob(OleDbDataReader rs, String colName)
		{
			if (canUseProperties())
			{
				Stream binaryInput = null;
				//UPGRADE_ISSUE: Method 'java.sql.ResultSet.getBytes' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlResultSetgetBytes_javalangString_3"'
				sbyte[] bytes = rs.getBytes(colName);
				if (bytes == null || bytes.Length == 0)
					return null;
				binaryInput = new MemoryStream(SupportClass.ToByteArray(bytes));
				return binaryInput;
			}
			return getObjectFromBlob(rs, colName);
		}
	}

	// EOF
}