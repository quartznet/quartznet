using System.Collections.Generic;
using System.Linq;

using Quartz.Impl;

namespace Quartz.Web.Api.Dto
{
    public class ServerDetailsDto
    {
        public ServerDetailsDto()
        {
            Name = System.Environment.MachineName;
            Address = "localhost";
            Schedulers = SchedulerRepository.Instance.LookupAll().Select(x => x.SchedulerName).ToList();
        }

        public string Name { get; private set; }
        public string Address { get; private set; }
        public IReadOnlyList<string> Schedulers { get; set; } 
    }
}