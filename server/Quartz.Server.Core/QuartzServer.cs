using System;
using System.Threading;

using Common.Logging;

using Quartz.Impl;

namespace Quartz.Server.Core
{
	/// <summary>
	/// The main server logic.
	/// </summary>
	public class QuartzServer : IQuartzServer
	{
		private readonly ILog logger = LogManager.GetLogger(typeof(QuartzServer));
		private ISchedulerFactory schedulerFactory;
		private IScheduler scheduler;

		/// <summary>
		/// Initializes the instance of the <see cref="QuartzServer"/> class.
		/// </summary>
		public virtual void Initialize()
		{
			try
			{				
				schedulerFactory = new StdSchedulerFactory();
				scheduler = schedulerFactory.GetScheduler();
			}
			catch (Exception e)
			{
				logger.Error("Scheduler initialization failed:" + e.Message, e);
				throw;
			}
		}


		/// <summary>
		/// Starts this instance.
		/// </summary>
		public virtual void Start()
		{
			scheduler.Start();

			try 
			{
				Thread.Sleep(3000);
			} 
			catch (ThreadInterruptedException) 
			{
			}

			logger.Info("Scheduler started successfully");
		}

		/// <summary>
		/// Stops this instance.
		/// </summary>
		public virtual void Stop()
		{
			scheduler.Shutdown(true);
			logger.Info("Scheduler shutdown complete");
		}

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
	    public virtual void Dispose()
	    {
	        // no-op for now
	    }
	}
}
