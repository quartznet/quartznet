using System.ServiceProcess;

using Common.Logging;

using Quartz.Server.Core;

namespace Quartz.Server.Service
{
    /// <summary>
    /// Main windows service to delegate calls to <see cref="IQuartzServer" />.
    /// </summary>
	public class QuartzService : ServiceBase
	{
		private readonly ILog logger;
		private readonly IQuartzServer server;

        /// <summary>
        /// Initializes a new instance of the <see cref="QuartzService"/> class.
        /// </summary>
		public QuartzService()
		{
            logger = LogManager.GetLogger(GetType());

            logger.Debug("Obtaining instance of an IQuartzServer");
		    server = QuartzServerFactory.CreateServer();

			logger.Debug("Initializing server");
            server.Initialize();
            logger.Debug("Server initialized");
		}

	    /// <summary>
		/// Clean up any resources being used.
		/// </summary>
		protected override void Dispose( bool disposing )
		{
			if(disposing)
			{
                logger.Debug("Disposing service");
                server.Dispose();
                logger.Debug("Service disposed");
			}
			base.Dispose( disposing );
		}

		/// <summary>
		/// Set things in motion so your service can do its work.
		/// </summary>
		protected override void OnStart(string[] args)
		{
            logger.Debug("Starting service");
			server.Start();
            logger.Debug("Service started");
		}
 
		/// <summary>
		/// Stop this service.
		/// </summary>
		protected override void OnStop()
		{
            logger.Debug("Stopping service");
            server.Stop();
            logger.Debug("Service stopped");
        }
	}
}
