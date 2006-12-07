/* 
* Copyright 2005 OpenSymphony 
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
using System;
//UPGRADE_TODO: The type 'org.quartz.StatefulJob' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using StatefulJob = org.quartz.StatefulJob;
namespace org.quartz.examples.example13
{
	
	/// <summary> This job has the same functionality of SimpleRecoveryJob
	/// except that this job implements the StatefulJob interface
	/// 
	/// </summary>
	/// <author>  Bill Kratzer
	/// </author>
	public class SimpleRecoveryStatefulJob:SimpleRecoveryJob, StatefulJob
	{
		
		/// <summary> </summary>
		public SimpleRecoveryStatefulJob():base()
		{
		}
	}
}