using System;
using System.Collections.Generic;
using System.Linq;

namespace Quartz.Web.Api.Dto
{
    public class ServerDetailsDto
    {
        public ServerDetailsDto(IEnumerable<IScheduler> schedulers)
        {
            Name = Environment.MachineName;
            Address = "localhost";
            Schedulers = schedulers.Select(x => x.SchedulerName).ToList();
        }

        public string Name { get; private set; }
        public string Address { get; private set; }
        public IReadOnlyList<string> Schedulers { get; set; } 
    }
}