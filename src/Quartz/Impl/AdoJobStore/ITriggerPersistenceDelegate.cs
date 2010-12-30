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
    /// type of <code>Trigger</code>'s extended properties.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <author>jhouse</author>
    public interface ITriggerPersistenceDelegate
    {
        void Initialize(string tablePrefix, string schedulerName, AdoUtil adoUtil);

        bool CanHandleTriggerType(IOperableTrigger trigger);

        string GetHandledTriggerTypeDiscriminator();

        int InsertExtendedTriggerProperties(ConnectionAndTransactionHolder conn, IOperableTrigger trigger, string state, IJobDetail jobDetail);

        int UpdateExtendedTriggerProperties(ConnectionAndTransactionHolder conn, IOperableTrigger trigger, string state, IJobDetail jobDetail);

        int DeleteExtendedTriggerProperties(ConnectionAndTransactionHolder conn, TriggerKey triggerKey);

        TriggerPropertyBundle LoadExtendedTriggerProperties(ConnectionAndTransactionHolder conn, TriggerKey triggerKey);
    }

    public class TriggerPropertyBundle
    {
        private IScheduleBuilder sb;
        private readonly string[] statePropertyNames;
        private readonly object[] statePropertyValues;

        public TriggerPropertyBundle(IScheduleBuilder sb, string[] statePropertyNames, object[] statePropertyValues)
        {
            this.sb = sb;
            this.statePropertyNames = statePropertyNames;
            this.statePropertyValues = statePropertyValues;
        }

        public IScheduleBuilder ScheduleBuilder
        {
            get { return sb; }
        }

        public string[] StatePropertyNames
        {
            get { return statePropertyNames; }
        }

        public object[] StatePropertyValues
        {
            get { return statePropertyValues; }
        }
    }
}