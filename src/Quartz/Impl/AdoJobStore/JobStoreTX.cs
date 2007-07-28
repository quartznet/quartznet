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

using Quartz.Spi;

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// <code>JobStoreTX</code> is meant to be used in a standalone environment.
    /// Both commit and rollback will be handled by this class.
    /// </summary>
    /// <author><a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a></author>
    /// <author>James House</author>
    public class JobStoreTX : JobStoreSupport
    {
        /// <summary>
        /// Called by the QuartzScheduler before the <see cref="IJobStore"/> is
        /// used, in order to give the it a chance to Initialize.
        /// </summary>
        /// <param name="loadHelper"></param>
        /// <param name="signaler"></param>
        public override void Initialize(IClassLoadHelper loadHelper, ISchedulerSignaler signaler)
        {
            base.Initialize(loadHelper, signaler);
            Log.Info("JobStoreTX initialized.");
        }


        /**
         * For <code>JobStoreTX</code>, the non-managed TX connection is just 
         * the normal connection because it is not CMT.
         * 
         * @see JobStoreSupport#getConnection()
         */
        protected override ConnectionAndTransactionHolder GetNonManagedTXConnection()
        {
            return GetConnection();
        }



        /**
         * Execute the given callback having optionally aquired the given lock.
         * For <code>JobStoreTX</code>, because it manages its own transactions
         * and only has the one datasource, this is the same behavior as 
         * executeInNonManagedTXLock().
         * 
         * @param lockName The name of the lock to aquire, for example 
         * "TRIGGER_ACCESS".  If null, then no lock is aquired, but the
         * lockCallback is still executed in a transaction.
         * 
         * @see JobStoreSupport#executeInNonManagedTXLock(String, TransactionCallback)
         * @see JobStoreCMT#executeInLock(String, TransactionCallback)
         * @see JobStoreSupport#getNonManagedTXConnection()
         * @see JobStoreSupport#getConnection()
         */
        protected override object ExecuteInLock(string lockName, ITransactionCallback txCallback)
        {
            return ExecuteInNonManagedTXLock(lockName, txCallback);
        }

        // EOF
    }
}