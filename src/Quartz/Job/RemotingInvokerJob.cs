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

namespace Quartz.Job
{
	/// <summary> <p>
	/// A <code>Job</code> that invokes a method on an EJB.
	/// </p>
	/// 
	/// <p>
	/// Expects the properties corresponding to the following keys to be in the
	/// <code>JobDataMap</code> when it executes:
	/// <ul>
	/// <li><code>EJB_JNDI_NAME_KEY</code>- the JNDI name (location) of the
	/// EJB's home interface.</li>
	/// <li><code>EJB_METHOD_KEY</code>- the name of the method to invoke on the
	/// EJB.</li>
	/// <li><code>EJB_ARGS_KEY</code>- an Object[] of the args to pass to the
	/// method (optional, if left out, there are no arguments).</li>
	/// <li><code>EJB_ARG_TYPES_KEY</code>- an Object[] of the args to pass to
	/// the method (optional, if left out, the types will be derived by calling
	/// getClass() on each of the arguments).</li>
	/// </ul>
	/// <br/>
	/// The following keys can also be used at need:
	/// <ul>
	/// <li><code>INITIAL_CONTEXT_FACTORY</code> - the context factory used to 
	/// build the context.</li>
	/// <li><code>PROVIDER_URL</code> - the name of the environment property
	/// for specifying configuration information for the service provider to use.
	/// </li>
	/// </ul>
	/// </p>
	/// 
	/// </summary>
	/// <author>  Andrew Collins
	/// </author>
	/// <author>  James House
	/// </author>
	/// <author>  Joel Shellman
	/// </author>
	/// <author>  <a href="mailto:bonhamcm@thirdeyeconsulting.com">Chris Bonham</a>
	/// </author>
	public class EJBInvokerJob : IJob
	{
		/*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Constants.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

		public const string EJB_JNDI_NAME_KEY = "ejb";

		public const string EJB_METHOD_KEY = "method";

		public const string EJB_ARG_TYPES_KEY = "argTypes";

		public const string EJB_ARGS_KEY = "args";

		public const string INITIAL_CONTEXT_FACTORY = "java.naming.factory.initial";

		public const string PROVIDER_URL = "java.naming.provider.url";

		public const string PRINCIPAL = "java.naming.security.principal";

		public const string CREDENTIALS = "java.naming.security.credentials";


		/*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Constructors.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

		public EJBInvokerJob()
		{
			// nothing
		}

		/*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Interface.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

		public virtual void execute(JobExecutionContext context)
		{
			JobDetail detail = context.JobDetail;

			JobDataMap dataMap = detail.JobDataMap;

			string ejb = dataMap.getString(EJB_JNDI_NAME_KEY);
			string method = dataMap.getString(EJB_METHOD_KEY);
			System.Object[] arguments = (System.Object[]) dataMap[EJB_ARGS_KEY];
			if (arguments == null)
			{
				arguments = new System.Object[0];
			}

			if (ejb == null)
			{
				// must specify remote home
				throw new JobExecutionException();
			}

			System.DirectoryServices.DirectoryEntry jndiContext = null;

			// get initial context
			try
			{
				jndiContext = getInitialContext(dataMap);
			}
				//UPGRADE_NOTE: Exception 'javax.naming.NamingException' was converted to 'System.Exception' which has different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1100_3"'
			catch (System.Exception ne)
			{
				throw new JobExecutionException(ne);
			}

			System.Object value_Renamed = null;

			// locate home interface
			try
			{
				//UPGRADE_TODO: Method 'javax.naming.InitialContext.lookup' was converted to 'System.Activator.GetObject' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javaxnamingInitialContextlookup_javalangString_3"'
				value_Renamed = Activator.GetObject(typeof (System.MarshalByRefObject), SupportClass.ParseURILookup(ejb));
			}
				//UPGRADE_NOTE: Exception 'javax.naming.NamingException' was converted to 'System.Exception' which has different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1100_3"'
			catch (System.Exception ne)
			{
				throw new JobExecutionException(ne);
			}
			finally
			{
				if (jndiContext != null)
				{
					try
					{
						jndiContext.Close();
					}
						//UPGRADE_NOTE: Exception 'javax.naming.NamingException' was converted to 'System.Exception' which has different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1100_3"'
					catch (System.Exception e)
					{
						// Ignore any errors closing the initial context
					}
				}
			}

			// get home interface
			//UPGRADE_ISSUE: Interface 'javax.ejb.EJBHome' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javaxejbEJBHome_3"'
			//UPGRADE_TODO: Method 'javax.rmi.PortableRemoteObject.narrow' was converted to 'System.Object' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javaxrmiPortableRemoteObjectnarrow_javalangObject_javalangClass_3"'
			EJBHome ejbHome = (EJBHome) value_Renamed;

			// get meta data
			//UPGRADE_ISSUE: Interface 'javax.ejb.EJBMetaData' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javaxejbEJBMetaData_3"'
			EJBMetaData metaData = null;

			try
			{
				//UPGRADE_ISSUE: Method 'javax.ejb.EJBHome.getEJBMetaData' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javaxejbEJBHome_3"'
				metaData = ejbHome.getEJBMetaData();
			}
			catch (System.Runtime.Remoting.RemotingException re)
			{
				throw new JobExecutionException(re);
			}

			// get home interface class
			//UPGRADE_ISSUE: Method 'javax.ejb.EJBMetaData.getHomeInterfaceClass' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javaxejbEJBMetaData_3"'
			System.Type homeClass = metaData.getHomeInterfaceClass();

			// get remote interface class
			//UPGRADE_ISSUE: Method 'javax.ejb.EJBMetaData.getRemoteInterfaceClass' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javaxejbEJBMetaData_3"'
			System.Type remoteClass = metaData.getRemoteInterfaceClass();

			// get home interface
			//UPGRADE_TODO: Method 'javax.rmi.PortableRemoteObject.narrow' was converted to 'System.Object' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javaxrmiPortableRemoteObjectnarrow_javalangObject_javalangClass_3"'
			//UPGRADE_ISSUE: Interface 'javax.ejb.EJBHome' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javaxejbEJBHome_3"'
			ejbHome = (EJBHome) ejbHome;

			System.Reflection.MethodInfo methodCreate = null;

			try
			{
				// create method 'create()' on home interface
				//UPGRADE_TODO: Method 'java.lang.Class.getDeclaredMethod' was converted to 'System.Type.GetMethod' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javalangClassgetDeclaredMethod_javalangString_javalangClass[]_3"'
				//UPGRADE_WARNING: Method 'java.lang.Class.getDeclaredMethod' was converted to 'System.Type.GetMethod' which may throw an exception. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1101_3"'
				methodCreate = homeClass.GetMethod("create", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly, null, null, null);
			}
			catch (System.MethodAccessException nsme)
			{
				throw new JobExecutionException(nsme);
			}

			// create remote object
			//UPGRADE_ISSUE: Interface 'javax.ejb.EJBObject' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javaxejbEJBObject_3"'
			EJBObject remoteObj = null;

			try
			{
				// invoke 'create()' method on home interface
				//UPGRADE_ISSUE: Interface 'javax.ejb.EJBObject' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javaxejbEJBObject_3"'
				remoteObj = (EJBObject) methodCreate.Invoke(ejbHome, (System.Object[]) null);
			}
			catch (System.UnauthorizedAccessException iae)
			{
				throw new JobExecutionException(iae);
			}
			catch (System.Reflection.TargetInvocationException ite)
			{
				throw new JobExecutionException(ite);
			}

			// execute user-specified method on remote object
			System.Reflection.MethodInfo methodExecute = null;

			try
			{
				// create method signature

				System.Type[] argTypes = (System.Type[]) dataMap[EJB_ARG_TYPES_KEY];
				if (argTypes == null)
				{
					argTypes = new System.Type[arguments.Length];
					for (int i = 0; i < arguments.Length; i++)
					{
						argTypes[i] = arguments[i].GetType();
					}
				}

				// get method on remote object
				methodExecute = remoteClass.GetMethod(method, (argTypes == null) ? new System.Type[0] : (System.Type[]) argTypes);
			}
			catch (System.MethodAccessException nsme)
			{
				throw new JobExecutionException(nsme);
			}

			try
			{
				// invoke user-specified method on remote object
				methodExecute.Invoke(remoteObj, (System.Object[]) arguments);
			}
			catch (System.UnauthorizedAccessException iae)
			{
				throw new JobExecutionException(iae);
			}
			catch (System.Reflection.TargetInvocationException ite)
			{
				throw new JobExecutionException(ite);
			}
		}

		private System.DirectoryServices.DirectoryEntry getInitialContext(JobDataMap jobDataMap)
		{
			System.Collections.Hashtable params_Renamed = System.Collections.Hashtable.Synchronized(new System.Collections.Hashtable(2));

			string initialContextFactory = jobDataMap.getString(INITIAL_CONTEXT_FACTORY);
			if (initialContextFactory != null)
			{
				//UPGRADE_ISSUE: Field 'javax.naming.Context.INITIAL_CONTEXT_FACTORY' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javaxnamingContextINITIAL_CONTEXT_FACTORY_f_3"'
				params_Renamed[Context.INITIAL_CONTEXT_FACTORY] = initialContextFactory;
			}

			string providerUrl = jobDataMap.getString(PROVIDER_URL);
			if (providerUrl != null)
			{
				//UPGRADE_ISSUE: Field 'javax.naming.Context.PROVIDER_URL' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javaxnamingContextPROVIDER_URL_f_3"'
				params_Renamed[Context.PROVIDER_URL] = providerUrl;
			}

			string principal = jobDataMap.getString(PRINCIPAL);
			if (principal != null)
			{
				//UPGRADE_ISSUE: Field 'javax.naming.Context.SECURITY_PRINCIPAL' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javaxnamingContextSECURITY_PRINCIPAL_f_3"'
				params_Renamed[Context.SECURITY_PRINCIPAL] = principal;
			}

			string credentials = jobDataMap.getString(CREDENTIALS);
			if (credentials != null)
			{
				//UPGRADE_ISSUE: Field 'javax.naming.Context.SECURITY_CREDENTIALS' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javaxnamingContextSECURITY_CREDENTIALS_f_3"'
				params_Renamed[Context.SECURITY_CREDENTIALS] = credentials;
			}

			if (params_Renamed.Count == 0)
			{
				//UPGRADE_TODO: Constructor 'javax.naming.InitialContext.InitialContext' was converted to 'System.DirectoryServices.DirectoryEntry' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javaxnamingInitialContextInitialContext_3"'
				//UPGRADE_TODO: Adjust remoting context initialization manually. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1258_3"'
				return new System.DirectoryServices.DirectoryEntry();
			}
			else
			{
				//UPGRADE_TODO: Constructor 'javax.naming.InitialContext.InitialContext' was converted to 'System.DirectoryServices.DirectoryEntry' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javaxnamingInitialContextInitialContext_javautilHashtable_3"'
				//UPGRADE_TODO: Adjust remoting context initialization manually. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1258_3"'
				return new System.DirectoryServices.DirectoryEntry();
			}
		}
	}
}