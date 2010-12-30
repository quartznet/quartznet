using System;

using Quartz.Server.Core;

namespace Quartz.Server.Console
{
    /// <summary>
    /// Main entry point for Quartz.NET console server.
    /// </summary>
	class Program
	{
		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		[STAThread]
		static void Main()
		{
			IQuartzServer server;
			try
			{
				server = QuartzServerFactory.CreateServer();
				server.Initialize();
				server.Start();
			}
			catch (Exception e)
			{
				System.Console.Write("Error starting server: " + e.Message);
				System.Console.WriteLine(e.ToString());
				System.Console.WriteLine("Hit any key to close");
				System.Console.Read();
				return;
			}

			System.Console.WriteLine(Environment.NewLine);
			System.Console.WriteLine("The scheduler will now run until you type \"exit\"");
			System.Console.WriteLine("   If it was configured to export itself via remoting,");
			System.Console.WriteLine("   then other process may now use it.");
		    System.Console.WriteLine();

			while (true) 
			{
				System.Console.WriteLine("Type 'exit' to shutdown the server: ");
				if ("exit".Equals(System.Console.ReadLine())) 
				{
					break;
				}
			}

			System.Console.WriteLine(Environment.NewLine + "...Shutting down server...");

			server.Stop();

		}
	}
}
