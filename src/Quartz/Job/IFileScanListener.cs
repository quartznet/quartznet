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

namespace Quartz.Job
{
	/// <summary> 
	/// Interface for objects wishing to receive a 'call-back' from a 
	/// <see cref="FileScanJob" />.
	/// </summary>
	/// <author>James House</author>
	/// <author>Marko Lahma (.NET)</author>
    /// <seealso cref="FileScanJob" />
	public interface IFileScanListener
	{
		/// <summary>
		/// Informs that certain file has been updated.
		/// </summary>
		/// <param name="fileName">Name of the file.</param>
		void FileUpdated(string fileName);
	}
}