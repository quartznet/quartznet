namespace Quartz.Server
{
    /// <summary>
    /// Service interface for core Quartz.NET server.
    /// </summary>
    public interface IQuartzServer
    {
        /// <summary>
        /// Initializes the instance of <see cref="IQuartzServer"/>.
        /// Initialization will only be called once in server's lifetime.
        /// </summary>
        void Initialize();

        /// <summary>
        /// Starts this instance.
        /// </summary>
        void Start();

        /// <summary>
        /// Stops this instance.
        /// </summary>
        void Stop();

        /// <summary>
        /// Pauses all activity in scheduler.
        /// </summary>
        void Pause();

        /// <summary>
        /// Resumes all activity in server.
        /// </summary>
        void Resume();
    }
}
