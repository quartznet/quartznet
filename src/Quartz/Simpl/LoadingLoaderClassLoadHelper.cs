/* 
* Copyright 2004-2009 James House 
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
using System.IO;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Simpl
{
	/// <summary>
	/// A <see cref="ITypeLoadHelper" /> that uses either the loader of it's own
	/// class.
	/// </summary>
	/// <seealso cref="ITypeLoadHelper" />
	/// <seealso cref="SimpleClassLoadHelper" />
	/// <seealso cref="CascadingClassLoadHelper" />
	/// <author>James House</author>
	public class LoadingLoaderClassLoadHelper : ITypeLoadHelper
	{
		/// <summary> 
		/// Called to give the ClassLoadHelper a chance to Initialize itself,
		/// including the oportunity to "steal" the class loader off of the calling
		/// thread, which is the thread that is initializing Quartz.
		/// </summary>
		public virtual void Initialize()
		{
		}

		/// <summary> Return the class with the given name.</summary>
		public virtual Type LoadType(string name)
		{
			return Type.GetType(name);
		}

		/// <summary> Finds a resource with a given name. This method returns null if no
		/// resource with this name is found.
		/// </summary>
		/// <param name="name">name of the desired resource
		/// </param>
		/// <returns> a java.net.URL object
		/// </returns>
		public virtual Uri GetResource(string name)
		{
			//UPGRADE_ISSUE: Method 'java.lang.ClassLoader.getResource' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javalangClassLoader_3"'
			return null; //ClassLoader.getResource(name);
		}

		/// <summary> Finds a resource with a given name. This method returns null if no
		/// resource with this name is found.
		/// </summary>
		/// <param name="name">name of the desired resource
		/// </param>
		/// <returns> a java.io.InputStream object
		/// </returns>
		public virtual Stream GetResourceAsStream(string name)
		{
			//UPGRADE_ISSUE: Method 'java.lang.ClassLoader.getResourceAsStream' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javalangClassLoader_3"'
			return null; // ClassLoader.getResourceAsStream(name);
		}
	}
}