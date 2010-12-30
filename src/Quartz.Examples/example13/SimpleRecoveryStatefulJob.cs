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

namespace Quartz.Examples.Example13
{
    /// <summary> 
    /// This job has the same functionality of SimpleRecoveryJob
    /// except that this job implements is 'stateful', in that it
    /// will have it's data (JobDataMap) automatically re-persisted 
    /// after each execution, and only one instance of the JobDetail
    /// can be executed at a time.
    /// </summary>
    /// <author>Bill Kratzer</author>
    /// <author>Marko Lahma (.NET)</author>
    [PersistJobDataAfterExecution]
    [DisallowConcurrentExecution]
    public class SimpleRecoveryStatefulJob : SimpleRecoveryJob
    {
    }
}