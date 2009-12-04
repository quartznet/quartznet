/* 
* Copyright 2004-2009 James House 
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

namespace Quartz
{
    /// <summary>
    /// Instructs Scheduler what to do with a trigger and job.
    /// </summary>
    public enum SchedulerInstruction
    {        
        /// <summary>
        /// Instructs the <see cref="IScheduler" /> that the <see cref="Trigger" />
        /// has no further instructions.
        /// </summary>
        NoInstruction,

        /// <summary>
        /// Instructs the <see cref="IScheduler" /> that the <see cref="Trigger" />
        /// wants the <see cref="JobDetail" /> to re-Execute
        /// immediately. If not in a 'RECOVERING' or 'FAILED_OVER' situation, the
        /// execution context will be re-used (giving the <see cref="IJob" /> the
        /// abilitiy to 'see' anything placed in the context by its last execution).
        /// </summary>      
        ReExecuteJob,

        /// <summary>
        /// Instructs the <see cref="IScheduler" /> that the <see cref="Trigger" />
        /// should be put in the <see cref="TriggerState.Complete" /> state.
        /// </summary>
        SetTriggerComplete,

        /// <summary>
        /// Instructs the <see cref="IScheduler" /> that the <see cref="Trigger" />
        /// wants itself deleted.
        /// </summary>
        DeleteTrigger,

        /// <summary>
        /// Instructs the <see cref="IScheduler" /> that all <see cref="Trigger" />
        /// s referencing the same <see cref="JobDetail" /> as
        /// this one should be put in the <see cref="TriggerState.Complete" /> state.
        /// </summary>
        SetAllJobTriggersComplete,

        /// <summary>
        /// Instructs the <see cref="IScheduler" /> that all <see cref="Trigger" />
        /// s referencing the same <see cref="JobDetail" /> as
        /// this one should be put in the <see cref="TriggerState.Error" /> state.
        /// </summary>
        SetAllJobTriggersError,

        /// <summary>
        /// Instructs the <see cref="IScheduler" /> that the <see cref="Trigger" />
        /// should be put in the <see cref="TriggerState.Error" /> state.
        /// </summary>
        SetTriggerError
    }
}
