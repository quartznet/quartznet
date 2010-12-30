using System;

namespace Quartz.Server.Core
{
    /// <summary>
    /// Service interface for core Quartz.NET server.
    /// </summary>
    public interface IQuartzServer : IDisposable
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

    }
}