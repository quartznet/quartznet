using System;
using System.Collections;
using System.ComponentModel;
using System.Configuration.Install;
using System.ServiceProcess;

using Common.Logging;

using Microsoft.Win32;

using Quartz.Server.Core;

namespace Quartz.Server.Service
{
	/// <summary>
	/// Service installer for the Quartz server.
	/// </summary>
	[RunInstaller(true)]
	public class QuartzServiceInstaller : Installer
	{
		private ServiceProcessInstaller serviceProcessInstaller;
		private ServiceInstaller serviceInstaller;
		private static readonly ILog logger = LogManager.GetLogger(typeof(QuartzServiceInstaller));

		public QuartzServiceInstaller()
		{
            Console.WriteLine("fasfafasfa");
				
			// This call is required by the Designer.
			InitializeComponent();

			serviceProcessInstaller.Account = ServiceAccount.LocalSystem;

			serviceInstaller.ServiceName = Configuration.ServiceName;
			serviceInstaller.DisplayName = Configuration.ServiceDisplayName;

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
		/// Overriden to get more control over service installation.
		/// </summary>
		/// <param name="stateServer"></param>
		public override void Install(IDictionary stateServer)
		{
			RegistryKey system;
            
			//HKEY_LOCAL_MACHINE\Services\CurrentControlSet
			RegistryKey currentControlSet;
            
			//...\Services
			RegistryKey services;
            
			//...\<Service Name>
			RegistryKey service;
            
			// ...\Parameters - this is where you can put service-specific configuration
			// Microsoft.Win32.RegistryKey config;

			try
			{
                Console.WriteLine("1");
				//Let the project installer do its job
				base.Install(stateServer);

                Console.WriteLine("2");
                //Open the HKEY_LOCAL_MACHINE\SYSTEM key
				system = Registry.LocalMachine.OpenSubKey("System");
				//Open CurrentControlSet
                Console.WriteLine("3");
				currentControlSet = system.OpenSubKey("CurrentControlSet");
				//Go to the services key
				services = currentControlSet.OpenSubKey("Services");

                Console.WriteLine("4");
                //Open the key for your service, and allow writing
				service = services.OpenSubKey(serviceInstaller.ServiceName, true);
                Console.WriteLine("5");
				//Add your service's description as a REG_SZ value named "Description"
				service.SetValue("Description", Configuration.ServiceDescription);
                Console.WriteLine("6");
				//(Optional) Add some custom information your service will use...
				// config = service.CreateSubKey("Parameters");
			}
			catch (Exception e)
			{
				logger.Error("Error installing Quartz service: " + e.Message, e);
				throw;
			}
		}

		#region Component Designer generated code

		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			this.serviceProcessInstaller = new System.ServiceProcess.ServiceProcessInstaller();
			this.serviceInstaller = new System.ServiceProcess.ServiceInstaller();
			// 
			// serviceProcessInstaller
			// 
			this.serviceProcessInstaller.Password = null;
			this.serviceProcessInstaller.Username = null;
			// 
			// ProjectInstaller
			// 
			this.Installers.AddRange(new System.Configuration.Install.Installer[]
										 {
											 this.serviceProcessInstaller,
											 this.serviceInstaller
										 });
		}

		#endregion
	}
}
