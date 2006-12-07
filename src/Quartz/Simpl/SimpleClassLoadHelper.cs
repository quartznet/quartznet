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
using System.IO;

using Quartz.Spi;

namespace Quartz.Simpl
{
	/// <summary> 
	/// A <code>ClassLoadHelper</code> that simply calls <code>Class.forName(..)</code>.
	/// </summary>
	/// <seealso cref="IClassLoadHelper" />
	/// <seealso cref="CascadingClassLoadHelper" />
	/// <author>James House</author>
	public class SimpleClassLoadHelper : IClassLoadHelper
	{
		/// <summary> 
		/// Called to give the ClassLoadHelper a chance to initialize itself,
		/// including the oportunity to "steal" the class loader off of the calling
		/// thread, which is the thread that is initializing Quartz.
		/// </summary>
		public virtual void Initialize()
		{
		}

		/// <summary> Return the class with the given name.</summary>
		public virtual Type LoadClass(string name)
		{
			return Type.GetType(name);
		}

		/// <summary>
		/// Finds a resource with a given name. This method returns null if no
		/// resource with this name is found.
		/// </summary>
		/// <param name="name">name of the desired resource
		/// </param>
		/// <returns> a Uri object</returns>
		public virtual Uri GetResource(string name)
		{
			return null; // TODO ClassLoader.getResource(name);
		}

		/// <summary>
		/// Finds a resource with a given name. This method returns null if no
		/// resource with this name is found.
		/// </summary>
		/// <param name="name">name of the desired resource
		/// </param>
		/// <returns> a Stream object
		/// </returns>
		public virtual Stream GetResourceAsStream(string name)
		{
			return null; // TODO ClassLoader.GetResourceAsStream(name);
		}
	}
}