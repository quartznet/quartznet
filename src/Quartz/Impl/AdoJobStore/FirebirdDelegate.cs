namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// Delegate implementation for Firebird.
    /// </summary>
    public class FirebirdDelegate : StdAdoDelegate
    {
        /// <summary>
        /// Gets the select next trigger to acquire SQL clause.
        /// FireBird version with ROWS support.
        /// </summary>
        /// <returns></returns>
        protected override string GetSelectNextTriggerToAcquireSql(int maxCount)
        {
            return SqlSelectNextTriggerToAcquire + " ROWS " + maxCount;
        }
    }
}