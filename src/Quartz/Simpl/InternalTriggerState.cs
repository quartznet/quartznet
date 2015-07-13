namespace Quartz.Simpl
{
    /// <summary>
    /// Possible internal trigger states 
    /// in RAMJobStore
    /// </summary>
    public enum InternalTriggerState
    {
        /// <summary>
        /// Waiting 
        /// </summary>
        Waiting,

        /// <summary>
        /// Acquired
        /// </summary>
        Acquired,

        /// <summary>
        /// Executing
        /// </summary>
        Executing,

        /// <summary>
        /// Complete
        /// </summary>
        Complete,

        /// <summary>
        /// Paused
        /// </summary>
        Paused,

        /// <summary>
        /// Blocked
        /// </summary>
        Blocked,

        /// <summary>
        /// Paused and Blocked
        /// </summary>
        PausedAndBlocked,

        /// <summary>
        /// Error
        /// </summary>
        Error
    }
}