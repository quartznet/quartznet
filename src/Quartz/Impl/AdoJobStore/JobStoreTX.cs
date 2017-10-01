#region License
/* 
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;

using Quartz.Logging;
using Quartz.Spi;

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// <see cref="JobStoreTX" /> is meant to be used in a standalone environment.
    /// Both commit and rollback will be handled by this class.
    /// </summary>
    /// <author><a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a></author>
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    public class JobStoreTX : JobStoreSupport
    {
        /// <summary>
        /// Called by the QuartzScheduler before the <see cref="IJobStore"/> is
        /// used, in order to give the it a chance to Initialize.
        /// </summary>
        public override Task Initialize(
            ITypeLoadHelper loadHelper, 
            ISchedulerSignaler signaler,
            CancellationToken cancellationToken = default)
        {
            base.Initialize(loadHelper, signaler, cancellationToken);
            Log.Info("JobStoreTX initialized.");
            return TaskUtil.CompletedTask;
        }

        /// <summary>
        /// For <see cref="JobStoreTX" />, the non-managed TX connection is just 
        /// the normal connection because it is not CMT.
        /// </summary> 
        /// <seealso cref="JobStoreSupport.GetConnection()" />
        protected override ConnectionAndTransactionHolder GetNonManagedTXConnection()
        {
            return GetConnection();
        }

        /// <summary>
        /// Execute the given callback having optionally acquired the given lock.
        /// For <see cref="JobStoreTX" />, because it manages its own transactions
        /// and only has the one datasource, this is the same behavior as 
        /// <see cref="JobStoreSupport.ExecuteInNonManagedTXLock" />.
        /// </summary>
        /// <param name="lockName">
        /// The name of the lock to acquire, for example "TRIGGER_ACCESS". 
        /// If null, then no lock is acquired, but the lockCallback is still
        /// executed in a transaction.
        /// </param>
        /// <param name="txCallback">Callback to execute.</param>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        /// <returns></returns>
        /// <seealso cref="JobStoreSupport.ExecuteInNonManagedTXLock" />
        /// <seealso cref="JobStoreSupport.GetNonManagedTXConnection()" />
        /// <seealso cref="JobStoreSupport.GetConnection()" />
        protected override Task<T> ExecuteInLock<T>(
            string lockName, 
            Func<ConnectionAndTransactionHolder, Task<T>> txCallback,
            CancellationToken cancellationToken = default)
        {
            return ExecuteInNonManagedTXLock(lockName, txCallback, cancellationToken);
        }
    }
}
