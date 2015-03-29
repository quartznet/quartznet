using System;

using Microsoft.AspNet.SignalR;

namespace Quartz.Web.Hubs
{
    [CLSCompliant(false)]
    public class SchedulerHub : Hub
    {
        public void Send(string name, string message)
        {
            Clients.All.addMessage(name, message);
        }
    }
}