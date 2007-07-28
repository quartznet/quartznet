using System;
using System.Data;
using System.Threading;

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// Provide thread/resource locking in order to protect
    /// resources from being altered by multiple threads at the same time using
    /// a db row update.
    /// 
    /// <p>
    /// <b>Note:</b> This Semaphore implementation is useful for databases that do
    /// not support row locking via "SELECT FOR UPDATE" type syntax, for example
    /// Microsoft SQLServer (MSSQL).
    /// </p> 
    /// </summary>
    public class UpdateLockRowSemaphore : DBSemaphore
    {
        /*
     * ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
     *
     * Constants.
     *
     * ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
     */

        public static readonly string UPDATE_FOR_LOCK =
            string.Format("UPDATE {0}{1} SET {2} = {3} WHERE {4} = @lockName", TABLE_PREFIX_SUBST, TABLE_LOCKS, COL_LOCK_NAME,
                          COL_LOCK_NAME, COL_LOCK_NAME);


        /*
     * ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
     *
     * Constructors.
     *
     * ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
     */

        /// <summary>
        /// Initializes a new instance of the <see cref="UpdateLockRowSemaphore"/> class.
        /// </summary>
        /// <param name="dbProvider">The db provider.</param>
        public UpdateLockRowSemaphore(IDbProvider dbProvider)
            : base(DEFAULT_TABLE_PREFIX, null, UPDATE_FOR_LOCK, dbProvider)
        {
        }

     /*
     * ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
     *
     * Interface.
     *
     * ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
     */

        /**
     * Execute the SQL select for update that will lock the proper database row.
     */

        protected override void ExecuteSQL(ConnectionAndTransactionHolder conn, string lockName, string expandedSQL)
        {
            try
            {
                using (IDbCommand cmd = AdoUtil.PrepareCommand(conn, expandedSQL))
                {
                    AdoUtil.AddCommandParameter(cmd, 1, "lockName", lockName);

                    if (Log.IsDebugEnabled)
                    {
                        Log.Debug("Lock '" + lockName + "' is being obtained: " + Thread.CurrentThread.Name);
                    }

                    int numUpdate = cmd.ExecuteNonQuery();

                    if (numUpdate < 1)
                    {
                        throw new Exception(
                            Util.ReplaceTablePrefix(
                                "No row exists in table " + TABLE_PREFIX_SUBST + TABLE_LOCKS + " for lock named: " +
                                lockName, TablePrefix));
                    }
                }
            }
            catch (Exception sqle)
            {
                if (Log.IsDebugEnabled)
                {
                    Log.Debug(
                        "Lock '" + lockName + "' was not obtained by: " +
                        Thread.CurrentThread.Name);
                }

                throw new LockException(
                    "Failure obtaining db row lock: " + sqle.Message, sqle);
            }
        }

        protected string UpdateLockRowSQL
        {
            get { return SQL; }
            set { SQL = value; }
        }
    }
}