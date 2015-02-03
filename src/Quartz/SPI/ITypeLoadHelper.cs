#region License
/* 
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved. 
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
#endregion

using System;
using System.IO;

namespace Quartz.Spi
{
	/// <summary> 
	/// An interface for classes wishing to provide the service of loading classes
	/// and resources within the scheduler...
	/// </summary>
	/// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    public interface ITypeLoadHelper
	{
		/// <summary> 
		/// Called to give the ClassLoadHelper a chance to Initialize itself,
		/// including the opportunity to "steal" the class loader off of the calling
		/// thread, which is the thread that is initializing Quartz.
		/// </summary>
		void Initialize();

		/// <summary> 
		/// Return the class with the given name.
		/// </summary>
		Type LoadType(string name);

		/// <summary> 
		/// Finds a resource with a given name. This method returns null if no
		/// resource with this name is found.
		/// </summary>
		/// <param name="name">name of the desired resource
		/// </param>
		/// <returns> a java.net.URL object
		/// </returns>
		Uri GetResource(string name);

		/// <summary> 
		/// Finds a resource with a given name. This method returns null if no
		/// resource with this name is found.
		/// </summary>
		/// <param name="name">name of the desired resource
		/// </param>
		/// <returns> a java.io.InputStream object
		/// </returns>
		Stream GetResourceAsStream(string name);
	}
}