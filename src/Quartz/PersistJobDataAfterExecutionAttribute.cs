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

namespace Quartz
{
    /// <summary>
    /// An attribute that marks a <see cref="IJob"/> class as one that makes updates to its
    /// <see cref="JobDataMap" /> during execution, and wishes the scheduler to re-store the
    /// <see cref="JobDataMap" /> when execution completes. 
    /// </summary>
    /// <remarks>
    /// <para>
    /// Jobs that are marked with this annotation should also seriously consider
    /// using the <see cref="DisallowConcurrentExecutionAttribute" /> attribute, to avoid data
    /// storage race conditions with concurrently executing job instances.
    /// </para>
    /// <para>
    /// This can be used in lieu of implementing the StatefulJob marker interface that 
    /// was used prior to Quartz 2.0
    /// </para>
    ///</remarks>
    /// <seealso cref="DisallowConcurrentExecutionAttribute" />
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    [AttributeUsage(AttributeTargets.Class)]
    public class PersistJobDataAfterExecutionAttribute : Attribute
    {
    }
}