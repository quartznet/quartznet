using System;

namespace Quartz.Server.Core
{
    /// <summary>
    /// Service interface for core Quartz.NET server.
    /// </summary>
    public interface IQuartzServer : IDisposable
    {
        /// <summary>
        /// Initializes the instance of the <see cref="QuartzServer"/> class.
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