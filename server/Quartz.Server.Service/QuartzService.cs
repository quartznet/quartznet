using System.ServiceProcess;

using Common.Logging;

using Quartz.Server.Core;

namespace Quartz.Server.Service
{
	public class QuartzService : ServiceBase
	{
		private static readonly ILog logger = LogManager.GetLogger(typeof (QuartzService));
		private QuartzServer server = new QuartzServer();

		public QuartzService()
		{
			server.Initialize();
		}



		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		protected override void Dispose( bool disposing )
		{
			if( disposing )
			{
			}
			base.Dispose( disposing );
		}



		/// <summary>
		/// Set things in motion so your service can do its work.
		/// </summary>
		protected override void OnStart(string[] args)
		{
			server.Start();
		}
 
		/// <summary>
		/// Stop this service.
		/// </summary>
		protected override void OnStop()
		{
			server.Stop();
		}
	}
}
