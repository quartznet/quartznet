using System;
using System.Threading;

using Common.Logging;

using Quartz.Impl;

namespace Quartz.Server.Core
{
	/// <summary>
	/// The main server logic.
	/// </summary>
	public class QuartzServer
	{
		private readonly ILog logger = LogManager.GetLogger(typeof(QuartzServer));
		private ISchedulerFactory schedulerFactory;
		private IScheduler scheduler;

		/// <summary>
		/// Initializes the instance of the <see cref="QuartzServer"/> class.
		/// </summary>
		public void Initialize()
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
		public void Start()
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
		public void Stop()
		{
			scheduler.Shutdown(true);
			logger.Info("Scheduler shutdown complete");
		}

	}
}
