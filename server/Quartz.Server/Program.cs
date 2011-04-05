using Topshelf;

namespace Quartz.Server
{
    /// <summary>
    /// The server's main entry point.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Main.
        /// </summary>
        /// <param name="args"></param>
        public static void Main(string[] args)
        {
            Host host = HostFactory.New(x =>   
            {
                x.Service<QuartzServer>(s =>               
                {
                    s.SetServiceName("quartz.server");                                
                    s.ConstructUsing(builder =>
                                            {
                                                QuartzServer server = new QuartzServer();
                                                server.Initialize();
                                                return server;
                                            });  
                    s.WhenStarted(server => server.Start());
                    s.WhenPaused(server => server.Pause());
                    s.WhenContinued(server => server.Resume());
                    s.WhenStopped(server => server.Stop());             
                });
                x.RunAsLocalSystem();                            

                x.SetDescription(Configuration.ServiceDescription);        
                x.SetDisplayName(Configuration.ServiceDisplayName);                      
                x.SetServiceName(Configuration.ServiceName);                       
            });

            host.Run();
        }

    }
}
