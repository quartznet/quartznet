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
using System.Text;

namespace Quartz.Xml
{
	/// <summary> 
	/// Reports QuartzMetaDataProcessor validation exceptions.
	/// </summary>
	/// <author> <a href="mailto:bonhamcm@thirdeyeconsulting.com">Chris Bonham</a></author>
	[Serializable]
	public class ValidationException : Exception
	{

		/// <summary>
		/// Gets the validation exceptions.
		/// </summary>
		/// <value>The validation exceptions.</value>
		public virtual ICollection ValidationExceptions
		{
			get { return validationExceptions; }
		}

		/// <summary
		/// Returns the detail message string.
		/// </summary>
		public override string Message
		{
			get
			{
				if (ValidationExceptions.Count == 0)
				{
					return base.Message;
				}

				StringBuilder sb = new StringBuilder();

				bool first = true;

				foreach (Exception e in ValidationExceptions)
				{
					if (!first)
					{
						sb.Append('\n');
						first = false;
					}

					sb.Append(e.Message);
				}

				return sb.ToString();
			}
		}

		private ICollection validationExceptions = new ArrayList();

		/// <summary>
		/// Constructor for ValidationException.
		/// </summary>
		public ValidationException() : base()
		{
		}

		/// <summary>
		/// Constructor for ValidationException.
		/// </summary>
		/// <param name="message">exception message.</param>
		public ValidationException(string message) : base(message)
		{
		}

		/// <summary>
		/// Constructor for ValidationException.
		/// </summary>
		/// <param name="errors">collection of validation exceptions.</param>
		public ValidationException(ICollection errors) : this()
		{
			validationExceptions = ArrayList.ReadOnly(new ArrayList(errors));
		}
	}
}