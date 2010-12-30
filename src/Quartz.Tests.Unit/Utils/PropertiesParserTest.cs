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

using System.Collections.Specialized;

using NUnit.Framework;

using Quartz.Util;

namespace Quartz.Tests.Unit.Utils
{
	/// <summary>
	/// Unit tests for PropertiesParser.
	/// </summary>
    /// <author>Marko Lahma (.NET)</author>
    [TestFixture]
	public class PropertiesParserTest
	{
		/// <summary>
		/// Unit test for full getPropertyGroup() method.
		/// </summary>
		[Test]
		public void TestGetPropertyGroupStringBooleanStringArray() 
		{
			// Test that an empty property does not cause an exception
			NameValueCollection props = new NameValueCollection();
			props.Add("x.y.z", "");
        
			PropertiesParser propertiesParser = new PropertiesParser(props);
			NameValueCollection propGroup = propertiesParser.GetPropertyGroup("x.y", true);
			Assert.AreEqual("", propGroup.Get("z"));
		}
	}
}