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
//UPGRADE_TODO: The type 'org.apache.commons.logging.LogFactory' could not be found. If it was not included in the conversion, there may be compiler issues. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1262_3"'
using System;
using log4net;

namespace Sentera.Scheduling.utils
{
	/// <summary> <p>
	/// A <code>ConnectionProvider</code> that provides connections from a <code>DataSource</code>
	/// that is managed by an application server, and made available via JNDI.
	/// </p>
	/// 
	/// </summary>
	/// <seealso cref="DBConnectionManager">
	/// </seealso>
	/// <seealso cref="ConnectionProvider">
	/// </seealso>
	/// <seealso cref="PoolingConnectionProvider">
	/// 
	/// </seealso>
	/// <author>  James House
	/// </author>
	/// <author>  Sharada Jambula
	/// </author>
	/// <author>  Mohammad Rezaei
	/// </author>
	/// <author>  Patrick Lightbody
	/// </author>
	/// <author>  Srinivas Venkatarangaiah
	/// </author>
	public class JNDIConnectionProvider : ConnectionProvider
	{
		internal virtual ILog Log
		{
			/*
			* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
			* 
			* Interface.
			* 
			* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
			*/


			get { return LogFactory.Log; }

		}

		//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
		public virtual System.Data.OleDb.OleDbConnection Connection
		{
			get
			{
				System.DirectoryServices.DirectoryEntry ctx = null;
				try
				{
					System.Object ds = this.datasource;

					if (ds == null || AlwaysLookup)
					{
						if (props != null)
						{
							//UPGRADE_TODO: Constructor 'javax.naming.InitialContext.InitialContext' was converted to 'System.DirectoryServices.DirectoryEntry' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javaxnamingInitialContextInitialContext_javautilHashtable_3"'
							//UPGRADE_TODO: Adjust remoting context initialization manually. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1258_3"'
							ctx = new System.DirectoryServices.DirectoryEntry();
						}
						else
						{
							//UPGRADE_TODO: Constructor 'javax.naming.InitialContext.InitialContext' was converted to 'System.DirectoryServices.DirectoryEntry' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javaxnamingInitialContextInitialContext_3"'
							//UPGRADE_TODO: Adjust remoting context initialization manually. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1258_3"'
							ctx = new System.DirectoryServices.DirectoryEntry();
						}

						//UPGRADE_TODO: Method 'javax.naming.Context.lookup' was converted to 'System.Activator.GetObject' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javaxnamingContextlookup_javalangString_3"'
						ds = Activator.GetObject(typeof (System.MarshalByRefObject), SupportClass.ParseURILookup(url));
						if (!AlwaysLookup)
							this.datasource = ds;
					}

					if (ds == null)
					{
						//UPGRADE_ISSUE: Constructor 'java.sql.SQLException.SQLException' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlSQLExceptionSQLException_javalangString_3"'
						throw new SQLException("There is no object at the JNDI URL '" + url + "'");
					}

					//UPGRADE_ISSUE: Interface 'javax.sql.XADataSource' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javaxsqlXADataSource_3"'
					if (ds is XADataSource)
					{
						SupportClass.DistributedConnectionSupport temp_connection;
						temp_connection = new SupportClass.DistributedConnectionSupport();
						//UPGRADE_TODO: Change connection string to .NET format and add values from the Properties object. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1064_3"'
						temp_connection.MainConnection = new System.Data.OleDb.OleDbConnection("Provider=SQLOLEDB;Data Source=localhost;Initial Catalog=Northwind;Integrated Security=SSPI;Connect Timeout=30");
						temp_connection.MainConnection.Open();
						return (temp_connection.GetConnection());
					}
					else
					{
						//UPGRADE_ISSUE: Interface 'javax.sql.DataSource' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javaxsqlDataSource_3"'
						if (ds is DataSource)
						{
							System.Data.OleDb.OleDbConnection temp_Connection2;
							//UPGRADE_TODO: Change connection string to .NET format. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1063_3"'
							//UPGRADE_NOTE: There are other database providers or managers under System.Data namespace which can be used optionally to better fit the application requirements. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1208_3"'
							temp_Connection2 = new System.Data.OleDb.OleDbConnection();
							temp_Connection2.Open();
							return temp_Connection2;
						}
						else
						{
							//UPGRADE_ISSUE: Constructor 'java.sql.SQLException.SQLException' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlSQLExceptionSQLException_javalangString_3"'
							throw new SQLException("Object at JNDI URL '" + url + "' is not a DataSource.");
						}
					}
				}
				catch (System.Exception e)
				{
					this.datasource = null;
					//UPGRADE_ISSUE: Constructor 'java.sql.SQLException.SQLException' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javasqlSQLExceptionSQLException_javalangString_3"'
					//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Class.getName' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
					//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
					throw new SQLException("Could not retrieve datasource via JNDI url '" + url + "' " + e.GetType().FullName + ": " + e.Message);
				}
				finally
				{
					if (ctx != null)
						try
						{
							ctx.Close();
						}
						catch (System.Exception ignore)
						{
						}
				}
			}

		}

		public virtual bool AlwaysLookup
		{
			get { return alwaysLookup; }

			set { alwaysLookup = value; }

		}

		/*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Data members.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

		private string url;

		//UPGRADE_ISSUE: Class hierarchy differences between 'java.util.Properties' and 'System.Collections.Specialized.NameValueCollection' may cause compilation errors. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1186_3"'
		private System.Collections.Specialized.NameValueCollection props;

		private System.Object datasource;

		private bool alwaysLookup = false;

		/*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Constructors.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

		/// <summary> Constructor
		/// 
		/// </summary>
		/// <param name="">jndiUrl
		/// The url for the datasource
		/// </param>
		public JNDIConnectionProvider(string jndiUrl, bool alwaysLookup)
		{
			this.url = jndiUrl;
			this.alwaysLookup = alwaysLookup;
			init();
		}

		/// <summary> Constructor
		/// 
		/// </summary>
		/// <param name="">jndiUrl
		/// The URL for the DataSource
		/// </param>
		/// <param name="">jndiProps
		/// The JNDI properties to use when establishing the InitialContext
		/// for the lookup of the given URL.
		/// </param>
		//UPGRADE_ISSUE: Class hierarchy differences between 'java.util.Properties' and 'System.Collections.Specialized.NameValueCollection' may cause compilation errors. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1186_3"'
		public JNDIConnectionProvider(string jndiUrl, System.Collections.Specialized.NameValueCollection jndiProps, bool alwaysLookup)
		{
			this.url = jndiUrl;
			this.props = jndiProps;
			this.alwaysLookup = alwaysLookup;
			init();
		}

		private void init()
		{
			if (!AlwaysLookup)
			{
				System.DirectoryServices.DirectoryEntry ctx = null;
				try
				{
					if (props != null)
					{
						//UPGRADE_TODO: Constructor 'javax.naming.InitialContext.InitialContext' was converted to 'System.DirectoryServices.DirectoryEntry' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javaxnamingInitialContextInitialContext_javautilHashtable_3"'
						//UPGRADE_TODO: Adjust remoting context initialization manually. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1258_3"'
						ctx = new System.DirectoryServices.DirectoryEntry();
					}
					else
					{
						//UPGRADE_TODO: Constructor 'javax.naming.InitialContext.InitialContext' was converted to 'System.DirectoryServices.DirectoryEntry' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javaxnamingInitialContextInitialContext_3"'
						//UPGRADE_TODO: Adjust remoting context initialization manually. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1258_3"'
						ctx = new System.DirectoryServices.DirectoryEntry();
					}

					//UPGRADE_TODO: Method 'javax.naming.Context.lookup' was converted to 'System.Activator.GetObject' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javaxnamingContextlookup_javalangString_3"'
					//UPGRADE_ISSUE: Interface 'javax.sql.DataSource' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javaxsqlDataSource_3"'
					datasource = (DataSource) Activator.GetObject(typeof (System.MarshalByRefObject), SupportClass.ParseURILookup(url));
				}
				catch (System.Exception e)
				{
					//UPGRADE_TODO: The equivalent in .NET for method 'java.lang.Throwable.getMessage' may return a different value. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1043_3"'
					Log.error("Error looking up datasource: " + e.Message, e);
				}
				finally
				{
					if (ctx != null)
						try
						{
							ctx.Close();
						}
						catch (System.Exception ignore)
						{
						}
				}
			}
		}

		/* 
		* @see org.quartz.utils.ConnectionProvider#shutdown()
		*/

		public virtual void shutdown()
		{
			// do nothing
		}
	}
}