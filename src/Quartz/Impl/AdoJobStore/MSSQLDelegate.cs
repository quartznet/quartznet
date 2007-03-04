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

using System;
using System.Data.OleDb;
using System.IO;

using Common.Logging;

namespace Quartz.Impl.AdoJobStore
{
	/// <summary>
	/// This is a driver delegate for the MSSQL ADO.NET driver.
	/// </summary>
	/// <author><a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a></author>
	public class MSSQLDelegate : StdAdoDelegate
	{
		/// <summary> <p>
		/// Create new MSSQLDelegate instance.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">logger
		/// the logger to use during execution
		/// </param>
		/// <param name="">tablePrefix
		/// the prefix of all table names
		/// </param>
		public MSSQLDelegate(ILog log, String tablePrefix, String instanceId) : base(log, tablePrefix, instanceId)
		{
		}

		public MSSQLDelegate(ILog log, String tablePrefix, String instanceId, ref Boolean useProperties) : base(log, tablePrefix, instanceId, useProperties)
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
		protected internal override object GetObjectFromBlob(OleDbDataReader rs, String colName)
		{
			Stream binaryInput = new MemoryStream((byte[]) rs[colName]);

			if (binaryInput == null)
				return null;

			//UPGRADE_TODO: Class 'java.io.ObjectInputStream' was converted to 'System.IO.BinaryReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javaioObjectInputStream_3"'
			BinaryReader in_Renamed = new BinaryReader(binaryInput);
			//UPGRADE_WARNING: Method 'java.io.ObjectInputStream.readObject' was converted to 'SupportClass.Deserialize' which may throw an exception. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1101_3"'
			// Object obj = SupportClass.Deserialize(in_Renamed);
			in_Renamed.Close();

			return null;
		}

		//UPGRADE_TODO: Interface 'java.sql.ResultSet' was converted to 'System.Data.OleDb.OleDbDataReader' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javasqlResultSet_3"'
		protected internal override object GetJobDetailFromBlob(OleDbDataReader rs, String colName)
		{
			if (CanUseProperties())
			{
				Stream binaryInput = new MemoryStream((byte[]) rs[colName]);
				return binaryInput;
			}
			return GetObjectFromBlob(rs, colName);
		}
	}

	// EOF
}