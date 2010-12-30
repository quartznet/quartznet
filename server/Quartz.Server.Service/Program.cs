using System.ServiceProcess;

namespace Quartz.Server.Service
{
	/// <summary>
	/// Summary description for Program.
	/// </summary>
	public class Program
	{
		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		static void Main()
		{
			ServiceBase[] ServicesToRun;

			ServicesToRun = new ServiceBase[] { new QuartzService() };

			ServiceBase.Run(ServicesToRun);
		}
	}
}
