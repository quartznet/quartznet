using System;
using System.Data;
using System.Data.SqlClient;

using Quartz.Impl.AdoJobStore;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Impl.AdoJobStore
{
    ///<summary>
    /// <see cref="JobStoreCMT" /> is meant to be used in an application-server
    /// or other software framework environment that provides 
    /// container-managed-transactions. No commit / rollback will be handled by this class.
    /// </summary>
    /// <remarks>
    /// If you need commit / rollback, use <see cref="JobStoreTX" />
    /// instead.
    /// </remarks>
    /// <author><a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a></author>
    /// <author>James House</author>
    /// <author>Srinivas Venkatarangaiah</author>
    public class JobStoreCMT : JobStoreSupport
    {
        /// <summary>
        /// Called by the QuartzScheduler before the <see cref="IJobStore"/> is
        /// used, in order to give the it a chance to Initialize.
        /// </summary>
        /// <param name="loadHelper"></param>
        /// <param name="signaler"></param>
        public override void Initialize(ITypeLoadHelper loadHelper, ISchedulerSignaler signaler)
        {
            if (LockHandler == null)
            {
                // If the user hasn't specified an explicit lock handler, 
                // then we ///must/// use DB locks with CMT...
                UseDBLocks = true;
            }

            base.Initialize(loadHelper, signaler);

            Log.Info("JobStoreCMT initialized.");
        }

        /// <summary>
        /// Called by the QuartzScheduler to inform the <see cref="IJobStore" /> that
        /// it should free up all of it's resources because the scheduler is
        /// shutting down.
        /// </summary>
        public override void Shutdown()
        {

            base.Shutdown();

            try
            {
                DBConnectionManager.Instance.Shutdown(DataSource);
            }
            catch (SqlException sqle)
            {
                Log.Warn("Database connection shutdown unsuccessful.", sqle);
            }
        }

        /// <summary>
        /// Gets the non managed TX connection.
        /// </summary>
        /// <returns></returns>
        protected override ConnectionAndTransactionHolder GetNonManagedTXConnection()
        {
            IDbConnection conn;
            try
            {
                conn = DBConnectionManager.Instance.GetConnection(DataSource);
            }
            catch (SqlException sqle)
            {
                throw new JobPersistenceException(
                    string.Format("Failed to obtain DB connection from data source '{0}': {1}", DataSource, sqle), sqle);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException(
                    string.Format("Failed to obtain DB connection from data source '{0}': {1}", DataSource, e), e,
                    SchedulerException.ErrorPersistenceCriticalFailure);
            }

            if (conn == null)
            {
                throw new JobPersistenceException(
                    string.Format("Could not get connection from DataSource '{0}'", DataSource));
            }

            return new ConnectionAndTransactionHolder(conn, null);
        }

        /// <summary>
        /// Execute the given callback having optionally aquired the given lock.  
        /// Because CMT assumes that the connection is already part of a managed
        /// transaction, it does not attempt to commit or rollback the 
        /// enclosing transaction.
        /// </summary>
        /// <seealso cref="JobStoreSupport.ExecuteInNonManagedTXLock(string, JobStoreSupport.ITransactionCallback)" />
        /// <seealso cref="JobStoreTX.ExecuteInLock(String, JobStoreSupport.ITransactionCallback)" />
        /// <seealso cref="JobStoreSupport.GetNonManagedTXConnection()" />
        /// <seealso cref="JobStoreSupport.GetConnection()" />
        /// <param name="lockName">
        /// The name of the lock to aquire, for example 
        /// "TRIGGER_ACCESS".  If null, then no lock is aquired, but the
        /// txCallback is still executed in a transaction.
        /// </param>
        /// <param name="txCallback">Callback to execute.</param>
        protected override object ExecuteInLock(
                string lockName,
                ITransactionCallback txCallback)
        {
            bool transOwner = false;
            ConnectionAndTransactionHolder conn = null;
            try
            {
                if (lockName != null)
                {
                    // If we aren't using db locks, then delay getting DB connection 
                    // until after aquiring the lock since it isn't needed.
                    if (LockHandler.RequiresConnection)
                    {
                        conn = GetNonManagedTXConnection();
                    }

                    transOwner = LockHandler.ObtainLock(DbMetadata, conn, lockName);
                }

                if (conn == null)
                {
                    conn = GetNonManagedTXConnection();
                }

                return txCallback.Execute(conn);
            }
            finally
            {
                try
                {
                    ReleaseLock(conn, LockTriggerAccess, transOwner);
                }
                finally
                {
                    CleanupConnection(conn);
                }
            }
        }
    }
}
