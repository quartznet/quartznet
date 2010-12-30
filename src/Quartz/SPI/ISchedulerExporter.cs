#region License
/* 
 * Copyright 2009- Marko Lahma
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

using Quartz.Simpl;

namespace Quartz.Spi
{
    /// <summary>
    /// Service interface for scheduler exporters.
    /// </summary>
    /// <author>Marko Lahma</author>
    public interface ISchedulerExporter
    {
        /// <summary>
        /// Binds (exports) scheduler to external context.
        /// </summary>
        /// <param name="scheduler"></param>
        void Bind(IRemotableQuartzScheduler scheduler);

        /// <summary>
        /// Unbinds scheduler from external context.
        /// </summary>
        /// <param name="scheduler"></param>
        void UnBind(IRemotableQuartzScheduler scheduler);
    }
}
