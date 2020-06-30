#region License
/* 
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
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
		/// Called to give the implementation a chance to initialize itself.
		/// </summary>
		void Initialize();

		/// <summary> 
		/// Return the class with the given name.
		/// </summary>
		Type? LoadType(string? name);
	}
}