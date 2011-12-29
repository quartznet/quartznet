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

using Quartz.Spi;

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// An interface which provides an implementation for storing a particular
    /// type of <see cref="ITrigger" />'s extended properties.
    /// </summary>
    /// <author>jhouse</author>
    public interface ITriggerPersistenceDelegate
    {
        /// <summary>
        /// Initializes the persistence delegate.
        /// </summary>
        void Initialize(string tablePrefix, string schedulerName, IDbAccessor dbAccessor);

        /// <summary>
        /// Returns whether the trigger type can be handled by delegate.
        /// </summary>
        bool CanHandleTriggerType(IOperableTrigger trigger);

        /// <summary>
        /// Returns database discriminator value for trigger type.
        /// </summary>
        string GetHandledTriggerTypeDiscriminator();

        /// <summary>
        /// Inserts trigger's special properties.
        /// </summary>
        int InsertExtendedTriggerProperties(ConnectionAndTransactionHolder conn, IOperableTrigger trigger, string state, IJobDetail jobDetail);

        /// <summary>
        /// Updates trigger's special properties.
        /// </summary>
        int UpdateExtendedTriggerProperties(ConnectionAndTransactionHolder conn, IOperableTrigger trigger, string state, IJobDetail jobDetail);

        /// <summary>
        /// Deletes trigger's special properties.
        /// </summary>
        int DeleteExtendedTriggerProperties(ConnectionAndTransactionHolder conn, TriggerKey triggerKey);
        
        /// <summary>
        /// Loads trigger's special properties.
        /// </summary>
        TriggerPropertyBundle LoadExtendedTriggerProperties(ConnectionAndTransactionHolder conn, TriggerKey triggerKey);
    }
}