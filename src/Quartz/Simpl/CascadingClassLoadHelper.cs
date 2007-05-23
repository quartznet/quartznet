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
using System.Collections;
using System.IO;

using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Simpl
{
	/// <summary>
	/// A <code>ClassLoadHelper</code> uses all of the <code>ClassLoadHelper</code>
	/// types that are found in this package in its attempts to load a class, when
	/// one scheme is found to work, it is promoted to the scheme that will be used
	/// first the next time a class is loaded (in order to improve perfomance).
	/// <p>
	/// This approach is used because of the wide variance in class loader behavior
	/// between the various environments in which Quartz runs (e.g. disparate 
	/// application servers, stand-alone, mobile devices, etc.).  Because of this
	/// disparity, Quartz ran into difficulty with a one class-load style fits-all 
	/// design.  Thus, this class loader finds the approach that works, then 
	/// 'remembers' it.  
	/// </p>
	/// </summary>
	/// <author>James House</author>
	public class CascadingClassLoadHelper : IClassLoadHelper
	{
		/*
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		* 
		* Data members.
		* 
		* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
		*/

		private ArrayList loadHelpers;

		private IClassLoadHelper bestCandidate;


		/// <summary> 
		/// Called to give the ClassLoadHelper a chance to Initialize itself,
		/// including the oportunity to "steal" the class loader off of the calling
		/// thread, which is the thread that is initializing Quartz.
		/// </summary>
		public virtual void Initialize()
		{
			loadHelpers = new ArrayList();

			loadHelpers.Add(new LoadingLoaderClassLoadHelper());
			loadHelpers.Add(new SimpleClassLoadHelper());

			foreach (IClassLoadHelper helper in loadHelpers)
			{
				helper.Initialize();
			}
		}

		/// <summary>
		/// Return the class with the given name.
		/// </summary>
		public virtual Type LoadType(string name)
		{
			if (bestCandidate != null)
			{
				try
				{
					return bestCandidate.LoadType(name);
				}
				catch (Exception)
				{
					bestCandidate = null;
				}
			}

			Exception cnfe = null;
			Type clazz = null;

			foreach (IClassLoadHelper loadHelper in loadHelpers)
			{
				try
				{
					clazz = loadHelper.LoadType(name);
					if (clazz != null)
					{
						break;
					}
					bestCandidate = loadHelper;
				}
				catch (Exception e)
				{
					cnfe = e;
				}
			}

			if (clazz == null)
			{
				throw cnfe;
			}

			return clazz;
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
			if (bestCandidate != null)
			{
				try
				{
					return bestCandidate.GetResource(name);
				}
				catch (Exception)
				{
					bestCandidate = null;
				}
			}

			Uri result = null;
			IClassLoadHelper loadHelper = null;

			foreach (IClassLoadHelper lh in loadHelpers)
			{
				loadHelper = lh;

				result = loadHelper.GetResource(name);
				if (result != null)
				{
					break;
				}
			}

			bestCandidate = loadHelper;
			return result;
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
			if (bestCandidate != null)
			{
				try
				{
					return bestCandidate.GetResourceAsStream(name);
				}
				catch (Exception)
				{
					bestCandidate = null;
				}
			}

			Stream result = null;
			IClassLoadHelper loadHelper = null;

			foreach (IClassLoadHelper lh in loadHelpers)
			{
				loadHelper = lh;

				result = loadHelper.GetResourceAsStream(name);
				if (result != null)
				{
					break;
				}
			}

			bestCandidate = loadHelper;
			return result;
		}
	}
}