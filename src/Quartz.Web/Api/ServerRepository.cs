using System;
using System.Collections.Generic;
using System.Linq;

namespace Quartz.Web.Api
{
    public static class ServerRepository
    {
        private static List<Server> servers;

        static ServerRepository()
        {
            Initialize();
        }

        private static void Initialize()
        {
            servers = new List<Server>();
            servers.Add(new Server("localhost", "http://localhost:28682/"));
        }

        public static Server Lookup(string name)
        {
            return servers.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        public static IReadOnlyList<Server> LookupAll()
        {
            return servers;
        }
    }

    public class Server
    {
        public Server(string name, string address)
        {
            Name = name;
            Address = address;
        }

        public string Name { get; private set; }
        public string Address { get; private set; }
    }
}